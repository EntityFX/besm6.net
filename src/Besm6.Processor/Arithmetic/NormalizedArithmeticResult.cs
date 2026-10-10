namespace Besm6.Core;

/// <summary>
/// Result of the shared Dubna normalization algorithm before register publication.
/// TO-3 §3.23 distinguishes the output rounding overlay from the stored low bit.
/// This value carries that distinction, not the instant of the hardware pulse.
/// Overflow must be raised after publication to retain the existing CPU contract.
/// </summary>
internal readonly record struct NormalizedArithmeticResult(
    Word48 UnroundedA, Word48 Y, bool RoundOnOutput, bool Overflow)
{
    internal Word48 A => RoundOnOutput
        ? Word48.FromInt48(UnroundedA.Value | 1UL) : UnroundedA;
}
