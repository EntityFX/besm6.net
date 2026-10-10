using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticUnitErrorTests
{
    private static Word48 One => new(Convert.ToUInt64("4050000000000000", 8));
    private static Word48 Large => new((127UL << 41) | (1UL << 39));
    private static ArithmeticUnitStages Unit(HardwareTimeline timeline, ArithmeticErrorMode mode,
        Word48 accumulator) => new(timeline, new(100), accumulator, Word48.Zero, new ArithmeticErrorPolicy(mode));

    private static void Start(ArithmeticUnitStages unit, uint code, PreparedArithmeticOperation input)
    {
        Assert.IsTrue(unit.TryReceiveCommand(code));
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(input, true));
        Assert.IsTrue(unit.TryStartOperation());
    }

    [TestMethod]
    public void HardwareStopPolicyOwnsOverflowAndSingleOperationReleaseAllowsSuccessor()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline, ArithmeticErrorMode.Stop, Large);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, (uint)RFlags.OvfDisable));
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.ChangeSign(false, 0), true));
        timeline.AdvanceTo(new(300));
        unit.CompleteOperation();
        Assert.IsTrue(unit.Interrupted);
        Assert.IsTrue(unit.Errors!.CompletionHeld);
        Assert.IsTrue(unit.TryReadOutput(out var output));
        Assert.AreEqual(1UL << 39, output.A.Value);
        Assert.IsTrue(output.Overflow);
        Assert.IsFalse(unit.TryStartOperation());
        unit.Errors.ReleaseSingleOperation();
        Assert.IsFalse(unit.Interrupted);
        Assert.IsNotNull(unit.Fault); // Last arithmetic cause is diagnostic, not the inhibition latch.
        Assert.IsTrue(unit.TryStartOperation());
        timeline.AdvanceTo(new(600));
        Assert.AreEqual(2U, unit.CompleteOperation());
        Assert.IsFalse(unit.Interrupted);
    }

    [TestMethod]
    public void SuppressedArithmeticCauseStillExistsLocallyAndDoesNotBlockSuccessor()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline, ArithmeticErrorMode.InputControlOnly, Large);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, 0));
        timeline.AdvanceTo(new(300));
        unit.CompleteOperation();
        Assert.IsFalse(unit.Interrupted);
        Assert.IsNull(unit.Errors!.LastInterruptSample);
        Assert.AreEqual(ArithmeticErrorSignals.PositiveOverflow, unit.Errors.Signals);
        Start(unit, 2, PreparedArithmeticOperation.ChangeSign(false, 0));
        timeline.AdvanceTo(new(400));
        Assert.AreEqual(ArithmeticErrorSignals.None, unit.Errors.Signals);
        timeline.AdvanceTo(new(600));
        unit.CompleteOperation();
        Assert.IsTrue(unit.TryReadOutput(out var output));
        Assert.AreEqual(1UL << 39, output.A.Value);
    }

    [TestMethod]
    public void InvalidDivisionInContinuingPolicyDoesNotInventAnAccumulator()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline, ArithmeticErrorMode.ContinueAndReport, One);
        Start(unit, 1, PreparedArithmeticOperation.Divide(Word48.Zero, 0));
        timeline.AdvanceTo(new(300));
        unit.CompleteOperation();
        Assert.IsTrue(unit.Interrupted);
        timeline.AdvanceTo(new(400));
        Assert.IsFalse(unit.Interrupted);
        Assert.IsFalse(unit.TryReadOutput(out _));
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.Add(One, 0), true));
        Assert.IsFalse(unit.TryStartOperation()); // Physical invalid-division output is not derived yet.
        unit.ApplyGeneralClearSignal();
        Assert.AreEqual(0, unit.QueuedCommands);
        Assert.IsNull(unit.PreparedCommand);
        Assert.IsTrue(unit.TryReadOutput(out var cleared));
        Assert.AreEqual(Word48.Zero, cleared.A);
        Assert.AreEqual(Word48.Zero, cleared.Y);
        Start(unit, 3, PreparedArithmeticOperation.Add(One, 0));
        timeline.AdvanceTo(new(700));
        unit.CompleteOperation();
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(One, result.A);
    }

    [TestMethod]
    public void GeneralClearCancelsPendingDivisionAndInputChecksWithoutClearingMachineTime()
    {
        var machine = new MachineCore();
        machine.Cpu.SetA(One.Value);
        var policy = new ArithmeticErrorPolicy(ArithmeticErrorMode.Stop);
        var unit = machine.CreateArithmeticUnitStages(new(100), policy);
        unit.Errors!.CheckInput(new(0), 3);
        Start(unit, 1, PreparedArithmeticOperation.Divide(Word48.Zero, 0));
        unit.TryReceiveCommand(2);
        machine.HardwareTimeline.AdvanceTo(new(100));
        bool unrelated = false;
        machine.HardwareTimeline.ScheduleAt(new(700), () => unrelated = true);
        unit.ApplyGeneralClearSignal();
        Assert.AreEqual(new HardwareInstant(700), machine.HardwareTimeline.NextEventTime);
        machine.HardwareTimeline.AdvanceTo(new(1000));
        Assert.IsTrue(unrelated);
        Assert.IsNull(unit.Fault);
        Assert.IsNull(unit.ActiveCommand);
        Assert.IsNull(unit.DivisorCheckedAt);
        Assert.IsNull(unit.Errors.InputCheckedAt);
        Assert.IsFalse(unit.Errors.InputControlPermission);
        Assert.AreEqual(0, unit.QueuedCommands);
        Assert.IsFalse(unit.CommandPermission);
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(Word48.Zero, result.A);
        Assert.AreEqual(One, machine.Cpu.GetA()); // This pulse is not CPU publication or ResetCpu.
        Assert.AreEqual(new HardwareInstant(1000), machine.HardwareTimeline.Now);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void CpuResetPreservesErrorPolicyAndHeldInterruption()
    {
        var machine = new MachineCore();
        machine.Cpu.SetA(One.Value);
        var policy = new ArithmeticErrorPolicy(ArithmeticErrorMode.Stop);
        var unit = machine.CreateArithmeticUnitStages(new(100), policy);
        unit.Errors!.CheckInput(new(0), 4);
        Start(unit, 1, PreparedArithmeticOperation.Add(One, 0));
        machine.HardwareTimeline.AdvanceTo(new(300));
        unit.CompleteOperation();
        machine.ResetCpu();
        Assert.AreSame(unit, machine.CreateArithmeticUnitStages(new(100), policy));
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.CreateArithmeticUnitStages(new(100)));
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.CreateArithmeticUnitStages(new(100), new ArithmeticErrorPolicy(ArithmeticErrorMode.ContinueAndReport)));
        Assert.IsTrue(unit.Errors.CompletionHeld);
        Assert.IsTrue(unit.Interrupted);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
    }

    [TestMethod]
    public void CompletionDeadlineOverflowLeavesInstructionActiveAndOutputUnpublished()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 300));
        var unit = Unit(timeline, ArithmeticErrorMode.ContinueAndReport, Large);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, 0));
        timeline.AdvanceTo(new(ulong.MaxValue - 50));
        Assert.ThrowsExactly<OverflowException>(() => unit.CompleteOperation());
        Assert.AreEqual(1U, unit.ActiveCommand);
        Assert.IsNull(unit.CompletedAt);
        Assert.IsNull(unit.Fault);
        Assert.IsTrue(unit.Errors!.OperationActive);
        Assert.IsFalse(unit.TryReadOutput(out _));
    }

    [TestMethod]
    public void PreviousOverflowCauseCannotCompleteLaterInvalidDivisionBeforeUdo()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline, ArithmeticErrorMode.Stop, Large);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, 0));
        timeline.AdvanceTo(new(300));
        unit.CompleteOperation();
        unit.Errors!.ReleaseSingleOperation();
        Start(unit, 2, PreparedArithmeticOperation.Divide(Word48.Zero, 0));
        timeline.AdvanceTo(new(550));
        Assert.AreEqual(ArithmeticUnitFaultKind.Overflow, unit.Fault!.Value.Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        timeline.AdvanceTo(new(600));
        Assert.AreEqual(ArithmeticUnitFaultKind.InvalidDivisor, unit.Fault!.Value.Kind);
        Assert.AreEqual(2U, unit.CompleteOperation());
    }

    [TestMethod]
    public void HostCallbackFailureDoesNotLoseInputControlPermissionEvent()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline, ArithmeticErrorMode.Stop, One);
        var host = new InvalidOperationException("host callback");
        timeline.ScheduleAt(new(200), () => throw host);
        var bad = new MemoryWord50(MemoryWord50.Form(One, true, true).RawValue ^ (1UL << 48));
        unit.Errors!.CheckInput(bad, 3);
        Start(unit, 1, PreparedArithmeticOperation.Add(One, 0));
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(200)));
        Assert.AreSame(host, failure.InnerException);
        Assert.IsFalse(unit.Interrupted);
        Assert.AreEqual(new HardwareInstant(200), timeline.NextEventTime);
        timeline.AdvanceTo(new(300));
        Assert.IsTrue(unit.Interrupted);
        Assert.AreEqual(1U, unit.ActiveCommand);
        unit.CompleteOperation();
        Assert.AreEqual(ArithmeticErrorSignals.InputControl | ArithmeticErrorSignals.Interruption,
            unit.Errors.LastInterruptSample!.Value.Signals);
    }
}
