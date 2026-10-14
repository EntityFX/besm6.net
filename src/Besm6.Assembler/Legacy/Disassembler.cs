namespace Besm6.Assembler.Legacy
{
    /// <summary>
    /// Устаревший дизассемблер. Делегирует <see cref="Assembler.Disassembler"/>.
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler.Disassembler")]
    public static class Disassembler
    {
        /// <summary>Форматировать число как восьмеричное.</summary>
        public static string ToOctal(long value)
            => Disassembly.Disassembler.ToOctal(value);

        /// <summary>Дизассемблировать одно полуслово (24 бита).</summary>
        public static string DisasmHalf(long halfWord)
            => Disassembly.Disassembler.DisasmHalf((uint)halfWord);

        /// <summary>Дизассемблировать полное 48-битное слово.</summary>
        public static string DisasmWord(long word)
            => Disassembly.Disassembler.DisasmWord((ulong)word);

        /// <summary>Дизассемблировать диапазон слов.</summary>
        public static string DisasmRange(long[] words, int start, int count)
            => Disassembly.Disassembler.DisasmRange(words.Select(w => (ulong)w).ToArray(), start, count);
    }
}