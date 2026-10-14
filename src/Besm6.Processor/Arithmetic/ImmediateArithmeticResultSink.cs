using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Arithmetic;

internal readonly struct ImmediateArithmeticResultSink(NormalizationAndRounding normalizer)
    : IArithmeticResultSink
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Normalize(MantissaExponent mantissa, ulong lowBits, bool round)
        => normalizer.NormalizeAndRound(mantissa, lowBits, round);

    public void Zero() => normalizer.PublishZero();

    public void ClearLowRegister() => normalizer.ClearLowRegister();
}
