namespace Besm6.Assembler;

/// <summary>
/// Диалект языка ассемблера БЭСМ-6.
/// </summary>
public enum AssemblyDialect
{
    /// <summary>Автоматическое определение диалекта (совместимое поведение).</summary>
    Auto,

    /// <summary>MADLEN: английские мнемоники и числовые формы.</summary>
    Madlen,

    /// <summary>БЭМШ: русские мнемоники и директивы.</summary>
    Bemsh,
}