using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class ProcessorOwnershipTests
{
    private static Word48 One => new(Convert.ToUInt64("4050000000000000", 8));
    private static Word48 Two => new(Convert.ToUInt64("4110000000000000", 8));

    [TestMethod]
    public void FunctionalBlocksDoNotConstructOrAdvancePhysicalModel()
    {
        var machine = new MachineCore();
        machine.LoadProgram([new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), stop"))], 1);
        Assert.IsNull(machine.ExistingHardwareModel);
        long completed = 0;
        Assert.IsTrue(machine.ExecuteBlock(20, ref completed));
        Assert.AreEqual(2L, completed);
        Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.AreEqual(1U, machine.Cpu.GetM(1));
        Assert.IsNull(machine.ExistingHardwareModel);
        Assert.AreSame(machine.Simulation.Clock, machine.Clock);
        Assert.AreSame(machine.Simulation.Scheduler, machine.Scheduler);
    }

    [TestMethod]
    public void FunctionalInstructionsAndPhysicalPreparationHaveSeparateTransientState()
    {
        var machine = new MachineCore();
        machine.LoadProgram([new Word48(Besm6.Asm.Assembler.Asm("xta 3, stop"))], 1);
        machine.Memory.Write(3, Two);
        machine.Cpu.SetA(One.Value);
        var unit = machine.CreateArithmeticUnitStages(new(100));
        var model = machine.HardwareModel;
        Assert.AreSame(model.Timeline, machine.HardwareTimeline);
        Assert.IsTrue(unit.TryReadOutput(out var initial));
        Assert.AreEqual(One, initial.A);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(Two, machine.Cpu.GetA());
        Assert.AreEqual(new HardwareInstant(0), model.Timeline.Now);
        Assert.IsTrue(unit.TryReadOutput(out var prepared));
        Assert.AreEqual(initial, prepared);
        unit.ApplyGeneralClearSignal();
        Assert.IsTrue(unit.TryReadOutput(out var cleared));
        Assert.AreEqual(Word48.Zero, cleared.A);
        Assert.AreEqual(Two, machine.Cpu.GetA()); // No physical retirement driver has issued a CPU write.
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(Two, machine.Memory.Read(3));
    }

    [TestMethod]
    public void CpuResetPreservesBothOwnersDiagnosticsAndPendingPhysicalWork()
    {
        var machine = new MachineCore();
        var simulation = machine.Simulation;
        var model = machine.HardwareModel;
        int events = 0, traces = 0;
        Action<int, ulong> trace = (_, _) => traces++;
        machine.StepTrace = trace;
        model.Timeline.ScheduleAt(new(100), () => events++);
        machine.ResetCpu();
        Assert.AreSame(simulation, machine.Simulation);
        Assert.AreSame(model, machine.HardwareModel);
        Assert.AreSame(trace, machine.StepTrace);
        Assert.AreSame(trace, simulation.StepTrace);
        model.Timeline.AdvanceTo(new(100));
        Assert.AreEqual(1, events);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        machine.LoadProgram([new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), stop"))], 1);
        machine.Step();
        Assert.AreEqual(1, traces);
        Assert.AreEqual(new HardwareInstant(100), model.Timeline.Now);
    }

    [TestMethod]
    public void ModelOwnedCallbacksStillGuardBothExecutionEntryPoints()
    {
        var machine = new MachineCore();
        machine.HardwareModel.Timeline.ScheduleAt(new(10), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Simulation.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Simulation.Scheduler.AdvanceTo(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Simulation.Clock.Advance(1));
        });
        machine.HardwareTimeline.AdvanceTo(new(10));
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void InvalidArithmeticConfigurationDoesNotCreatePhysicalOwner()
    {
        var machine = new MachineCore();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => machine.CreateArithmeticUnitStages(new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => machine.CreateArithmeticUnitStages(new(101)));
        Assert.IsNull(machine.ExistingHardwareModel);
    }
}
