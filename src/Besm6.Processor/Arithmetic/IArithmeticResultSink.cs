using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Arithmetic;

/// <summary>
/// АУ — арифметическое устройство. One arithmetic algorithm, two publication paths.
/// Struct sinks are specialized by the JIT; there is no per-instruction allocation.
/// </summary>
internal interface IArithmeticResultSink
{
    void Normalize(MantissaExponent mantissa, ulong lowBits, bool round);
    void Zero();
    void ClearLowRegister();
}
