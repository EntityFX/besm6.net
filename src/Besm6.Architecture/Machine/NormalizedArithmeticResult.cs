using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine;

/// <summary>
/// Результат нормализации в Dubna до фиксации регистра.
/// TO-3 §3.23
/// </summary>
public readonly record struct NormalizedArithmeticResult(
    Word48 UnroundedA, Word48 Y, bool RoundOnOutput, bool Overflow)
{
    public Word48 A => RoundOnOutput
        ? Word48.FromInt48(UnroundedA.Value | 1UL) : UnroundedA;
}
