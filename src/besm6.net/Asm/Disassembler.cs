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
            // Точный порт besm6_print_instruction_mnemonics (dubna/besm6_arch.cpp).
            long reg = (halfWord >> 20) & 0xF;           // (cmd >> 20) & 017
            bool isLong = (halfWord & (1L << 19)) != 0;  // ONEBIT(20) — бит 19
            long opcode, addr;
            if (isLong)
            {
                opcode = (halfWord >> 12) & 0xF8;        // (cmd >> 12) & 0370
                addr = halfWord & 0x7FFF;                // cmd & BITS(15)
            }
            else
            {
                opcode = (halfWord >> 12) & 0x3F;        // (cmd >> 12) & 077
                addr = halfWord & 0xFFF;                 // cmd & 07777 (12 бит)
                if ((halfWord & (1L << 18)) != 0)        // ONEBIT(19) — расширенный адрес
                    addr |= 0x7000;                       // addr |= 070000
            }

            string opname = isLong
                ? OpcodeTable.LongMadlen[(opcode >> 3) & 0xF]
                : OpcodeTable.ShortMadlen[opcode & 0x3F];

            var sb = new System.Text.StringBuilder();
            sb.Append(opname);
            if (addr != 0)
            {
                sb.Append(' ');
                if (addr >= 0x7FC0)                                  // addr >= 077700 — отрицательный
                    sb.Append('-').Append(ToOctal(((addr ^ 0x7FFF) + 1)));  // (addr ^ 077777) + 1
                else
                    sb.Append(ToOctal(addr));
            }
            if (reg != 0)
            {
                if (addr == 0) sb.Append(' ');
                sb.Append('(').Append(ToOctal(reg)).Append(')');
            }
            return sb.ToString();
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