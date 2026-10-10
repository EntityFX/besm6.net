namespace Besm6.Assembler
{
    /// <summary>
    /// Разбор токенов полуслова в <see cref="DecodedInstruction"/> или данные.
    /// Использует <see cref="InstructionCodec.EncodeHalf"/> для кодирования.
    /// </summary>
    internal static class Parser
    {
        /// <summary>
        /// Ассемблирует одно 24-битное поле в uint.
        /// </summary>
        internal static uint AssembleField(
            string s,
            Dictionary<string, int> labels,
            AssemblyDialect dialect)
        {
            s = s.Trim();
            if (s.Length == 0) return 0;

            while (s.Length > 0 && s[0] == ',') s = s.Substring(1).Trim();
            while (s.Length > 0 && s[s.Length - 1] == ',') s = s.Substring(0, s.Length - 1).Trim();
            if (s.Length == 0) return 0;

            // Восьмеричное представление: reg [2|3]opcode addr.
            if (s[0] >= '0' && s[0] <= '7' && s.Contains(' '))
            {
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    int reg = (int)ParseOctalSafe(parts[0]);
                    int opcode = 0;
                    string opPart = parts[1];
                    if (opPart[0] == '2' || opPart[0] == '3')
                    {
                        opcode = (int)ParseOctalSafe(opPart) << 3;
                    }
                    else
                    {
                        opcode = (int)ParseOctalSafe(opPart);
                    }
                    int addr = (int)ParseOctalSafe(parts[2]);
                    uint encoded = ((uint)reg << 20) | ((uint)opcode << 12) | (uint)addr;
                    return encoded & 0xFFFFFF;
                }
                // Одиночное восьмеричное число.
                if (parts.Length == 1)
                    return (uint)(ParseOctalSafe(s) & 0xFFFFFF);
            }

            // Восьмеричное число → данные.
            if (Lexer.IsOctal(s))
                return (uint)(ParseOctalSafe(s) & 0xFFFFFF);

            // Ссылка на лейбл → адрес.
            if (labels.TryGetValue(s, out int labelAddr))
                return (uint)((long)labelAddr & 0xFFFFF);

            // Мнемоника.
            if (Lexer.IsMnemonicStart(s))
            {
                string mnemonic = Lexer.FirstToken(s);
                string rest = s.Length > mnemonic.Length ? s.Substring(mnemonic.Length).Trim() : "";

                if (OpcodeTable.TryGetOpcode(mnemonic, dialect, out int opcode))
                {
                    long a = 0;
                    int reg = 0;

                    if (rest.Length > 0)
                    {
                        // Адрес — часть перед '(' (если есть), иначе до пробела/запятой.
                        int parenIdx = rest.IndexOf('(');
                        string addrPart = parenIdx >= 0 ? rest.Substring(0, parenIdx) : rest;
                        string addrTok = addrPart.Trim();
                        if (addrTok.Length > 0)
                        {
                            if (labels.TryGetValue(addrTok, out int la))
                                a = la;
                            else if (Lexer.IsOctal(addrTok))
                                a = ParseOctalSafe(addrTok);
                            else if (IsNegativeAddress(addrTok))
                                a = -(long)ParseOctalSafe(addrTok.Substring(1)) & ArchitectureConstants.AddrMask;
                        }
                    }

                    int pIdx = rest.IndexOf('(');
                    if (pIdx >= 0)
                    {
                        int cIdx = rest.IndexOf(')', pIdx);
                        if (cIdx > pIdx)
                        {
                            string rStr = rest.Substring(pIdx + 1, cIdx - pIdx - 1).Trim();
                            if (Lexer.IsOctal(rStr)) reg = (int)ParseOctalSafe(rStr);
                        }
                    }

                    var instruction = new DecodedInstruction(
                        checked((byte)reg),
                        (Opcode)opcode,
                        checked((ushort)a),
                        (opcode & 0x80) != 0 ? InstructionFormat.Long : InstructionFormat.Short);
                    return InstructionCodec.EncodeHalf(instruction);
                }

                // Мнемоника не распознана в данном диалекте.
                if (dialect != AssemblyDialect.Auto)
                {
                    string lower = s.ToLower();
                    bool isDataDirective = IsDataDirective(lower) || IsDataModeMarker(lower);
                    if (!isDataDirective)
                        throw new AssemblerException($"Неизвестная мнемоника '{mnemonic}' в диалекте {dialect}");
                }
            }

            // БЭМШ директивы данных.
            string low = s.ToLower();
            if (low == "конк")
            {
                string arg = ExtractArg(s, "конк");
                arg = StripQuoted(arg);
                return (uint)(ParseOctalSafe(arg) & 0xFFFFF);
            }
            if (low == "конд")
            {
                string arg = ExtractArg(s, "конд");
                return (uint)(ParseMaskedOctal(arg) & 0xFFFFF);
            }
            if (IsDataModeMarker(low))
                return 0;

            // Fallback: текст → первые байты.
            long result = 0;
            for (int i = 0; i < s.Length && i < 3; i++)
                result |= (long)s[i] << (i * 8);
            return (uint)(result & 0xFFFFF);
        }

        private static bool IsNegativeAddress(string s)
            => s.Length > 1 && s[0] == '-' && Lexer.IsOctal(s.Substring(1));

        private static bool IsDataDirective(string lower)
            => lower.StartsWith("конк", StringComparison.Ordinal) ||
               lower.StartsWith("конд", StringComparison.Ordinal);

        private static bool IsDataModeMarker(string lower)
            => lower == "мода" || lower == "текст" || lower == "text" ||
               lower == "gost" || lower == "hex";

        internal static long ParseOctalSafe(string s)
        {
            s = s.Trim();
            long r = 0;
            foreach (char c in s)
                if (c >= '0' && c <= '7') r = (r << 3) | (uint)(c - '0');
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
            s = s.Trim();
            int bIdx = s.IndexOf('b');
            string valPart = bIdx >= 0 ? s.Substring(bIdx + 1) : s;
            valPart = StripQuoted(valPart);
            return ParseOctalSafe(valPart);
        }
    }
}