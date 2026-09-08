namespace Besm6.Assembler
{
    /// <summary>
    /// Двухпроходная таблица символов: сборка адресов лейблов в pass 1,
    /// разрешение в pass 2. Базовый адрес задаётся при создании.
    /// </summary>
    internal sealed class SymbolTable
    {
        private readonly Dictionary<string, int> _labels = new(StringComparer.OrdinalIgnoreCase);

        internal SymbolTable(int baseAddress)
        {
            BaseAddress = baseAddress;
        }

        internal int BaseAddress { get; }

        internal Dictionary<string, int> Labels => _labels;

        internal void CollectLabels(IEnumerable<string> lines)
        {
            int addr = BaseAddress;
            foreach (var raw in lines)
            {
                var lexed = Lexer.Tokenize(raw);
                if (lexed.Label is { Length: > 0 })
                    _labels[lexed.Label] = addr;

                if (!lexed.IsEndDirective)
                    addr++;
            }
        }

        internal bool TryResolve(string name, out int address)
        {
            if (_labels.TryGetValue(name, out int addr))
            {
                address = addr;
                return true;
            }
            address = 0;
            return false;
        }
    }
}