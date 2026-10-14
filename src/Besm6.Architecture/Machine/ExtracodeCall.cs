using Besm6.Architecture.Isa;

namespace Besm6.Architecture.Machine
{
    /// <summary>
    /// Полный контекст вызова экстракода процессором.
    /// </summary>
    public readonly record struct ExtracodeCall(
        Extracode Code,
        uint EffectiveAddress,
        byte Register,
        ushort RawAddress,
        bool IsRightHalf);
}
