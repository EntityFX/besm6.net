namespace Besm6.Core;

// АУ — арифметическое устройство. One arithmetic algorithm, two publication paths.
// Struct sinks are specialized by the JIT; there is no per-instruction allocation.
internal interface IArithmeticResultSink
{
    void Normalize(MantissaExponent mantissa, ulong lowBits, bool round);
    void Zero();
    void ClearLowRegister();
}

internal readonly struct ImmediateArithmeticResultSink(NormalizationAndRounding normalizer)
    : IArithmeticResultSink
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Normalize(MantissaExponent mantissa, ulong lowBits, bool round)
        => normalizer.NormalizeAndRound(mantissa, lowBits, round);

    public void Zero() => normalizer.PublishZero();

    public void ClearLowRegister() => normalizer.ClearLowRegister();
}

internal struct PreparedArithmeticResultSink(uint mode, Word48 previousY) : IArithmeticResultSink
{
    internal NormalizedArithmeticResult Result;

    public void Normalize(MantissaExponent mantissa, ulong lowBits, bool round)
        => Result = NormalizationAndRounding.Evaluate(mantissa, lowBits, round, mode, previousY);

    public void Zero() => Result = new(Word48.Zero,
        Word48.FromInt48(previousY.Value & ~(ulong)ArchitectureConstants.BITS40), false, false);

    public void ClearLowRegister() => previousY = Word48.Zero;
}
