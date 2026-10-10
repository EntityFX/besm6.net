using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class DivisionControlTests
{
    [TestMethod]
    public void EveryCombinationMatchesIndependentTable44Transcription()
    {
        // Rows carry=00,01,10,11; columns sum=1,00 / 1,01 / 1,10 / 1,11.
        // '+' adds the positive divisor, '-' subtracts, '.' only shifts.
        string[] positive = ["+++.", "++.-", "+.--", ".---"];
        string[] negative = ["---.", "--.+", "-.++", ".+++"];
        foreach (bool divisorNegative in new[] { false, true })
        foreach (bool previousAdd in new[] { false, true })
        for (byte carry = 0; carry < 4; carry++)
        for (byte sum = 0; sum < 8; sum++)
        {
            char expected = sum < 4 ? previousAdd ? '+' : '-' :
                (divisorNegative ? negative : positive)[carry][sum - 4];
            var action = expected switch
            {
                '+' => DivisionAction.AddDivisor,
                '-' => DivisionAction.SubtractDivisor,
                _ => DivisionAction.ShiftOnly
            };
            Assert.AreEqual(action, DivisionControl.Decode(carry, sum, previousAdd, divisorNegative),
                $"carry={carry}, sum={sum}, previousAdd={previousAdd}, divisorNegative={divisorNegative}");
        }
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    public void InitialHistoryMatchesDividendAndDivisorSigns(bool dividendNegative, bool divisorNegative, bool expected)
    {
        var control = new DivisionControl(new(), new(100), dividendNegative, divisorNegative);
        Assert.AreEqual(expected, control.PreviousActionWasAdd);
    }

    [TestMethod]
    [DataRow(100)]
    [DataRow(110)]
    public void ControlPrecedesMainRemainderAndShiftClearsHistory(int cycle)
    {
        var timeline = new HardwareTimeline(new(17));
        var control = new DivisionControl(timeline, new((ulong)cycle), true, false);
        var add = control.Sample(0, 0);
        Assert.AreEqual(DivisionAction.AddDivisor, add.Action);
        Assert.IsTrue(add.PreviousActionWasAdd);
        Assert.IsNull(control.AvailableMainRemainder);
        timeline.AdvanceTo(new(17UL + (ulong)cycle / 2 - 1));
        Assert.IsNull(control.AvailableMainRemainder);
        timeline.AdvanceTo(add.MainRemainderAvailableAt);
        Assert.AreEqual(add, control.AvailableMainRemainder);
        Assert.IsFalse(control.HasPendingMainRemainder);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.Sample(0, 7));
        timeline.AdvanceTo(new(17UL + (ulong)cycle));
        var shift = control.Sample(0, 7);
        Assert.AreEqual(DivisionAction.ShiftOnly, shift.Action);
        Assert.IsFalse(control.PreviousActionWasAdd);
        Assert.AreEqual(add, control.AvailableMainRemainder); // Earlier main rows still visible.
        timeline.AdvanceTo(new(17UL + (ulong)cycle * 2));
        Assert.AreEqual(shift, control.AvailableMainRemainder);
        var subtract = control.Sample(0, 0);
        Assert.AreEqual(DivisionAction.SubtractDivisor, subtract.Action);
        Assert.IsFalse(subtract.PreviousActionWasAdd);
    }

    [TestMethod]
    public void StopCancelsOnlyOwnedArrivalAndRejectsLaterSamples()
    {
        var timeline = new HardwareTimeline();
        var control = new DivisionControl(timeline, new(100), false, false);
        control.Sample(1, 7);
        bool foreignRan = false;
        timeline.ScheduleAt(new(50), () => foreignRan = true);
        control.Stop();
        control.Stop();
        timeline.AdvanceTo(new(100));
        Assert.IsTrue(foreignRan);
        Assert.IsNull(control.AvailableMainRemainder);
        Assert.IsFalse(control.HasPendingMainRemainder);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.Sample(0, 0));
    }

    [TestMethod]
    public void InvalidInputsAndDeadlineOverflowLeaveHistoryAndCalendarUnchanged()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 75));
        var control = new DivisionControl(timeline, new(100), true, false);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => control.Sample(4, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => control.Sample(0, 8));
        Assert.ThrowsExactly<OverflowException>(() => control.Sample(0, 0));
        Assert.IsNull(control.LatestSample);
        Assert.IsNull(timeline.NextEventTime);
        Assert.IsTrue(control.PreviousActionWasAdd);
        Assert.IsFalse(control.HasPendingMainRemainder);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DivisionControl(timeline, new(0), false, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DivisionControl(timeline, new(101), false, false));
    }

    private static Word48 One => new(Convert.ToUInt64("4050000000000000", 8));
    private static void Start(ArithmeticUnitStages unit, PreparedArithmeticOperation operation)
    {
        Assert.IsTrue(unit.TryReceiveCommand(1));
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(operation, true));
        Assert.IsTrue(unit.TryStartOperation());
    }

    [TestMethod]
    public void UnitDoesNotPublishCommonOutputBeforeSampledMainRowsArrive()
    {
        var timeline = new HardwareTimeline();
        var unit = new ArithmeticUnitStages(timeline, new(100), One, Word48.Zero);
        Start(unit, PreparedArithmeticOperation.Divide(One, 0));
        Assert.IsNotNull(unit.Division);
        timeline.AdvanceTo(new(250)); // Existing independent divisor-check transition.
        var sample = unit.Division.Sample(2, 5);
        Assert.AreEqual(DivisionAction.ShiftOnly, sample.Action);
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.CompleteOperation());
        Assert.AreEqual(1U, unit.ActiveCommand);
        Assert.IsFalse(unit.TryReadOutput(out _));
        timeline.AdvanceTo(new(300));
        Assert.AreEqual(sample, unit.Division.AvailableMainRemainder);
        Assert.AreEqual(1U, unit.CompleteOperation());
        Assert.IsTrue(unit.TryReadOutput(out var result));
        Assert.AreEqual(One, result.A);
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.Division.Sample(0, 0));
        // Sampling control does not replace the shared arithmetic or invent IZOP.
        Start(unit, PreparedArithmeticOperation.Add(One, 0));
        Assert.IsNull(unit.Division);
    }

    [TestMethod]
    public void GeneralClearCannotDeliverOldDivisionRowsIntoNewOperation()
    {
        var timeline = new HardwareTimeline();
        var unit = new ArithmeticUnitStages(timeline, new(100), One, Word48.Zero);
        Start(unit, PreparedArithmeticOperation.Divide(One, 0));
        var old = unit.Division!;
        old.Sample(0, 0);
        unit.ApplyGeneralClearSignal();
        Assert.IsNull(unit.Division);
        Start(unit, PreparedArithmeticOperation.Divide(One, 0));
        var current = unit.Division!;
        var sample = current.Sample(0, 0);
        timeline.AdvanceTo(new(50));
        Assert.IsNull(old.AvailableMainRemainder);
        Assert.AreEqual(sample, current.AvailableMainRemainder);
        Assert.AreNotSame(old, current);
    }

    [TestMethod]
    public void DivisorSignUsesMantissaSignRatherThanExponentHighBit()
    {
        Assert.IsFalse(PreparedArithmeticOperation.Divide(new(127UL << 41), 0).DivisionOperandNegative);
        Assert.IsTrue(PreparedArithmeticOperation.Divide(new(1UL << 40), 0).DivisionOperandNegative);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = PreparedArithmeticOperation.Add(One, 0).DivisionOperandNegative);
    }
}
