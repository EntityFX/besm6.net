namespace Besm6.Assembler;

/// <summary>
/// Интерфейс хост-ассемблера БЭСМ-6. Разные реализации соответствуют
/// разным диалектам языка (MADLEN, БЭМШ, автоопределение).
/// </summary>
public interface IBesm6Assembler
{
    /// <summary>Диалект, который обслуживает данная реализация.</summary>
    AssemblyDialect Dialect { get; }

    /// <summary>
    /// Ассемблирует одно 48-битное слово из строки исходного кода.
    /// </summary>
    ulong AssembleWord(string source);

    /// <summary>
    /// Ассемблирует программу из строк исходного кода. Каждая строка → одно слово.
    /// </summary>
    AssemblyResult AssembleProgram(IEnumerable<string> source, int baseAddress = 512);
}