using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Besm6.Loader
{
    /// <summary>
    /// Карта управления .dub.
    /// </summary>
    public sealed class DubControlCard
    {
        /// <summary>Директива без '*', в нижнем регистре (например "tape", "execute").</summary>
        public string Directive { get; set; } = "";

        /// <summary>Остаток строки после директивы (аргумент).</summary>
        public string? Argument { get; set; }

        public override string ToString() => $"*{Directive} {Argument}".TrimEnd();
    }

    /// <summary>
    /// Слово программы в секции *assem: либо мнемоническая инструкция (Text),
    /// либо сырое восьмеричное слово (Value, IsRaw=true).
    /// </summary>
    public sealed class ProgramWord
    {
        /// <summary>true — сырое слово (Value); false — мнемоника (Text).</summary>
        public bool IsRaw { get; set; }

        /// <summary>Текст мнемонической инструкции.</summary>
        public string? Text { get; set; }

        /// <summary>Значение сырого слова.</summary>
        public long Value { get; set; }
    }

    /// <summary>
    /// Результат разбора .dub job-скрипта.
    /// </summary>
    public sealed class DubJob
    {
        /// <summary>Все карты управления в порядке появления.</summary>
        public List<DubControlCard> ControlCards { get; } = new();

        /// <summary>Исходный текст (строки, не являющиеся картами управления, raw-словами и не секцией *assem).</summary>
        public List<string> SourceLines { get; } = new();

        /// <summary>Сырые восьмеричные слова вне секции *assem (строки, начинающиеся с '`').</summary>
        public List<long> RawWords { get; } = new();

        /// <summary>
        /// Секция *assem в порядке появления: мнемонические инструкции и/или
        /// сырые слова (восьмеричные), ассемблируемые загрузчиком в память.
        /// </summary>
        public List<ProgramWord> AssemProgram { get; } = new();

        /// <summary>Есть ли образ программы (raw-слова и/или секция *assem).</summary>
        public bool HasProgramImage => RawWords.Count > 0 || AssemProgram.Count > 0;

        /// <summary>Список карт '*tape:N/имя,Z'.</summary>
        public List<TapeMount> TapeMounts { get; } = new();

        /// <summary>Список карт '*library:N'.</summary>
        public List<int> Libraries { get; } = new();

        /// <summary>Адрес *trans-main (восьмеричный), если задан.</summary>
        public int? TransMain { get; set; }

        /// <summary>Имя для *execute (могут быть параметры).</summary>
        public string? Execute { get; set; }

        /// <summary>Директива *name.</summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Карта '*tape:N/имя,Z' (монтаж ленты на канал N).
    /// </summary>
    public sealed class TapeMount
    {
        /// <summary>Номер канала (восьмеричный).</summary>
        public int Channel { get; set; }

        /// <summary>Имя ленты.</summary>
        public string Name { get; set; } = "";

        /// <summary>Необязательный числовой суффикс Z.</summary>
        public int? Zone { get; set; }

        public override string ToString() => $"*tape:{Convert.ToString(Channel, 8)}/{Name},{Zone}";
    }

    /// <summary>
    /// Парсер .dub job-скриптов.
    /// Формат: карты управления (строки '*...'), сырые слова (строки '`...'),
    /// секция *assem (мнемоника + сырые слова) и исходный текст транслятора.
    /// </summary>
    public static class JobParser
    {
        /// <summary>
        /// Директивы управления, которые закрывают секцию *assem.
        /// Внутрисекционные строки вида '*NN' (экстракоды) и мнемоники не входят в этот набор
        /// и трактуются как инструкции.
        /// </summary>
        private static readonly HashSet<string> ControlDirectives = new(StringComparer.OrdinalIgnoreCase)
        {
            "name", "assem", "execute", "call", "end", "tape", "library",
            "trans-main", "disc", "file", "overlay", "record", "read", "write",
            "no load list", "start", "stop", "release", "reset", "load", "move",
            "directory", "delete", "catalog", "list", "print",
        };

        /// <summary>
        /// Разобрать .dub скрипт из строк.
        /// </summary>
        public static DubJob Parse(IEnumerable<string> lines)
        {
            var job = new DubJob();
            bool inAssem = false;
            foreach (var rawLine in lines)
            {
                // Убираем перевод строки.
                string line = rawLine.TrimEnd('\r', '\n');
                if (line.Length == 0)
                {
                    if (inAssem)
                        job.AssemProgram.Add(new ProgramWord { IsRaw = true, Value = 0 });
                    else
                        job.SourceLines.Add("");
                    continue;
                }

                char first = line[0];
                if (first == '`')
                {
                    // Сырое восьмеричное слово.
                    string oct = line.Substring(1).Trim();
                    long word = ParseOctalWord(oct, rawLine);
                    if (inAssem)
                        job.AssemProgram.Add(new ProgramWord { IsRaw = true, Value = word });
                    else
                        job.RawWords.Add(word);
                    continue;
                }

                if (first == '*')
                {
                    // Внутри *assem строка вида '*NN' (экстракод) — это инструкция,
                    // а не карта управления. Закрываем секцию только по известным директивам.
                    if (inAssem && !IsControlDirective(line))
                    {
                        job.AssemProgram.Add(new ProgramWord { IsRaw = false, Text = line });
                        continue;
                    }

                    // Карта управления.
                    var card = ParseControlCard(line);
                    job.ControlCards.Add(card);
                    ApplyCard(job, card);
                    // 'assem' включает режим секции ассемблера; другие карты её закрывают.
                    inAssem = card.Directive == "assem";
                    continue;
                }

                // Внутри *assem — мнемонические инструкции; иначе — исходный текст.
                if (inAssem)
                    job.AssemProgram.Add(new ProgramWord { IsRaw = false, Text = line });
                else
                    job.SourceLines.Add(line);
            }
            return job;
        }

        /// <summary>
        /// Является ли строка с '*' известной картой управления (а не инструкцией-экстракодом).
        /// </summary>
        private static bool IsControlDirective(string line)
        {
            // line[0] == '*'
            int pos = 1;
            int end = line.Length;
            while (pos < end && (char.IsWhiteSpace(line[pos]) || line[pos] == '*')) pos++;
            int start = pos;
            while (pos < end && !char.IsWhiteSpace(line[pos]) && line[pos] != ':') pos++;
            string directive = line.Substring(start, pos - start).ToLowerInvariant();
            return ControlDirectives.Contains(directive);
        }

        /// <summary>
        /// Разобрать .dub скрипт из файла.
        /// </summary>
        public static DubJob ParseFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Job script not found: {path}");
            return Parse(File.ReadAllLines(path));
        }

        /// <summary>
        /// Разобрать карту управления '*name ...'. Возвращает директиву без '*'.
        /// </summary>
        public static DubControlCard ParseControlCard(string line)
        {
            // line[0] == '*'
            int pos = 1;
            int end = line.Length;
            // Пропускаем пробелы и дополнительные '*'.
            while (pos < end && (char.IsWhiteSpace(line[pos]) || line[pos] == '*')) pos++;

            // Ищем конец директивы: пробел или ':'.
            int start = pos;
            while (pos < end && !char.IsWhiteSpace(line[pos]) && line[pos] != ':') pos++;
            string directive = line.Substring(start, pos - start).ToLowerInvariant();

            // Пропускаем ':' и/или пробелы.
            while (pos < end && (char.IsWhiteSpace(line[pos]) || line[pos] == ':')) pos++;
            string argument = pos < end ? line.Substring(pos).TrimEnd('\r', '\n') : "";

            return new DubControlCard { Directive = directive, Argument = argument };
        }

        private static void ApplyCard(DubJob job, DubControlCard card)
        {
            switch (card.Directive)
            {
                case "name":
                    job.Name = card.Argument?.Trim();
                    break;
                case "tape":
                    if (card.Argument != null)
                        job.TapeMounts.Add(ParseTapeMount(card.Argument));
                    break;
                case "library":
                    if (card.Argument != null && int.TryParse(card.Argument.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lib))
                        job.Libraries.Add(lib);
                    break;
                case "trans-main":
                    if (card.Argument != null)
                    {
                        string a = card.Argument.Trim();
                        // Формат может содержать суффикс/параметры.
                        int sep = a.IndexOfAny(new[] { ' ', '\t', ',' });
                        string addr = sep >= 0 ? a.Substring(0, sep) : a;
                        if (TryParseOctal(addr, out int octAddr))
                            job.TransMain = octAddr;
                    }
                    break;
                case "execute":
                    job.Execute = card.Argument?.Trim();
                    break;
                // Прочие директивы (read, call, no load list, end file и т.д.)
                // сохраняются в ControlCards, но не влияют на структуру DubJob.
            }
        }

        private static TapeMount ParseTapeMount(string arg)
        {
            // Формат: 'N/имя' или 'N/имя,Z'
            var mount = new TapeMount();
            int slash = arg.IndexOf('/');
            if (slash >= 0)
            {
                string channel = arg.Substring(0, slash).Trim();
                if (TryParseOctal(channel, out int ch))
                    mount.Channel = ch;
                string rest = arg.Substring(slash + 1).Trim();
                int comma = rest.IndexOf(',');
                if (comma >= 0)
                {
                    mount.Name = rest.Substring(0, comma).Trim();
                    if (int.TryParse(rest.Substring(comma + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone))
                        mount.Zone = zone;
                }
                else
                {
                    mount.Name = rest;
                }
            }
            else
            {
                // Без '/': только имя.
                mount.Name = arg.Trim();
            }
            return mount;
        }

        /// <summary>
        /// Разобрать строку восьмеричного слова (без ведущего '`').
        /// </summary>
        public static long ParseOctalWord(string oct, string originalLine)
        {
            try
            {
                // Восьмеричное слово — до 16 цифр. Сбрасываем ведущие нули.
                string trimmed = oct.TrimStart('0');
                if (trimmed.Length == 0) return 0;
                if (trimmed.Length > 16) trimmed = trimmed.Substring(trimmed.Length - 16);
                long val = 0;
                foreach (char c in trimmed)
                {
                    if (c < '0' || c > '7')
                        throw new FormatException($"Invalid octal digit '{c}' in '{originalLine}'");
                    val = (val << 3) | (long)(c - '0');
                }
                return val & 0xFFFFFFFFFFFFL;
            }
            catch (Exception ex)
            {
                throw new FormatException($"Invalid raw word: '{originalLine}' ({ex.Message})");
            }
        }

        private static bool TryParseOctal(string s, out int value)
        {
            value = 0;
            s = s.Trim();
            if (s.Length == 0) return false;
            foreach (char c in s)
            {
                if (c < '0' || c > '7') return false;
                value = (value << 3) | (c - '0');
            }
            return true;
        }
    }
}