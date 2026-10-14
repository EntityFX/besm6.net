using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Arithmetic;

internal struct PreparedArithmeticResultSink(uint mode, Word48 previousY) : IArithmeticResultSink
{
    internal NormalizedArithmeticResult Result;

    public void Normalize(MantissaExponent mantissa, ulong lowBits, bool round)
        => Result = NormalizationAndRounding.Evaluate(mantissa, lowBits, round, mode, previousY);

    public void Zero() => Result = new(Word48.Zero,
        Word48.FromInt48(previousY.Value & ~(ulong)ArchitectureConstants.BITS40), false, false);

    public void ClearLowRegister() => previousY = Word48.Zero;
}
