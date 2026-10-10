namespace Besm6.Runtime.Timing;

/// <summary>
/// АУ: formation of the two quotient components, TO-3 §4.16, sheets 93–102.
/// Forty-one formation bits plus one additional rounding bit per component.
/// This describes explicit shift/formation pulses, not their start time or the
/// relationship between the counter preset 40 and the 42 generated digits.
/// </summary>
internal readonly record struct DivisionQuotientParts
{
    internal const int Width = 42;
    private const ulong Mask = (1UL << Width) - 1;
    internal ulong Positive { get; }
    internal ulong Negative { get; }
    internal int DigitsShifted { get; }

    private DivisionQuotientParts(ulong positive, ulong negative, int digits)
    { Positive = positive; Negative = negative; DigitsShifted = digits; }

    internal DivisionQuotientParts Shift(DivisionAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (DigitsShifted == Width)
            throw new InvalidOperationException("All 42 quotient digits have already been shifted.");
        // АУ: subtraction (-ЧВР) records a positive quotient digit; addition
        // (+ЧВР) records a negative one. Shift-only records neither.
        return new(((Positive << 1) | (action == DivisionAction.SubtractDivisor ? 1UL : 0)) & Mask,
            ((Negative << 1) | (action == DivisionAction.AddDivisor ? 1UL : 0)) & Mask,
            DigitsShifted + 1);
    }

    internal DivisionQuotientFormation Form()
    {
        if (DigitsShifted != Width)
            throw new InvalidOperationException("Formation requires the 42 quotient digits.");
        ulong positiveMain = Positive >> 1;
        ulong negativeMain = Negative >> 1;
        // TO-3 sheet 96: rounding occurs only when the first main bit of
        // either component is one. The positive additional bit sets its sign.
        int rounding = ((positiveMain | negativeMain) & 1) == 0 ? 0 :
            (Positive & 1) != 0 ? 1 : -1;
        return new(positiveMain, negativeMain, rounding);
    }
}

/// <summary>
/// Inputs to quotient formation, before standard carry propagation and
/// normalization. Sheet 102 implements -1 rounding by suppressing the inverse-
/// to-two's-complement unit (ДПР); +1 rounding supplies an additional unit.
/// The signed values are a logical oracle, not published CPU registers.
/// </summary>
internal readonly record struct DivisionQuotientFormation(
    ulong PositiveMain, ulong NegativeMain, int RoundAdjustment)
{
    internal bool ComplementConversionUnit => RoundAdjustment != -1;
    internal bool AdditionalRoundingUnit => RoundAdjustment == 1;
    internal ulong InvertedNegativeMain => ~NegativeMain & ((1UL << 42) - 1);
    internal long MantissaBeforeRounding => (long)PositiveMain - (long)NegativeMain;
    internal long RoundedMantissa => MantissaBeforeRounding + RoundAdjustment;
}
