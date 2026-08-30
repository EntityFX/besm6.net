using System;
using System.Collections.Generic;
using System.Text;

namespace Besm6.Asm
{
    /// <summary>Результат ассемблирования программы.</summary>
    public sealed class AsmResult
    {
        public int BaseAddr { get; set; }
        public List<long> Words { get; set; } = new();
        public Dictionary<string, int> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Хост-ассемблер БЭСМ-6 для MADLEN и BEMSH форматов.
    /// Поддерживает: лейблы, мнемоники, окт. данные, GOST-текст.
    /// </summary>
    public static class ProgramAssembler
    {
        /// <summary>
        /// Ассемблировать программу. Каждый рядок → 1 слово.
        /// Формат: [label] left, right
        /// </summary>
        public static AsmResult Assemble(IEnumerable<string> lines, int baseAddr = 512)
        {
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var words = new List<long>();

            // ─── Pass 1: определить адреса лейблов ───
            int addr = baseAddr;
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                string label = ExtractLabel(line, out string body);
                if (label.Length > 0)
                    labels[label] = addr;

                // Каждый рядок = 1 слово (кроме пустых и end)
                string first = FirstToken(body);
                if (first != "end" && first != "финиш" && first != "старт")
                    addr++;
            }

            // ─── Pass 2: ассемблировать ───
            addr = baseAddr;
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                string label = ExtractLabel(line, out string body);
                if (body.Length == 0) continue;

                string first = FirstToken(body);
                if (first == "end" || first == "финиш" || first == "старт")
                    continue;

                long word = AssembleWord(body, labels);
                words.Add(word & 0xFFFFFFFFFFFFL);
                addr++;
            }

            return new AsmResult { BaseAddr = baseAddr, Words = words, Labels = labels };
        }

        /// <summary>
        /// Ассемблировать одно 48-битное слово из строки.
        /// Формат MADLEN: left, right (каждое поле = 24 бита)
        /// Формат BEMSH: mnemonic addr (reg)
        /// </summary>
        private static long AssembleWord(string body, Dictionary<string, int> labels)
        {
            // MADLEN: есть запятая-разделитель → два поля
            if (body.Contains(','))
            {
                // Разбиваем на left и right по ПЕРВОЙ запятой после полей
                // Формат: [left], [right]
                int commaIdx = FindSeparatorComma(body);
                if (commaIdx > 0)
                {
                    string leftStr = body.Substring(0, commaIdx).Trim();
                    string rightStr = commaIdx + 1 < body.Length ? body.Substring(commaIdx + 1).Trim() : "";

                    long left = AssembleField(leftStr, labels);
                    long right = AssembleField(rightStr, labels);

                    return (left << 24) | right;
                }
            }

            // BEMSH: mnemonic addr (reg) — одно поле
            return AssembleField(body, labels);
        }

        /// <summary>
        /// Найти запятую-разделитель между left и right полями.
        /// Пропускает запятые внутри полей (например в тексте).
        /// </summary>
        private static int FindSeparatorComma(string body)
        {
            // В MADLEN формате: [left], [right]
            // Левое поле заканчивается до первой запятой,
            // которая НЕ является частью текста в кавычках.
            int depth = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\'') depth ^= 1; // toggle quote
                else if (c == ',' && depth == 0)
                    return i;
            }
            return body.IndexOf(',');
        }

        /// <summary>
        /// Ассемблировать одно поле (24 бита).
        /// Может быть: пусто(0), мнемоника(+addr+reg), окт. число, лейбл.
        /// </summary>
        private static long AssembleField(string s, Dictionary<string, int> labels)
        {
            s = s.Trim();
            if (s.Length == 0) return 0;

            // Убрать ведущее/хвостовое запятые
            while (s.Length > 0 && s[0] == ',') s = s.Substring(1).Trim();
            while (s.Length > 0 && s[s.Length - 1] == ',') s = s.Substring(0, s.Length - 1).Trim();
            if (s.Length == 0) return 0;

            // Восьмеричное число → данные
            if (IsOctal(s))
                return ParseOctalSafe(s) & 0xFFFFF;

            // Ссылка на лейбл → адрес
            if (labels.TryGetValue(s, out int labelAddr))
                return (long)labelAddr & 0xFFFFF;

            // Мнемоника
            if (IsMnemonicStart(s))
            {
                string mnemonic = FirstToken(s);
                string rest = s.Length > mnemonic.Length ? s.Substring(mnemonic.Length).Trim() : "";

                if (OpcodeTable.TryGetOpcode(mnemonic, out int opcode))
                {
                    long a = 0;
                    int reg = 0;

                    // Адрес
                    if (rest.Length > 0)
                    {
                        string addrTok = FirstToken(rest);
                        if (labels.TryGetValue(addrTok, out int la))
                            a = la;
                        else if (IsOctal(addrTok))
                            a = ParseOctalSafe(addrTok);
                    }

                    // Регистр
                    int pIdx = rest.IndexOf('(');
                    if (pIdx >= 0)
                    {
                        int cIdx = rest.IndexOf(')', pIdx);
                        if (cIdx > pIdx)
                        {
                            string rStr = rest.Substring(pIdx + 1, cIdx - pIdx - 1).Trim();
                            if (IsOctal(rStr)) reg = (int)ParseOctalSafe(rStr);
                        }
                    }

                    return ((long)reg << 20) | ((long)opcode << 12) | (a & 0xFFFFF);
                }
            }

            // BEMSH директивы
            string lower = s.ToLower();
            if (lower == "конк")
            {
                string arg = ExtractArg(s, "конк");
                arg = StripQuoted(arg);
                return ParseOctalSafe(arg) & 0xFFFFF;
            }
            if (lower == "конд")
            {
                string arg = ExtractArg(s, "конд");
                return ParseMaskedOctal(arg) & 0xFFFFF;
            }
            if (lower == "мода" || lower == "текст" || lower == "text" ||
                lower == "gost" || lower == "hex")
            {
                // Data mode markers → encode as 0 (the actual data follows in next words)
                return 0;
            }

            // Fallback: treat as text → encode first bytes
            long result = 0;
            for (int i = 0; i < s.Length && i < 3; i++)
                result |= (long)s[i] << (i * 8);
            return result & 0xFFFFF;
        }

        // ─── Вспомогательные ─────────────────────────────────────────────────

        private static string ExtractLabel(string line, out string body)
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
            {
                string label = line.Substring(0, colonIdx).Trim();
                body = line.Substring(colonIdx + 1).Trim();
                return label;
            }

            // BEMSH: label = first token (if not a known keyword)
            string first = FirstToken(line);
            if (first.Length > 0 && !IsMnemonicStart(first) && !IsOctal(first) &&
                !IsBemshDirective(first) && !first.EndsWith("$$$") &&
                first != "0-0" && first != "блмак" && first != "бтмалф")
            {
                body = line.Substring(first.Length).Trim();
                return first;
            }

            body = line;
            return "";
        }

        private static bool IsBemshDirective(string s)
        {
            string l = s.ToLower();
            return l == "старт" || l == "финиш" || l == "конк" || l == "конд" ||
                   l == "текст" || l == "мода" || l == "end";
        }

        private static string FirstToken(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != ',') i++;
            return s.Substring(0, i);
        }

        private static bool IsOctal(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
                if (c < '0' || c > '7') return false;
            return true;
        }

        private static bool IsMnemonicStart(string s)
        {
            if (s.Length == 0) return false;
            char c = s[0];
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   c == '*' || (c >= 0x400 && c <= 0x4FF) || c >= 0x80;
        }

        private static long ParseOctalSafe(string s)
        {
            s = s.Trim();
            long r = 0;
            foreach (char c in s)
                if (c >= '0' && c <= '7') r = (r << 3) | (c - '0');
            return r;
        }

        private static string ExtractArg(string body, string directive)
        {
            int idx = body.IndexOf(directive, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            return body.Substring(idx + directive.Length).Trim();
        }

        private static string StripQuoted(string s)
        {
            s = s.Trim();
            // k'NNN' or п'NNN'
            if (s.Length > 2 && (s[0] == 'к' || s[0] == 'п' || s[0] == 'К' || s[0] == 'П') && s[1] == '\'')
            {
                int end = s.IndexOf('\'', 2);
                if (end > 1) return s.Substring(2, end - 2);
            }
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static long ParseMaskedOctal(string s)
        {
            // мNNb'OOO' or мNNb OOO
            s = s.Trim();
            int bIdx = s.IndexOf('b');
            string valPart = bIdx >= 0 ? s.Substring(bIdx + 1) : s;
            valPart = StripQuoted(valPart);
            return ParseOctalSafe(valPart);
        }
    }
}