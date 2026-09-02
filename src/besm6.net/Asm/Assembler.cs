using System;
using System.Collections.Generic;

namespace Besm6.Asm
{

    /// <summary>
    /// Ассемблер БЭСМ-6: преобразование строки исходного кода в 48-битное слово.
    /// Порт dubna/assembler.cpp.
    /// </summary>
    public static class Assembler
    {
        /// <summary>
        /// Преобразовать строку ассемблера в 48-битное слово.
        /// Формат: [left_instruction] [, right_instruction] [; comment]
        /// </summary>
        public static ulong Asm(string src)
        {
            ulong left = 0, right = 0;
            int pos = 0;

            string? end = ParseInstruction(src, ref pos, ref left);
            if (end == null)
                throw new FormatException($"Bad left instruction: {src}");

            // Skip spaces.
            while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;

            if (pos < src.Length && src[pos] == ',')
            {
                pos++;
                end = ParseInstruction(src, ref pos, ref right);
                if (end == null)
                    throw new FormatException($"Bad right instruction: {src}");
            }

            // Skip spaces.
            while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;

            // Allow trailing ';' or end.
            if (pos < src.Length && src[pos] != ';' && src[pos] != '\n' && src[pos] != '\r')
                throw new FormatException($"Bad extra symbols: {src}");

            ulong word = (left << 24) | right;
            return word & 0xFFFFFFFFFFFFL; // 48-bit mask
        }

        /// <summary>
        /// Парсить одну инструкцию (полу-слово: 24 бита).
        /// Поддерживает восьмеричное и мнемоническое представление.
        /// </summary>
        private static string? ParseInstruction(string src, ref int pos, ref ulong result)
        {
            pos = SkipSpaces(src, pos);
            if (pos >= src.Length) return null;

            int opcode, reg, addr;

            if (src[pos] >= '0' && src[pos] <= '7')
            {
                // Восьмеричное представление.
                if (!TryParseOctal(src, ref pos, out reg)) return null;
                if (reg > 15) return null;

                pos = SkipSpaces(src, pos);
                if (pos >= src.Length) return null;

                if (src[pos] == '2' || src[pos] == '3')
                {
                    // Длинная команда.
                    if (!TryParseOctal(src, ref pos, out opcode)) return null;
                    if (opcode < 0x10 || opcode > 0x1F) return null; // 020..037 octal
                    opcode <<= 3;
                }
                else
                {
                    // Короткая команда.
                    if (!TryParseOctal(src, ref pos, out opcode)) return null;
                    if (opcode > 0x7F) return null; // 0177 octal = 0x7F
                }

                pos = SkipSpaces(src, pos);
                if (!TryParseOctal(src, ref pos, out addr)) return null;
                if (addr > 0x7FFF) return null; // 0177777 octal = 0x7FFF (BITS(15))
                if (opcode <= 0x7F && addr > 0xFFF) return null; // 01777 octal = 0xFFF (BITS(12))
            }
            else
            {
                // Мнемоническое представление.
                string? name = GetAlnum(src, ref pos);
                if (name == null || !OpcodeTable.TryGetOpcode(name, out opcode))
                    return null;

                int negate = 0;
                pos = SkipSpaces(src, pos);
                if (pos < src.Length && src[pos] == '-')
                {
                    negate = 1;
                    pos++;
                    pos = SkipSpaces(src, pos);
                }

                addr = 0;
                if (pos < src.Length && src[pos] >= '0' && src[pos] <= '7')
                {
                    if (!TryParseOctal(src, ref pos, out addr)) return null;
                    if (addr > 0x7FFF) return null; // BITS(15)
                    if (negate == 1)
                        addr = (-addr) & 0x7FFF;
                    if (opcode <= 0x3F && addr > 0xFFF)
                    {
                        if (addr < 0x7000) return null; // 070000 octal = 0x7000 hex — Bad short address
                        opcode |= 0x40; // 0100 octal = 0x40
                        addr &= 0xFFF;  // 01777 octal = 0xFFF
                    }
                }

                reg = 0;
                pos = SkipSpaces(src, pos);
                if (pos < src.Length && src[pos] == '(')
                {
                    pos++;
                    if (!TryParseOctal(src, ref pos, out reg)) return null;
                    if (reg > 15) return null;
                    pos = SkipSpaces(src, pos);
                    if (pos >= src.Length || src[pos] != ')') return null;
                    pos++;
                }
            }

            result = (ulong)(uint)reg << 20 | (ulong)(uint)opcode << 12 | (uint)addr;
            return src;
        }

        /// <summary>
        /// Пропустить пробелы и BOM-последовательности.
        /// </summary>
        private static int SkipSpaces(string src, int pos)
        {
            while (pos < src.Length)
            {
                if (src[pos] == ' ' || src[pos] == '\t' || src[pos] == '\r')
                {
                    pos++;
                    continue;
                }
                if (src[pos] == '#')
                    return src.Length; // comment to end
                break;
            }
            return pos;
        }

        /// <summary>
        /// Прочитать буквенно-цифровую строку (мнемоника).
        /// </summary>
        private static string? GetAlnum(string src, ref int pos)
        {
            int start = pos;
            while (pos < src.Length)
            {
                char c = src[pos];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    c == '*' || c == '/' || c == '+' || c == '-' ||
                    (c >= '0' && c <= '9') || c >= 0x80)
                {
                    pos++;
                }
                else break;
            }
            if (pos == start) return null;
            return src.Substring(start, pos - start);
        }

        /// <summary>
        /// Разобрать восьмеричное число из строки.
        /// </summary>
        private static bool TryParseOctal(string src, ref int pos, out int result)
        {
            result = 0;
            int start = pos;
            while (pos < src.Length && src[pos] >= '0' && src[pos] <= '7')
            {
                result = (result << 3) | (src[pos] - '0');
                pos++;
            }
            return pos > start;
        }
    }
}
