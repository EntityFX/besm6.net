using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticUnitStagesTests
{
    private static Word48 Octal(string value) => new(Convert.ToUInt64(value, 8));
    private static Word48 One => Octal("4050000000000000");
    private static Word48 Two => Octal("4110000000000000");

    private static ArithmeticUnitStages Unit(HardwareTimeline timeline) => new(timeline, new(100), One, new(123));

    private static void Start(ArithmeticUnitStages unit, uint code, PreparedArithmeticOperation input)
    {
        Assert.IsTrue(unit.TryReceiveCommand(code));
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(input, true));
        Assert.IsTrue(unit.TryStartOperation());
    }

    [TestMethod]
    public void OperandAcceptancePreservesPrerequisitesAndCapturedData()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline);
        Assert.IsTrue(unit.TryReceiveCommand(0)); // Zero is valid buffer content.
        Assert.IsFalse(unit.TryAcceptOperand(PreparedArithmeticOperation.Add(One, 0), true));
        unit.GrantCommandPermission();
        Assert.IsFalse(unit.TryAcceptOperand(PreparedArithmeticOperation.Multiply(Two, 0), false));
        Assert.IsTrue(unit.CommandPermission);
        Assert.IsFalse(unit.TryStartOperation());
        Assert.IsTrue(unit.TryReadOutput(out var original));
        Assert.AreEqual(One, original.A);
        Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.Add(One, 0), true));
        Assert.AreEqual(0U, unit.PreparedCommand);
        Assert.AreEqual(0, unit.QueuedCommands);
        Assert.IsTrue(unit.TryReceiveCommand(7));
        unit.GrantCommandPermission();
        Assert.IsFalse(unit.TryAcceptOperand(PreparedArithmeticOperation.Multiply(Two, 0), true));
        Assert.IsTrue(unit.TryStartOperation());
        Assert.IsFalse(unit.TryReadOutput(out _));
        Assert.AreEqual(0U, unit.CompleteOperation());
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(Two, result.A); // The accepted add was not overwritten.
        Assert.AreEqual(1, unit.QueuedCommands);
        Assert.IsTrue(unit.CommandPermission);
    }

    [TestMethod]
    public void SuccessorUsesCompletedAccumulatorRatherThanValueAtAcceptance()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline);
        Start(unit, 1, PreparedArithmeticOperation.Add(One, 0));
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.Multiply(Two, 0), true));
        Assert.IsFalse(unit.TryStartOperation());
        timeline.AdvanceTo(new(100)); // Synthetic completion instant, not an opcode cost.
        Assert.AreEqual(1U, unit.CompleteOperation());
        Assert.IsTrue(unit.TryStartOperation());
        timeline.AdvanceTo(new(200));
        Assert.AreEqual(2U, unit.CompleteOperation());
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(Octal("4150000000000000"), result.A); // 4, independently encoded.
        Assert.AreEqual(new HardwareInstant(100), unit.StartedAt);
        Assert.AreEqual(new HardwareInstant(200), unit.CompletedAt);
    }

    [TestMethod]
    public void SameTimeCompletionAndStartPreserveOutputRoundingOverlay()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline);
        var small = new Word48((24UL << 41) | (1UL << 39));
        Start(unit, 1, PreparedArithmeticOperation.Add(small, 0));
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        unit.TryAcceptOperand(PreparedArithmeticOperation.AddExponent(0, 0), true);
        var log = new List<string>();
        timeline.ScheduleAt(new(100), () =>
        {
            unit.CompleteOperation();
            Assert.IsTrue(unit.TryReadOutput(out var output));
            Assert.AreEqual(One, output.UnroundedA);
            Assert.AreEqual(One.Value | 1UL, output.A.Value);
            Assert.IsTrue(output.RoundOnOutput);
            log.Add("IZOP");
        });
        timeline.ScheduleAt(new(100), () =>
        {
            Assert.IsTrue(unit.TryStartOperation());
            Assert.IsFalse(unit.TryReadOutput(out _));
            log.Add("SPOP");
        });
        timeline.AdvanceTo(new(100));
        CollectionAssert.AreEqual(new[] { "IZOP", "SPOP" }, log);
        unit.CompleteOperation();
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(One.Value | 1UL, result.A.Value);
        Assert.IsFalse(result.RoundOnOutput); // Already supplied by the predecessor.
        Assert.AreEqual(Word48.Zero, result.Y);
    }

    [TestMethod]
    public void CompletionDoesNotNeedSuccessorAndCannotOccurTwice()
    {
        var unit = Unit(new());
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        Start(unit, 1, PreparedArithmeticOperation.ChangeSign(true, 0));
        Assert.AreEqual(1U, unit.CompleteOperation());
        Assert.IsNull(unit.ActiveCommand);
        Assert.IsNull(unit.PreparedCommand);
        Assert.IsFalse(unit.CommandPermission);
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(Octal("4020000000000000"), result.A);
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
    }

    [TestMethod]
    [DataRow(100UL)]
    [DataRow(110UL)]
    public void InvalidDivisorIsIndicatedAfterThreeSuppliedCycles(ulong cycle)
    {
        var timeline = new HardwareTimeline(new(50));
        var unit = new ArithmeticUnitStages(timeline, new(cycle), One, Word48.Zero);
        Start(unit, 1, PreparedArithmeticOperation.Divide(Word48.Zero, 0));
        Assert.IsNull(unit.Fault);
        Assert.IsFalse(unit.TryReadOutput(out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        ulong due = 50 + 3 * cycle;
        ulong checkAt = 50 + 2 * cycle + cycle / 2;
        timeline.AdvanceTo(new(checkAt - 1));
        Assert.IsNull(unit.DivisorCheckedAt);
        Assert.IsNull(unit.Fault);
        timeline.AdvanceTo(new(checkAt));
        Assert.AreEqual(new HardwareInstant(checkAt), unit.DivisorCheckedAt);
        Assert.IsNull(unit.Fault);
        Assert.IsFalse(unit.Interrupted);
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        timeline.AdvanceTo(new(due - 1));
        Assert.IsNull(unit.Fault);
        timeline.AdvanceTo(new(due)); // Guest calculation error does not escape the callback.
        Assert.AreEqual(ArithmeticUnitFaultKind.InvalidDivisor, unit.Fault!.Value.Kind);
        Assert.AreEqual(new HardwareInstant(due), unit.Fault.Value.Time);
        Assert.IsInstanceOfType<ProcessorException>(unit.Fault.Value.Cause);
        Assert.IsTrue(unit.Interrupted);
        Assert.AreEqual(1U, unit.CompleteOperation());
        Assert.IsFalse(unit.TryReadOutput(out _)); // No fabricated division result.
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        Assert.IsFalse(unit.TryAcceptOperand(PreparedArithmeticOperation.Add(One, 0), true));
        Assert.IsFalse(unit.TryStartOperation());
    }

    [TestMethod]
    [DataRow(100UL)]
    [DataRow(110UL)]
    public void ValidDivisorCheckDoesNotCreateAnErrorOrPublishOutput(ulong cycle)
    {
        var timeline = new HardwareTimeline(new(50));
        var unit = new ArithmeticUnitStages(timeline, new(cycle), One, Word48.Zero);
        Start(unit, 1, PreparedArithmeticOperation.Divide(Two, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        ulong checkAt = 50 + 2 * cycle + cycle / 2;
        timeline.AdvanceTo(new(checkAt - 1));
        Assert.IsNull(unit.DivisorCheckedAt);
        timeline.AdvanceTo(new(checkAt));
        Assert.AreEqual(new HardwareInstant(checkAt), unit.DivisorCheckedAt);
        Assert.IsNull(unit.Fault);
        Assert.IsFalse(unit.TryReadOutput(out _));
        Assert.IsNull(timeline.NextEventTime); // No fabricated whole-operation deadline.
        timeline.AdvanceTo(new(checkAt + cycle)); // Explicit test IZOP supplied by caller.
        unit.CompleteOperation();
        Assert.IsTrue(unit.TryReadOutput(out var output));
        Assert.AreEqual(Octal("4010000000000000"), output.A);
        Start(unit, 2, PreparedArithmeticOperation.Add(One, 0));
        Assert.IsNull(unit.DivisorCheckedAt);
        unit.CompleteOperation();
    }

    [TestMethod]
    public void UnnormalizedNonzeroDivisorHasTheSameDelayedIndication()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline);
        Start(unit, 1, PreparedArithmeticOperation.Divide(new((65UL << 41) | 1UL), 0));
        timeline.AdvanceTo(new(250));
        Assert.AreEqual(new HardwareInstant(250), unit.DivisorCheckedAt);
        Assert.IsNull(unit.Fault);
        timeline.AdvanceTo(new(300));
        Assert.AreEqual(ArithmeticUnitFaultKind.InvalidDivisor, unit.Fault!.Value.Kind);
        Assert.IsInstanceOfType<ProcessorException>(unit.Fault.Value.Cause);
        Assert.AreEqual(1U, unit.CompleteOperation());
        Assert.IsFalse(unit.TryReadOutput(out _));
    }

    [TestMethod]
    public void OverflowIsIndicatedAtCompletionAndKeepsTheCalculatedOutput()
    {
        var timeline = new HardwareTimeline();
        var large = new Word48((127UL << 41) | (1UL << 39));
        var unit = new ArithmeticUnitStages(timeline, new(100), large, Word48.Zero);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, 0));
        Assert.IsNull(unit.Fault);
        unit.TryReceiveCommand(2);
        unit.GrantCommandPermission();
        unit.TryAcceptOperand(PreparedArithmeticOperation.ChangeSign(false, 0), true);
        timeline.AdvanceTo(new(100));
        unit.CompleteOperation();
        Assert.AreEqual(ArithmeticUnitFaultKind.Overflow, unit.Fault!.Value.Kind);
        Assert.AreEqual(new HardwareInstant(100), unit.Fault.Value.Time);
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(1UL << 39, result.A.Value);
        Assert.AreEqual(Word48.Zero, result.Y);
        Assert.IsTrue(result.Overflow);
        Assert.IsFalse(unit.TryStartOperation());
        Assert.AreEqual(2U, unit.PreparedCommand);
    }

    [TestMethod]
    public void LegacyOverflowDisabledResultDoesNotInventAnInterruption()
    {
        var large = new Word48((127UL << 41) | (1UL << 39));
        var unit = new ArithmeticUnitStages(new(), new(100), large, Word48.Zero);
        Start(unit, 1, PreparedArithmeticOperation.AddExponent(1, (uint)RFlags.OvfDisable));
        unit.CompleteOperation();
        Assert.IsNull(unit.Fault);
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.IsFalse(result.Overflow);
        // This is the shared Dubna result flag, not a model of all TO-3 error policies.
    }

    [TestMethod]
    public void InvalidDivisorDeadlineOverflowLeavesPreparedOperationUnchanged()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 200));
        var unit = Unit(timeline);
        unit.TryReceiveCommand(1);
        unit.GrantCommandPermission();
        unit.TryAcceptOperand(PreparedArithmeticOperation.Divide(Word48.Zero, 0), true);
        Assert.ThrowsExactly<OverflowException>(() => unit.TryStartOperation());
        Assert.AreEqual(1U, unit.PreparedCommand);
        Assert.IsNull(unit.ActiveCommand);
        Assert.IsNull(timeline.NextEventTime);
        Assert.IsNull(unit.Fault);
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(One, result.A);
    }

    [TestMethod]
    public void MachineOwnsStagesAndResetCpuPreservesTheirPendingWork()
    {
        var machine = new MachineCore();
        machine.Cpu.SetA(One.Value);
        var unit = machine.CreateArithmeticUnitStages(new(100));
        Assert.AreSame(unit, machine.CreateArithmeticUnitStages(new(100)));
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.CreateArithmeticUnitStages(new(110)));
        Start(unit, 1, PreparedArithmeticOperation.Add(One, 0));
        machine.HardwareTimeline.ScheduleAt(new(100), () => unit.CompleteOperation());
        machine.ResetCpu();
        Assert.AreSame(unit, machine.CreateArithmeticUnitStages(new(100)));
        Assert.AreEqual(1U, unit.ActiveCommand);
        machine.HardwareTimeline.AdvanceTo(new(100));
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(Two, result.A);
        Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
        Assert.AreEqual(0UL, machine.Clock.Tick);
        // The internal staged unit is not yet the CPU instruction retirement driver.
    }

    [TestMethod]
    public void FailedHostCallbackDoesNotConsumeTheLaterGuestErrorIndication()
    {
        var timeline = new HardwareTimeline();
        var unit = Unit(timeline);
        var hostFailure = new InvalidOperationException("host callback");
        timeline.ScheduleAt(new(300), () => throw hostFailure);
        Start(unit, 1, PreparedArithmeticOperation.Divide(Word48.Zero, 0));
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(300)));
        Assert.AreSame(hostFailure, failure.InnerException);
        Assert.IsNull(unit.Fault);
        Assert.AreEqual(new HardwareInstant(300), timeline.NextEventTime);
        timeline.AdvanceTo(new(300));
        Assert.AreEqual(ArithmeticUnitFaultKind.InvalidDivisor, unit.Fault!.Value.Kind);
        Assert.AreEqual(1U, unit.ActiveCommand);
    }

    [TestMethod]
    public void ConstructionRejectsInvalidQuantumAndNullCalendar()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ArithmeticUnitStages(null!, new(100), One, Word48.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticUnitStages(new(), new(0), One, Word48.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticUnitStages(new(), new(101), One, Word48.Zero));
        var machine = new MachineCore();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => machine.CreateArithmeticUnitStages(new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => machine.CreateArithmeticUnitStages(new(101)));
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }
}
