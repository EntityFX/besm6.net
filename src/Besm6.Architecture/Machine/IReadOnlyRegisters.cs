using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine;

/// <summary>Register observation without exposing writable storage.</summary>
public interface IReadOnlyRegisters
{
    uint K { get; }
    Word48 A { get; }
    Word48 Y { get; }
    uint R { get; }
    ReadOnlySpan<uint> M { get; }
    uint C { get; }
    bool ApplyC { get; }
    uint EffectiveAddress { get; }
}
