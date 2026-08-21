namespace Besm6.Asm
{
    /// <summary>
    /// Дизассемблер БЭСМ-6: преобразование 48-битного слова в мнемонический код.
    /// Порт besm6_print_instruction_mnemonics из dubna/besm6_arch.cpp.
    /// </summary>
    public static class Disassembler
    {
        /// <summary>
        /// Форматировать число как восьмеричное (C# не имеет octal format specifier).
        /// </summary>
        public static string ToOctal(long value)
        {
            if (value == 0) return "0";
            var chars = new System.Text.StringBuilder();
            while (value > 0)
            {
                chars.Insert(0, (char)('0' + (value & 7)));
                value >>= 3;
            }
            return chars.ToString();
        }

        /// <summary>
        /// Дизассемблировать одно полу-слово (24 бита) в строку мнемоники.
        /// </summary>
        public static string DisasmHalf(long halfWord)
        {
            long reg = (halfWord >> 20) & 0x7;
            long opcode = (halfWord >> 12) & 0xFF;
            long addr = halfWord & 0x3FFF;

            // Бит 7 (0x80) = длинная команда, бит 6 (0x40) = расширенный адрес.
            bool isLong = (opcode & 0x80) != 0;
            bool ext = (opcode & 0x40) != 0; // 0100 octal = 0x40

            string opname;
            if (isLong)
                opname = OpcodeTable.LongMadlen[(opcode >> 3) & 0xF];
            else
                opname = OpcodeTable.ShortMadlen[opcode & 0x3F];

            if (addr == 0 && reg == 0)
                return opname;

            if (reg != 0)
                return $"{opname} {ToOctal(addr)}({ToOctal(reg)})";
            return $"{opname} {ToOctal(addr)}";
        }

        /// <summary>
        /// Дизассемблировать полное 48-битное слово (2 полу-слова).
        /// </summary>
        public static string DisasmWord(long word)
        {
            long left = (word >> 24) & 0xFFFFFFL;
            long right = word & 0xFFFFFFL;

            string leftStr = DisasmHalf(left);
            if (right == 0)
                return leftStr;
            string rightStr = DisasmHalf(right);
            return $"{leftStr},{rightStr}";
        }

        /// <summary>
        /// Дизассемблировать диапазон слов.
        /// </summary>
        public static string DisasmRange(long[] words, int start, int count)
        {
            var lines = new List<string>();
            for (int i = start; i < Math.Min(start + count, words.Length); i++)
            {
                string addrStr = ToOctal(i).PadLeft(5, '0');
                lines.Add($"{addrStr}  {DisasmWord(words[i])}");
            }
            return string.Join("\n", lines);
        }
    }
}