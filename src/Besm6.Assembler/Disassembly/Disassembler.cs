namespace Besm6.Assembler
{
    /// <summary>
    /// Дизассемблер БЭСМ-6: преобразование 48-битного слова в мнемонический код
    /// через <see cref="InstructionCodec"/>. Формат: MADLEN-мнемоники, восьмеричные
    /// адреса, отрицательные адреса с «-», опущение нулевого правого полуслова.
    /// </summary>
    public static class Disassembler
    {
        /// <summary>
        /// Форматировать число как восьмеричное (без ведущего нуля).
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
        /// Дизассемблировать одно полуслово (24 бита) в строку мнемоники.
        /// Использует <see cref="InstructionCodec.DecodeHalf"/> для декодирования.
        /// </summary>
        public static string DisasmHalf(uint halfWord)
        {
            var instr = InstructionCodec.DecodeHalf(halfWord);
            string opname = GetMnemonic((int)instr.Opcode, instr.Format);

            var sb = new System.Text.StringBuilder();
            sb.Append(opname);

            if (instr.Address != 0)
            {
                sb.Append(' ');
                if (instr.Address >= 0x7FC0)
                    sb.Append('-').Append(ToOctal(((instr.Address ^ 0x7FFF) + 1)));
                else
                    sb.Append(ToOctal(instr.Address));
            }

            if (instr.Register != 0)
            {
                if (instr.Address == 0) sb.Append(' ');
                sb.Append('(').Append(ToOctal(instr.Register)).Append(')');
            }

            return sb.ToString();
        }

        /// <summary>Overload для long (совместимость с legacy API).</summary>
        public static string DisasmHalf(long halfWord) => DisasmHalf((uint)halfWord);

        /// <summary>
        /// Дизассемблировать полное 48-битное слово (два полуслова).
        /// Ноль правого полуслова опускается.
        /// </summary>
        public static string DisasmWord(ulong word)
        {
            string leftStr = DisasmHalf((uint)(word >> 24));

            // Правое полуслово нулевое → не выводится.
            if ((word & 0xFFFFFF) == 0)
                return leftStr;

            string rightStr = DisasmHalf((uint)(word & 0xFFFFFF));
            return $"{leftStr},{rightStr}";
        }

        /// <summary>Overload для long (совместимость с legacy API).</summary>
        public static string DisasmWord(long word) => DisasmWord((ulong)word);

        /// <summary>
        /// Дизассемблировать диапазон слов с номерами адресов.
        /// </summary>
        public static string DisasmRange(ulong[] words, int start, int count)
        {
            var lines = new List<string>();
            for (int i = start; i < Math.Min(start + count, words.Length); i++)
            {
                string addrStr = ToOctal(i).PadLeft(5, '0');
                lines.Add($"{addrStr}  {DisasmWord(words[i])}");
            }
            return string.Join("\n", lines);
        }

        private static string GetMnemonic(int opcode, InstructionFormat format)
        {
            if (format == InstructionFormat.Long)
                return OpcodeTable.LongMadlen[(opcode >> 3) & 0xF];
            return OpcodeTable.ShortMadlen[opcode & 0x3F];
        }
    }
}