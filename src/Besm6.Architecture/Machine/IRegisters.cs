using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine;

/// <summary>Explicit register editing. R writes retain only its six architectural bits.</summary>
public interface IRegisters : IReadOnlyRegisters
{
    new uint K { get; set; }
    new Word48 A { get; set; }
    new Word48 Y { get; set; }
    new uint R { get; set; }
    new uint C { get; set; }
    new bool ApplyC { get; set; }
    new uint EffectiveAddress { get; set; }
}
