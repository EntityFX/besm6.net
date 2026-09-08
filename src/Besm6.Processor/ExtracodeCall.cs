namespace Besm6.Core
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
