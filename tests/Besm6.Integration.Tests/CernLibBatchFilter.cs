using System;
using System.Collections.Generic;
using System.Linq;

namespace Besm6.Tests
{
    /// <summary>
    /// Детерминированное batch-разбиение CERNlib-матрицы (SuperPlan Task A2).
    /// Переменная окружения BESM6_CERN_BATCH, токены через запятую:
    ///   пусто / all          — вся матрица (по умолчанию)
    ///   lib1 / lib2          — целые библиотеки
    ///   lib1:0-99            — диапазон [0..99] по 0-based индексу внутри библиотеки
    ///   names:d302,j531a     — по именам случаев (ищутся в обеих библиотеках)
    /// Фильтр НЕ изменяет manifest — только выбирает подмножество исполняемых случаев.
    /// </summary>
    public static class CernLibBatchFilter
    {
        public const string EnvVarName = "BESM6_CERN_BATCH";

        /// <summary>Чистая функция от (cases, spec): детерминированна и тестируема без env.</summary>
        public static IEnumerable<CernLibCase> Filter(IReadOnlyList<CernLibCase> cases, string? spec)
        {
            string s = (spec ?? string.Empty).Trim();
            if (s.Length == 0 || s.Equals("all", StringComparison.OrdinalIgnoreCase))
                return cases;

            // names: — режим «весь спецификатор есть список имён» (имена через запятую).
            if (s.StartsWith("names:", StringComparison.OrdinalIgnoreCase))
            {
                var byName = new List<CernLibCase>(cases.Count);
                AddNames(cases, s.Substring("names:".Length), byName);
                return cases.Where(c => byName.Contains(c));
            }

            var wanted = new List<CernLibCase>(cases.Count);
            foreach (string tokenRaw in s.Split(','))
            {
                string t = tokenRaw.Trim();
                if (t.Length == 0)
                    continue;
                if (t.Equals("all", StringComparison.OrdinalIgnoreCase))
                    return cases;
                if (t.Equals("lib1", StringComparison.OrdinalIgnoreCase) || t.Equals("lib2", StringComparison.OrdinalIgnoreCase))
                {
                    int lib = t.EndsWith("1") ? 1 : 2;
                    AddLibrary(cases, lib, wanted);
                }
                else if (t.IndexOf(':') > 0)
                    AddRange(cases, t, wanted);
                else
                    throw new InvalidDataException(
                        "Неизвестный batch-токен '" + t + "' (env " + EnvVarName + "). " +
                        "Ожидается: all | lib1 | lib2 | libN:a-b | names:a,b");
            }

            // Порядок manifest'а сохраняется, дубликаты исключены.
            return cases.Where(c => wanted.Contains(c));
        }

        private static void AddLibrary(IReadOnlyList<CernLibCase> cases, int lib, List<CernLibCase> sink)
        {
            foreach (var c in cases)
                if (c.Library == lib)
                    sink.Add(c);
        }

        private static void AddNames(IReadOnlyList<CernLibCase> cases, string names, List<CernLibCase> sink)
        {
            foreach (string raw in names.Split(','))
            {
                string n = raw.Trim();
                if (n.Length == 0)
                    continue;
                foreach (var c in cases)
                    if (string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))
                        sink.Add(c);
            }
        }

        private static void AddRange(IReadOnlyList<CernLibCase> cases, string token, List<CernLibCase> sink)
        {
            int colon = token.IndexOf(':');
            string libPart = token.Substring(0, colon);
            if (!libPart.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Диапазон имеет вид libN:a-b: '" + token + "'");
            if (!int.TryParse(libPart.Substring("lib".Length), out int lib) || (lib != 1 && lib != 2))
                throw new InvalidDataException("Неизвестная библиотека в диапазоне: '" + token + "'");

            string[] parts = token.Substring(colon + 1).Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int from) || !int.TryParse(parts[1], out int to))
                throw new InvalidDataException("Диапазон имеет вид a-b: '" + token + "'");
            if (from < 0 || to < from)
                throw new InvalidDataException("Некорректный диапазон a-b (a>=0, b>=a): '" + token + "'");

            var libCases = cases.Where(c => c.Library == lib).ToList();
            int count = Math.Min(to, libCases.Count - 1) - from + 1;
            if (count <= 0)
                return;
            sink.AddRange(libCases.Skip(from).Take(count));
        }
    }
}