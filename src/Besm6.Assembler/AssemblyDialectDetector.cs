namespace Besm6.Assembler
{
    /// <summary>
    /// Определяет диалект ассемблирования по первой директиве программы.
    /// <c>*madlen</c> → <see cref="AssemblyDialect.Madlen"/>,
    /// <c>*bemsh</c> → <see cref="AssemblyDialect.Bemsh"/>,
    /// <c>*assem</c> или отсутствие → <see cref="AssemblyDialect.Auto"/>.
    /// Директивы выбора не создают машинного слова.
    /// </summary>
    public static class AssemblyDialectDetector
    {
        /// <summary>
        /// Определяет диалект по первой непустой строке программы.
        /// Строка-директива не включается в ассемблирование.
        /// </summary>
        public static AssemblyDialect Detect(IEnumerable<string> lines)
        {
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                string first = FirstToken(line);
                string lower = first.ToLowerInvariant();

                return lower switch
                {
                    "*madlen" => AssemblyDialect.Madlen,
                    "*bemsh" => AssemblyDialect.Bemsh,
                    "*assem" => AssemblyDialect.Auto,
                    _ => AssemblyDialect.Auto,
                };
            }

            return AssemblyDialect.Auto;
        }

        /// <summary>
        /// Возвращает true, если строка является директивой выбора диалекта.
        /// </summary>
        public static bool IsDialectDirective(string line)
        {
            string first = FirstToken(line.Trim()).ToLowerInvariant();
            return first is "*madlen" or "*bemsh" or "*assem";
        }

        private static string FirstToken(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != ',') i++;
            return s.Substring(0, i);
        }
    }
}