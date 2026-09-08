namespace Besm6.Assembler;

/// <summary>
/// Общий движок ассемблирования БЭСМ-6. Портирован из монолитного
/// ProgramAssembler и параметризован диалектом: в строгих диалектах
/// (MADLEN/БЭМШ) посторонние мнемоники отклоняются, в Auto сохраняется
/// совместимое поведение.
/// </summary>
internal static class AssemblyEngine
{
    private const ulong WordMask = 0xFFFFFFFFFFFFUL;
    private const ulong FieldMask = 0xFFFFFUL;

    /// <summary>Ассемблирует одно 48-битное слово из строки исходного кода.</summary>
    internal static ulong AssembleWord(string source, AssemblyDialect dialect)
    {
        string line = source.Trim();
        if (line.Length == 0) return 0;

        ExtractLabel(line, out string body);
        if (body.Length == 0) return 0;

        return AssembleBody(body, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), dialect);
    }

    /// <summary>Ассемблирует программу: каждый рядок → одно слово.</summary>
    internal static AssemblyResult AssembleProgram(
        IEnumerable<string> lines,
        int baseAddr,
        AssemblyDialect dialect)
    {
        var input = lines.ToList();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var words = new List<ulong>();

        // Pass 1: адреса лейблов.
        int addr = baseAddr;
        foreach (var raw in input)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            string label = ExtractLabel(line, out string body);
            if (label.Length > 0)
                labels[label] = addr;

            string first = FirstToken(body);
            if (!IsEndDirective(first))
                addr++;
        }

        // Pass 2: ассемблирование.
        addr = baseAddr;
        foreach (var raw in input)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            ExtractLabel(line, out string body);
            if (body.Length == 0) continue;

            string first = FirstToken(body);
            if (IsEndDirective(first))
                continue;

            ulong word = AssembleBody(body, labels, dialect) & WordMask;
            words.Add(word);
            addr++;
        }

        return new AssemblyResult { BaseAddr = baseAddr, Words = words, Labels = labels };
    }

    /// <summary>Собирает 48-битное слово из тела строки (left,right либо одно поле).</summary>
    private static ulong AssembleBody(string body, Dictionary<string, int> labels, AssemblyDialect dialect)
    {
        // MADLEN: есть запятая-разделитель → два поля.
        if (body.Contains(','))
        {
            int commaIdx = FindSeparatorComma(body);
            if (commaIdx > 0)
            {
                string leftStr = body.Substring(0, commaIdx).Trim();
                string rightStr = commaIdx + 1 < body.Length ? body.Substring(commaIdx + 1).Trim() : "";

                long left = AssembleField(leftStr, labels, dialect);
                long right = AssembleField(rightStr, labels, dialect);

                return (((ulong)left << 24) | (ulong)right) & WordMask;
            }
        }

        // БЭМШ: одиночное поле (mnemonic addr (reg)).
        return (ulong)AssembleField(body, labels, dialect) & WordMask;
    }

    /// <summary>Ассемблирует одно 24-битное поле.</summary>
    private static long AssembleField(string s, Dictionary<string, int> labels, AssemblyDialect dialect)
    {
        s = s.Trim();
        if (s.Length == 0) return 0;

        while (s.Length > 0 && s[0] == ',') s = s.Substring(1).Trim();
        while (s.Length > 0 && s[s.Length - 1] == ',') s = s.Substring(0, s.Length - 1).Trim();
        if (s.Length == 0) return 0;

        // Восьмеричное число → данные.
        if (IsOctal(s))
            return ParseOctalSafe(s) & 0xFFFFF;

        // Ссылка на лейбл → адрес.
        if (labels.TryGetValue(s, out int labelAddr))
            return (long)labelAddr & 0xFFFFF;

        // Мнемоника.
        if (IsMnemonicStart(s))
        {
            string mnemonic = FirstToken(s);
            string rest = s.Length > mnemonic.Length ? s.Substring(mnemonic.Length).Trim() : "";

            if (OpcodeTable.TryGetOpcode(mnemonic, dialect, out int opcode))
            {
                long a = 0;
                int reg = 0;

                if (rest.Length > 0)
                {
                    string addrTok = FirstToken(rest);
                    if (labels.TryGetValue(addrTok, out int la))
                        a = la;
                    else if (IsOctal(addrTok))
                        a = ParseOctalSafe(addrTok);
                    else if (IsNegativeAddress(addrTok))
                        a = (long)ParseOctalSafe(addrTok.Substring(1)) & 0xFFFFF;
                }

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

            // Мнемоника не распознана в данном диалекте.
            if (dialect != AssemblyDialect.Auto)
            {
                // Допускаем data-директивы независимо от диалекта, иначе отклоняем.
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
            return ParseOctalSafe(arg) & 0xFFFFF;
        }
        if (low == "конд")
        {
            string arg = ExtractArg(s, "конд");
            return ParseMaskedOctal(arg) & 0xFFFFF;
        }
        if (IsDataModeMarker(low))
        {
            // Маркеры режима данных → 0 (данные идут следующими словами).
            return 0;
        }

        // Fallback: текст → первые байты.
        long result = 0;
        for (int i = 0; i < s.Length && i < 3; i++)
            result |= (long)s[i] << (i * 8);
        return result & 0xFFFFF;
    }

    private static bool IsDataDirective(string lower)
        => lower.StartsWith("конк", StringComparison.Ordinal) ||
           lower.StartsWith("конд", StringComparison.Ordinal);

    private static bool IsDataModeMarker(string lower)
        => lower == "мода" || lower == "текст" || lower == "text" ||
           lower == "gost" || lower == "hex";

    private static bool IsNegativeAddress(string s)
        => s.Length > 1 && s[0] == '-' && IsOctal(s.Substring(1));

    private static string ExtractLabel(string line, out string body)
    {
        int colonIdx = line.IndexOf(':');
        if (colonIdx > 0)
        {
            string label = line.Substring(0, colonIdx).Trim();
            body = line.Substring(colonIdx + 1).Trim();
            return label;
        }

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

    private static bool IsEndDirective(string first)
    {
        string l = first.ToLower();
        return l == "end" || l == "финиш" || l == "старт";
    }

    private static string FirstToken(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != ',') i++;
        return s.Substring(0, i);
    }

    private static int FindSeparatorComma(string body)
    {
        int depth = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\'') depth ^= 1;
            else if (c == ',' && depth == 0)
                return i;
        }
        return body.IndexOf(',');
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