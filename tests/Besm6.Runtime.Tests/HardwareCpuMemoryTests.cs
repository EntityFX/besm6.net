using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareCpuMemoryTests
{
    private static MramPortConfiguration Configuration => new(new(2000), new(900), new(1500), MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);
    private static MachineCore Machine(string code = "aax 20, stop")
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor); machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm(code)), false, false));
        machine.MappedMemory.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(17), true, true));
        machine.Cpu.StartAt(8); machine.Cpu.SetA(31); return machine;
    }

    [TestMethod]
    public void FetchReservesCpuWithoutDecodingUntilReturnAndStopsFunctionalDispatch()
    {
        var machine = Machine("stop, stop"); var model = machine.HardwareModel; int traces = 0;
        machine.Cpu.TraceInstruction = (_, _, _, _) => traces++;
        model.StartTimedInstructionFetch(Configuration, new(100));
        Assert.IsTrue(model.FetchWaiting); Assert.IsNull(model.CurrentInstruction); Assert.AreEqual(0, traces);
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step());
        model.AdvanceTo(new(899)); Assert.AreEqual(0, traces);
        model.AdvanceTo(new(900)); Assert.IsFalse(model.FetchWaiting); Assert.AreEqual(1, traces);
        var instruction = model.CurrentInstruction!.Value; model.ScheduleCompletionAt(instruction, new(1000));
        Assert.IsTrue(model.AdvanceTo(new(1000))!.Value.Stopped);
        Assert.AreEqual(1UL, model.CompletedInstructions); Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void TimedFetchAndOperandUseOneSharedCpuSemanticsWithoutSecondMemoryRead()
    {
        var expected = Machine(); expected.Cpu.Step(); var machine = Machine(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
        var instruction = model.CurrentInstruction!.Value;
        var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0), true);
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.RequestBoundArithmeticOperand(instruction, command);
        // Change after the magnetic cycle starts. PVR must use its accepted transfer.
        model.Timeline.ScheduleAt(new(2500), () => machine.MappedMemory!.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(18), true, true)));
        HardwareInstructionOutcome? outcome = null;
        for (int i = 0; outcome is null && i < 30; i++) outcome = model.AdvanceNextEvent();
        Assert.IsNotNull(outcome); Assert.AreEqual(HardwareInstructionStatus.Completed, outcome.Value.Status);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)), JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ResetOrHostReplacementCannotPublishStaleFetch(bool reset)
    {
        var machine = Machine(); var model = machine.HardwareModel; model.StartTimedInstructionFetch(Configuration, new(100));
        model.AdvanceTo(new(100));
        if (reset) machine.ResetCpu(); else machine.Memory.Write(8, new(Besm6.Asm.Assembler.Asm("stop, stop")));
        var outcome = model.AdvanceTo(new(2000));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, outcome!.Value.Status); Assert.IsNull(model.CurrentInstruction);
        Assert.IsFalse(machine.Cpu.HasPreparedInstruction); Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void FetchControlFailureUsesGuestCompletionBoundaryInsteadOfCallbackException()
    {
        var machine = Machine(); machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(0), true, true));
        var model = machine.HardwareModel; model.StartTimedInstructionFetch(Configuration, new(100));
        var outcome = model.AdvanceTo(new(900));
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, outcome!.Value.Status);
        Assert.AreEqual(0x140U, machine.Cpu.GetK()); Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow("a+x 20")] [DataRow("a-x 20")] [DataRow("x-a 20")] [DataRow("amx 20")]
    [DataRow("avx 20")] [DataRow("a/x 20")] [DataRow("a*x 20")]
    [DataRow("e+x 20")] [DataRow("e-x 20")]
    [DataRow("aax 20")] [DataRow("aex 20")] [DataRow("aox 20")] [DataRow("apx 20")] [DataRow("aux 20")]
    public void TransferredOperandsPreserveAllSupportedMemoryArithmeticForBothHalves(string code)
    {
        foreach (bool right in new[] { false, true })
        {
            var expected = Machine(code + ", " + code); var machine = Machine(code + ", " + code);
            foreach (var item in new[] { expected, machine })
            {
                item.Cpu.State.IsRightHalf = right; item.Cpu.SetA(Word48.FromDouble(1.25).Value);
                item.MappedMemory!.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(Word48.FromDouble(0.75), true, true));
            }
            expected.Cpu.Step(); var model = machine.HardwareModel;
            model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
            var instruction = model.CurrentInstruction!.Value;
            var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
                new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0));
            model.CreateArithmeticController(new(100)).GrantCommandPermission();
            model.RequestBoundArithmeticOperand(instruction, command);
            model.ScheduleBoundArithmeticCompletionAt(instruction, new(8000));
            Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(8000))!.Value.Status);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)), code + right);
        }
    }

    [TestMethod]
    public void GuestAssignmentChangeCancelsWaitingCpuFetchWithoutFlushingBrzImplicitly()
    {
        var machine = Machine(); var model = machine.HardwareModel; machine.MappedMemory!.Write(16, new(19));
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(100));
        machine.Cpu.Supervisor!.WriteRegister(0x10, Word48.Zero);
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceTo(new(1000))!.Value.Status);
        Assert.AreEqual(1, machine.MappedMemory.PendingWriteCount);
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void HostCanExplicitlyCancelFetchBeforeItReturnsWithoutCountingAnInstruction()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(100));
        Assert.IsTrue(model.TryCancelTimedFetch());
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceTo(new(1000))!.Value.Status);
        Assert.IsFalse(model.TryCancelTimedFetch()); Assert.AreEqual(1UL, model.CancelledInstructions);
        Assert.IsFalse(machine.Cpu.HasPreparedInstruction); Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void FetchHookFailureIsHostFailureOutsideCalendarWithReleasedLease()
    {
        var machine = Machine(); var model = machine.HardwareModel; var failure = new ApplicationException("trace");
        machine.Cpu.TraceInstruction = (_, _, _, _) => throw failure;
        model.StartTimedInstructionFetch(Configuration, new(100));
        var error = Assert.ThrowsExactly<ApplicationException>(() => model.AdvanceTo(new(900)));
        Assert.AreSame(failure, error); Assert.IsFalse(machine.Cpu.HasPreparedInstruction);
        Assert.AreEqual(HardwareInstructionStatus.HostFailure, model.LastOutcome!.Value.Status);
        Assert.AreEqual(0UL, model.CompletedInstructions); Assert.AreEqual(0UL, machine.Cpu.Supervisor!.InternalInterrupts);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void OperandHardwareWatchIsDeliveredLikeStepIncludingStackCorrection(bool stack)
    {
        var code = stack ? "aax (17), stop" : "aax 20, stop";
        var expected = Machine(code); var machine = Machine(code);
        foreach (var item in new[] { expected, machine })
        {
            item.Cpu.SetM(15, 17); item.Cpu.Supervisor!.WriteModifier(29, 16);
        }
        expected.Cpu.Step(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
        var instruction = model.CurrentInstruction!.Value;
        var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0));
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.RequestBoundArithmeticOperand(instruction, command);
        HardwareInstructionOutcome? outcome = null;
        for (int i = 0; outcome is null && i < 20; i++) outcome = model.AdvanceNextEvent();
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, outcome!.Value.Status);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)), JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Cpu.Supervisor!.Snapshot()), JsonSerializer.Serialize(machine.Cpu.Supervisor!.Snapshot()));
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void InstructionHardwareWatchIsDeliveredAtCpuBoundaryWithoutCallbackFailure()
    {
        var expected = Machine(); var machine = Machine();
        foreach (var item in new[] { expected, machine })
        {
            item.Cpu.Supervisor!.WriteModifier(28, 8);
            for (int i = 0; i < 5; i++) item.Cpu.Supervisor.CompletedInstruction();
        }
        expected.Cpu.Step(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100));
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, model.AdvanceTo(model.Timeline.Now)!.Value.Status);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)), JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Cpu.Supervisor!.Snapshot()), JsonSerializer.Serialize(machine.Cpu.Supervisor!.Snapshot()));
    }
}
