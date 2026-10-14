namespace Besm6.Architecture.Machine
{
    /// <summary>
    /// Типизированная запись об изменении одного регистра процессора.
    /// </summary>
    public readonly record struct RegisterTraceRecord(string Name, ulong Value);
}
