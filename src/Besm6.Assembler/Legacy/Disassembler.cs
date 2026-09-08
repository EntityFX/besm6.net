namespace Besm6.Asm
{
    /// <summary>
    /// Устаревший дизассемблер. Делегирует <see cref="global::Besm6.Assembler.Disassembler"/>.
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler.Disassembler")]
    public static class Disassembler
    {
        /// <summary>Форматировать число как восьмеричное.</summary>
        public static string ToOctal(long value)
            => global::Besm6.Assembler.Disassembler.ToOctal(value);

        /// <summary>Дизассемблировать одно полуслово (24 бита).</summary>
        public static string DisasmHalf(long halfWord)
            => global::Besm6.Assembler.Disassembler.DisasmHalf((uint)halfWord);

        /// <summary>Дизассемблировать полное 48-битное слово.</summary>
        public static string DisasmWord(long word)
            => global::Besm6.Assembler.Disassembler.DisasmWord((ulong)word);

        /// <summary>Дизассемблировать диапазон слов.</summary>
        public static string DisasmRange(long[] words, int start, int count)
            => global::Besm6.Assembler.Disassembler.DisasmRange(words.Select(w => (ulong)w).ToArray(), start, count);
    }
}