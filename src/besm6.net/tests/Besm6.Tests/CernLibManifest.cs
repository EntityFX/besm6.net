using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Besm6.Tests
{
    /// <summary>
    /// Один активный CERNlib-случай: номер библиотеки (1 или 2) и имя теста
    /// (соответствует ref/tests/lib{Library}/{Name}.f и expect_{Name}.txt).
    /// </summary>
    public sealed record CernLibCase(int Library, string Name)
    {
        public override string ToString() => "lib" + Library + "/" + Name;
    }

    /// <summary>
    /// Активная CERNlib-матрица (183 + 214 = 397 случаев), зафиксированная
    /// коммиченным файлом cernlib_manifest.json.
    ///
    /// Manifest генерируется из эталона ref/tests/cernlib_test.cpp скриптом
    /// plans/_count_cernlib.ps1 и обновляется только при изменении эталона.
    /// Тесты НЕ зависят от каталога ref/ (он git-ignored и на чистом checkout
    /// отсутствует) — источник данных для тестов всегда доступен.
    /// </summary>
    public static class CernLibManifest
    {
        private static IReadOnlyList<CernLibCase>? _activeCases;

        /// <summary>
        /// Непустая последовательность из 397 активных случаев
        /// (183 в lib1, 214 в lib2), в порядке эталонного cernlib_test.cpp.
        /// </summary>
        public static IReadOnlyList<CernLibCase> ActiveCases
        {
            get
            {
                if (_activeCases != null)
                    return _activeCases;

                string path = Path.Combine(AppContext.BaseDirectory, "cernlib_manifest.json");
                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        "CERNlib manifest не найден: " + path +
                        ". Ожидается рядом с тестовым assembly (см. csproj CopyToOutputDirectory).", path);

                ManifestDoc doc;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                };
                using (var fs = File.OpenRead(path))
                {
                    try
                    {
                        doc = JsonSerializer.Deserialize<ManifestDoc>(fs, options)
                              ?? throw new InvalidDataException("Пустой JSON в " + path);
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidDataException("Невалидный manifest: " + path, ex);
                    }
                }

                if (doc.Cases == null || doc.Cases.Count == 0)
                    throw new InvalidDataException("Manifest без случаев: " + path);

                var list = new List<CernLibCase>(doc.Cases.Count);
                foreach (var c in doc.Cases)
                {
                    if (c == null)
                        continue;
                    var name = (c.Name ?? string.Empty).Trim();
                    if (c.Library != 1 && c.Library != 2)
                        throw new InvalidDataException("Недопустимая библиотека " + c.Library + " в " + path);
                    if (name.Length == 0)
                        throw new InvalidDataException("Пустое имя теста в " + path);
                    list.Add(new CernLibCase(c.Library, name));
                }

                _activeCases = list;
                return list;
            }
        }

        private sealed class ManifestDoc
        {
            public List<CaseDto>? Cases { get; set; }
        }

        private sealed class CaseDto
        {
            public int Library { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}