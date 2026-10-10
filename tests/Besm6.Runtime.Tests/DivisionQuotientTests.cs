using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class DivisionQuotientTests
{
    private static DivisionQuotientParts Parts(ulong positive, ulong negative)
    {
        Assert.AreEqual(0UL, positive & negative); // A single action cannot write both digits.
        var parts = new DivisionQuotientParts();
        for (int bit = 41; bit >= 0; bit--)
            parts = parts.Shift((positive & (1UL << bit)) != 0 ? DivisionAction.SubtractDivisor :
                (negative & (1UL << bit)) != 0 ? DivisionAction.AddDivisor : DivisionAction.ShiftOnly);
        return parts;
    }

    [TestMethod]
    [DataRow("positive-a", 4L, 3L, 1L, -1, 0L)]
    [DataRow("positive-b", 4L, 1L, 2L, 0, 2L)]
    [DataRow("positive-c", 1L, 0L, 0L, 0, 0L)]
    [DataRow("positive-d", 0L, 0L, 0L, 0, 0L)]
    [DataRow("negative-a", 3L, 4L, -1L, 1, 0L)]
    [DataRow("negative-b", 2L, 4L, -1L, -1, -2L)]
    [DataRow("negative-c", 0L, 1L, 0L, 0, 0L)]
    [DataRow("negative-d", 0L, 0L, 0L, 0, 0L)]
    public void HistoricalExactDivisionTailsUseDocumentedCorrections(string example,
        long positive, long negative, long before, int correction, long after)
    {
        // TO-3 sheet 95: the printed low-bit tails of A/B for exact division.
        // These are formation fragments, not invented complete X/Y test values.
        var formation = Parts((ulong)positive, (ulong)negative).Form();
        Assert.AreEqual(before, formation.MantissaBeforeRounding, example);
        Assert.AreEqual(correction, formation.RoundAdjustment, example);
        Assert.AreEqual(after, formation.RoundedMantissa, example);
        Assert.AreEqual(correction != -1, formation.ComplementConversionUnit);
        Assert.AreEqual(correction == 1, formation.AdditionalRoundingUnit);
    }

    [TestMethod]
    [DataRow(0L, 0L, 0)]
    [DataRow(1L, 0L, 0)]
    [DataRow(0L, 1L, 0)]
    [DataRow(2L, 0L, -1)]
    [DataRow(3L, 0L, 1)]
    [DataRow(2L, 1L, -1)]
    [DataRow(0L, 2L, -1)]
    [DataRow(1L, 2L, 1)]
    [DataRow(0L, 3L, -1)]
    public void EveryReachablePairOfLowDigitsMatchesSheet96(long positive, long negative, int adjustment)
    {
        Assert.AreEqual(adjustment, Parts((ulong)positive, (ulong)negative).Form().RoundAdjustment);
    }

    [TestMethod]
    public void All42DigitsAreRetainedAndFormationDoesNotPublishOrNormalize()
    {
        var positive = Parts((1UL << 42) - 1, 0);
        Assert.AreEqual(42, positive.DigitsShifted);
        Assert.AreEqual((1UL << 42) - 1, positive.Positive);
        Assert.AreEqual(0UL, positive.Negative);
        Assert.AreEqual(1L << 41, positive.Form().RoundedMantissa); // Awaiting standard ending.
        var negative = Parts(0, (1UL << 42) - 1);
        Assert.AreEqual(-(1L << 41), negative.Form().RoundedMantissa);
        Assert.ThrowsExactly<InvalidOperationException>(() => positive.Shift(DivisionAction.ShiftOnly));
        Assert.ThrowsExactly<InvalidOperationException>(() => new DivisionQuotientParts().Form());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DivisionQuotientParts().Shift((DivisionAction)99));
    }

    [TestMethod]
    public void WeightedSignedDigitsAndPhysicalCorrectionUnitsAgree()
    {
        var random = new Random(0x642);
        const ulong mask = (1UL << 42) - 1;
        for (int vector = 0; vector < 512; vector++)
        {
            var parts = new DivisionQuotientParts();
            long rawDifference = 0, mainDifference = 0;
            DivisionAction firstMainBit = default, extraBit = default;
            for (int digit = 0; digit < 42; digit++)
            {
                var action = (DivisionAction)random.Next(3);
                int signedDigit = action == DivisionAction.SubtractDivisor ? 1 :
                    action == DivisionAction.AddDivisor ? -1 : 0;
                rawDifference += signedDigit * (1L << (41 - digit));
                if (digit < 41) mainDifference += signedDigit * (1L << (40 - digit));
                if (digit == 40) firstMainBit = action;
                if (digit == 41) extraBit = action;
                parts = parts.Shift(action);
                Assert.AreEqual(0UL, parts.Positive & parts.Negative);
            }
            Assert.AreEqual(rawDifference, (long)parts.Positive - (long)parts.Negative);
            int correction = firstMainBit == DivisionAction.ShiftOnly ? 0 :
                extraBit == DivisionAction.SubtractDivisor ? 1 : -1;
            var formation = parts.Form();
            Assert.AreEqual(mainDifference, formation.MantissaBeforeRounding);
            Assert.AreEqual(mainDifference + correction, formation.RoundedMantissa);
            ulong physicalSum = formation.PositiveMain + formation.InvertedNegativeMain +
                (formation.ComplementConversionUnit ? 1UL : 0) + (formation.AdditionalRoundingUnit ? 1UL : 0);
            Assert.AreEqual((ulong)formation.RoundedMantissa & mask, physicalSum & mask);
        }
    }

    [TestMethod]
    public void ControlRequiresAnExplicitSingleShiftPulsePerSample()
    {
        var timeline = new HardwareTimeline();
        var control = new DivisionControl(timeline, new(100), false, false);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ShiftQuotientParts());
        control.Sample(1, 7); // Positive divisor: subtract, positive digit.
        Assert.AreEqual(0, control.QuotientParts.DigitsShifted);
        control.ShiftQuotientParts();
        Assert.AreEqual(1UL, control.QuotientParts.Positive);
        Assert.AreEqual(0UL, control.QuotientParts.Negative);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ShiftQuotientParts());
        timeline.AdvanceTo(new(100));
        control.Sample(0, 6); // Add, negative digit.
        control.ShiftQuotientParts();
        Assert.AreEqual(2UL, control.QuotientParts.Positive);
        Assert.AreEqual(1UL, control.QuotientParts.Negative);
        timeline.AdvanceTo(new(200));
        control.Sample(0, 7); // Shift-only shifts both registers.
        control.ShiftQuotientParts();
        Assert.AreEqual(4UL, control.QuotientParts.Positive);
        Assert.AreEqual(2UL, control.QuotientParts.Negative);
        control.Stop();
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ShiftQuotientParts());
    }

    [TestMethod]
    public void GeneralClearDropsPartialQuotientAndNewOperationStartsWithZeroParts()
    {
        var timeline = new HardwareTimeline();
        var one = new Word48(Convert.ToUInt64("4050000000000000", 8));
        var unit = new ArithmeticUnitStages(timeline, new(100), one, Word48.Zero);
        void Start()
        {
            Assert.IsTrue(unit.TryReceiveCommand(1));
            unit.GrantCommandPermission();
            Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.Divide(one, 0), true));
            Assert.IsTrue(unit.TryStartOperation());
        }
        Start();
        var old = unit.Division!;
        old.Sample(0, 6);
        old.ShiftQuotientParts();
        Assert.AreEqual(1UL, old.QuotientParts.Negative);
        unit.ApplyGeneralClearSignal();
        Start();
        Assert.AreEqual(new DivisionQuotientParts(), unit.Division!.QuotientParts);
        Assert.ThrowsExactly<InvalidOperationException>(() => old.ShiftQuotientParts());
        timeline.AdvanceTo(new(50));
        Assert.AreEqual(0, unit.Division.QuotientParts.DigitsShifted);
    }
}
