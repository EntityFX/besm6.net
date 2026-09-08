namespace Besm6.Assembler
{
    /// <summary>
    /// Токенизация строки исходного кода без знания диалекта.
    /// Разделяет строку на левое/правое полуслово и извлекает токены.
    /// </summary>
    internal sealed class LexedLine
    {
        internal string? Label { get; init; }
        internal string? Left { get; init; }
        internal string? Right { get; init; }
        internal bool IsEmpty { get; init; }
        internal bool IsEndDirective { get; init; }
    }

    internal static class Lexer
    {
        /// <summary>
        /// Разбирает строку на лейбл, левое и правое полуслово.
        /// </summary>
        internal static LexedLine Tokenize(string line)
        {
            line = line.Trim();
            if (line.Length == 0)
                return new LexedLine { IsEmpty = true };

            string? label = null;
            string body = line;

            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
            {
                label = line.Substring(0, colonIdx).Trim();
                body = line.Substring(colonIdx + 1).Trim();
            }
            else
            {
                string first = FirstToken(line);
                if (first.Length > 0 && !IsMnemonicStart(first) && !IsOctal(first) &&
                    !IsBemshDirective(first) && !first.EndsWith("$$$") &&
                    first != "0-0" && first != "блмак" && first != "бтмалф")
                {
                    label = first;
                    body = line.Substring(first.Length).Trim();
                }
            }

            if (body.Length == 0)
                return new LexedLine { Label = label, IsEmpty = true };

            string firstToken = FirstToken(body);
            if (IsEndDirective(firstToken))
                return new LexedLine { Label = label, IsEndDirective = true };

            // Разделение на левое/правое полуслово.
            int commaIdx = FindSeparatorComma(body);
            if (commaIdx > 0)
            {
                string left = body.Substring(0, commaIdx).Trim();
                string right = commaIdx + 1 < body.Length ? body.Substring(commaIdx + 1).Trim() : "";
                return new LexedLine { Label = label, Left = left, Right = right };
            }

            return new LexedLine { Label = label, Left = body };
        }

        internal static string FirstToken(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != ',' && s[i] != '(') i++;
            return s.Substring(0, i);
        }

        internal static int FindSeparatorComma(string body)
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

        internal static bool IsOctal(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
                if (c < '0' || c > '7') return false;
            return true;
        }

        internal static bool IsMnemonicStart(string s)
        {
            if (s.Length == 0) return false;
            char c = s[0];
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   c == '*' || (c >= 0x400 && c <= 0x4FF) || c >= 0x80;
        }

        internal static bool IsBemshDirective(string s)
        {
            string l = s.ToLower();
            return l == "старт" || l == "финиш" || l == "конк" || l == "конд" ||
                   l == "текст" || l == "мода" || l == "end";
        }

        internal static bool IsEndDirective(string first)
        {
            string l = first.ToLower();
            return l == "end" || l == "финиш" || l == "старт";
        }
    }
}