namespace Besm6.Assembler;

/// <summary>
/// Общий движок ассемблирования БЭСМ-6. Оркестрирует Lexer → Parser → SymbolTable
/// и кодирование через <see cref="InstructionCodec"/>. Параметризован диалектом:
/// в строгих диалектах (MADLEN/БЭМШ) посторонние мнемоники отклоняются,
/// в Auto сохраняется совместимое поведение.
/// </summary>
internal static class AssemblyEngine
{
    private const uint HalfMask = 0xFFFFFF;

    /// <summary>Ассемблирует одно 48-битное слово из строки исходного кода.</summary>
    internal static ulong AssembleWord(string source, AssemblyDialect dialect)
    {
        var lexed = Lexer.Tokenize(source);
        if (lexed.IsEmpty) return 0;

        var emptyLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ulong left = (ulong)Parser.AssembleField(lexed.Left ?? "", emptyLabels, dialect) & HalfMask;

        // Одиночное поле (без запятой) → возвращается как есть (24 бита).
        if (lexed.Right is not { Length: > 0 })
            return left;

        ulong right = (ulong)Parser.AssembleField(lexed.Right, emptyLabels, dialect) & HalfMask;
        return (left << 24) | right;
    }

    /// <summary>Ассемблирует программу: каждый рядок → одно слово.</summary>
    internal static AssemblyResult AssembleProgram(
        IEnumerable<string> lines,
        int baseAddr,
        AssemblyDialect dialect)
    {
        var input = lines.ToList();
        var symbols = new SymbolTable(baseAddr);

        // Пропуск директив выбора диалекта (*madlen, *bemsh, *assem).
        var codeLines = input.Where(l => !AssemblyDialectDetector.IsDialectDirective(l)).ToList();

        // Pass 1: адреса лейблов.
        symbols.CollectLabels(codeLines);

        // Pass 2: ассемблирование.
        var words = new List<ulong>();
        foreach (var raw in codeLines)
        {
            var lexed = Lexer.Tokenize(raw);
            if (lexed.IsEmpty || lexed.IsEndDirective) continue;

            var labels = symbols.Labels;
            ulong left = (ulong)Parser.AssembleField(lexed.Left ?? "", labels, dialect) & HalfMask;

            // Одиночное поле (без запятой) → возвращается как есть (24 бита).
            if (lexed.Right is not { Length: > 0 })
            {
                words.Add(left);
                continue;
            }

            ulong right = (ulong)Parser.AssembleField(lexed.Right, labels, dialect) & HalfMask;
            words.Add((left << 24) | right);
        }

        return new AssemblyResult { BaseAddr = baseAddr, Words = words, Labels = symbols.Labels };
    }
}