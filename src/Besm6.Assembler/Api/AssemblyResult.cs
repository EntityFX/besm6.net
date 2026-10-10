namespace Besm6.Assembler;

/// <summary>
/// Результат ассемблирования программы: базовый адрес, слова и таблица лейблов.
/// </summary>
public sealed class AssemblyResult
{
    /// <summary>Базовый адрес, с которого размещается программа.</summary>
    public int BaseAddr { get; init; }

    /// <summary>Ассемблированные 48-битные слова программы.</summary>
    public List<ulong> Words { get; init; } = new();

    /// <summary>Таблица лейблов: имя → адрес.</summary>
    public Dictionary<string, int> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}