using System.Text.Json;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class PreparedProcessorOwnershipTests
{
    private static MachineCore Create(string command = "vtm 7(1)")
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new(Besm6.Asm.Assembler.Asm(command + ", " + command)));
        machine.Cpu.StartAt(8);
        machine.Cpu.SetA(Word48.FromDouble(1.25).Value);
        machine.Cpu.SetY(123);
        machine.Cpu.Supervisor!.Status = new(ControlUnitFlags.None);
        return machine;
    }

    [TestMethod]
    [DataRow("vtm 7(1)", false)]
    [DataRow("*50 3", false)]
    [DataRow("stop", false)]
    [DataRow("a/x 144", false)]
    [DataRow("xta 144", false)]
    [DataRow("vtm 7(1)", true)]
    public void SupervisorStateFaultsAndDiagnosticsMatchSerialStep(string command, bool fetchFault)
    {
        foreach (bool right in new[] { false, true })
        {
            var serial = Create(command);
            var model = Create(command);
            int serialCount = 0, modelCount = 0;
            serial.Cpu.InstructionExecuted = _ => serialCount++;
            model.Cpu.InstructionExecuted = _ => modelCount++;
            foreach (var machine in new[] { serial, model })
            {
                machine.Cpu.State.IsRightHalf = right;
                if (fetchFault) machine.MappedMemory!.PhysicalMemory.WriteRaw(8, new MemoryWord50(0));
            }
            bool expected = serial.Cpu.Step();
            var pending = model.Cpu.PrepareInstruction();
            bool actual = model.Cpu.CompleteInstruction(in pending);
            Assert.AreEqual(expected, actual);
            Assert.AreEqual(serial.Cpu.LastStepCompleted, model.Cpu.LastStepCompleted);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.Cpu.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(model.Cpu.State)));
            var supervisor = serial.Cpu.Supervisor!;
            var modeledSupervisor = model.Cpu.Supervisor!;
            Assert.AreEqual(supervisor.InternalInterrupts, modeledSupervisor.InternalInterrupts);
            Assert.AreEqual(supervisor.SavedFlags, modeledSupervisor.SavedFlags);
            Assert.AreEqual(supervisor.Status, modeledSupervisor.Status);
            Assert.AreEqual(supervisor.Mode, modeledSupervisor.Mode);
            foreach (int index in Enumerable.Range(0, 17).Concat(new[] { 23, 26, 27 }))
                Assert.AreEqual(supervisor.ReadModifier(index), modeledSupervisor.ReadModifier(index));
            Assert.AreEqual(serialCount, modelCount);
            Assert.AreEqual(0UL, model.Clock.Tick); // The driver, not the shared handlers, owns time.
        }
    }

    [TestMethod]
    public void PendingModelCommandBlocksFunctionalExecutionBeforeItDeliversEvents()
    {
        var machine = Create();
        int events = 0;
        machine.Scheduler.Schedule(0, () => events++);
        var pending = machine.Cpu.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Step());
        long completed = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.ExecuteBlock(5, ref completed));
        Assert.AreEqual(0L, completed);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0, events);
        machine.Cpu.CancelInstruction(in pending);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(1, events);
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void BothCalendarsRejectPreparationAndCompletionInsideCallbacks()
    {
        var machine = Create();
        var pending = machine.Cpu.PrepareInstruction();
        void Check()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.PrepareInstruction());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.CompleteInstruction(in pending));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.CancelInstruction(in pending));
        }
        machine.Scheduler.Schedule(0, Check);
        machine.HardwareTimeline.ScheduleAt(new(100), Check);
        machine.Simulation.Scheduler.AdvanceTo(0);
        machine.HardwareTimeline.AdvanceTo(new(100));
        Assert.IsTrue(machine.Cpu.HasPreparedInstruction);
        Assert.IsFalse(machine.Cpu.CompleteInstruction(in pending));
        Assert.AreEqual(7u, machine.Cpu.GetM(1));
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void InterruptDuringCalendarWaitIsAcceptedAtTheNextInstructionBoundary()
    {
        var machine = Create();
        machine.SupervisorIo!.WriteRegister(30, new(SupervisorIoController.TimerInterrupt));
        machine.Memory.Write(0x141, new(Besm6.Asm.Assembler.Asm("vtm 1(2), stop")));
        var pending = machine.Cpu.PrepareInstruction();
        machine.HardwareTimeline.ScheduleAt(new(100), () =>
            machine.SupervisorIo!.RaiseInterrupts(SupervisorIoController.TimerInterrupt));
        machine.HardwareTimeline.AdvanceTo(new(100));
        Assert.IsFalse(machine.Cpu.CompleteInstruction(in pending));
        Assert.AreEqual(7u, machine.Cpu.GetM(1));
        Assert.AreEqual(8u, machine.Cpu.GetK());
        Assert.IsTrue(machine.Cpu.RightInstruction);
        var next = machine.Cpu.PrepareInstruction();
        Assert.AreEqual(0x141u, machine.Cpu.GetK()); // External interrupt vector00501.
        machine.Cpu.CancelInstruction(in next);
    }

    [TestMethod]
    public void CallbackFailureDoesNotPublishCpuAndResetInvalidatesItsPendingLease()
    {
        var machine = Create();
        var pending = machine.Cpu.PrepareInstruction();
        var cause = new InvalidOperationException("Model callback failed");
        machine.HardwareTimeline.ScheduleAt(new(100), () => throw cause);
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => machine.HardwareTimeline.AdvanceTo(new(100)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(0u, machine.Cpu.GetM(1));
        Assert.IsFalse(machine.Cpu.LastStepCompleted);
        machine.ResetCpu();
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.CompleteInstruction(in pending));
        Assert.AreEqual(new HardwareInstant(100), machine.HardwareTimeline.Now);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        machine.Cpu.StartAt(8);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(7u, machine.Cpu.GetM(1));
    }
}
