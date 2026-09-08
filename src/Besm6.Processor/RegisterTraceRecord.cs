namespace Besm6.Core
{
    /// <summary>
    /// Типизированная запись об изменении одного регистра процессора.
    /// </summary>
    public readonly record struct RegisterTraceRecord(string Name, ulong Value);
}
