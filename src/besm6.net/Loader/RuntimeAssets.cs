using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Besm6.Loader
{
    /// <summary>
    /// Класс лицензии runtime-ресурса (SuperPlan Task A4):
    /// <see cref="Bundled"/> — поставляется вместе с пакетом;
    /// <see cref="UserProvided"/> — пользователь предоставляет сам (лицензия не позволяет встраивать).
    /// </summary>
    public enum RuntimeAssetLicense
    {
        Bundled,
        UserProvided,
    }
}

namespace Besm6.Loader
{
    /// <summary>
    /// Описание одного обязательного runtime-образа (лента/диск) с контрольной суммой,
    /// происхождением и классом лицензии — единица checksum-manifest (SuperPlan Task A4).
    /// </summary>
    public sealed class RuntimeAsset
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("tapeId")]
        public long TapeId { get; init; }

        /// <summary>Ожидаемый SHA256 (hex, нижний регистр). null — не проверяется.</summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("provenance")]
        public string Provenance { get; init; } = string.Empty;

        [JsonPropertyName("license")]
        public RuntimeAssetLicense License { get; init; } = RuntimeAssetLicense.Bundled;

        [JsonPropertyName("required")]
        public bool Required { get; init; } = true;

        [JsonPropertyName("obtainHint")]
        public string ObtainHint { get; init; } = string.Empty;
    }
}

namespace Besm6.Loader
{
    /// <summary>Результат успешного разрешения: каталог + полный путь и фактический checksum каждого ресурса.</summary>
    public sealed class ResolvedRuntimeAssets
    {
        [JsonPropertyName("tapesDir")]
        public required string TapesDir { get; init; }

        [JsonPropertyName("paths")]
        public required IReadOnlyDictionary<string, string> PathsByAsset { get; init; }

        [JsonPropertyName("sha256")]
        public IReadOnlyDictionary<string, string> Sha256ByAsset { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

namespace Besm6.Loader
{
    /// <summary>
    /// Ошибка проверки runtime-ресурсов (SuperPlan Task A4): перечисляет каждый отсутствующий
    /// или несовпадающий по checksum ресурс, способ его получения и все проверенные каталоги.
    /// </summary>
    public sealed class RuntimeAssetsException : Exception
    {
        public IReadOnlyList<RuntimeAsset> ProblemAssets { get; }
        public IReadOnlyList<string> SearchDirectories { get; }

        public RuntimeAssetsException(
            IReadOnlyList<RuntimeAsset> problemAssets,
            IReadOnlyList<string> searchDirectories,
            string message)
            : base(message)
        {
            ProblemAssets = problemAssets;
            SearchDirectories = searchDirectories;
        }
    }
}

namespace Besm6.Loader
{
    /// <summary>
    /// Каталог обязательных runtime-образов и их fail-fast разрешение (SuperPlan Task A4).
    /// Наличие ресурса проверяется ДО запуска процессора; при любом отсутствии/несоответствии
    /// checksum бросается <see cref="RuntimeAssetsException"/>, перечисляющий все проблемные
    /// ресурсы и все проверенные каталоги. Поиск идёт в порядке приоритета:
    /// явный абсолютный путь → каталог конфигурации → каталог приложения (publish) → dev-поиск вверх.
    /// </summary>
    public static class RuntimeAssets
    {
        /// <summary>
        /// Полная checksum-manifest всех обязательных MONSYS/CERN tape images с происхождением.
        /// </summary>
        public static readonly IReadOnlyList<RuntimeAsset> Catalog = new List<RuntimeAsset>
        {
            new()
            {
                Name = "monsys.9",
                TapeId = TapeImage.TapeMonsys,
                Sha256 = "cc27c8d982231442e4d5b2bb6672945cbcd8caaf47ff3be1e578c5de621908ec",
                Provenance = "MONSYS «Дубна» — операционная система БЭСМ-6, лента 011 oct",
                License = RuntimeAssetLicense.UserProvided,
                Required = true,
                ObtainHint = "Положите monsys.9 в каталог tapes/ (или задайте ключ 'tapes' в конфиге). Исторический образ Дубны — получите по лицензии вашего учреждения. См. docs/runtime-assets.md.",
            },
            new()
            {
                Name = "librar.12",
                TapeId = TapeImage.TapeLibrar12,
                Sha256 = "4fbfb41bfac01949eafa084fb35fd915c211ed16f9417694b107bc0f23f0bb14",
                Provenance = "CERN Common Software Library #1 (CERNlib lib1), лента 012 oct",
                License = RuntimeAssetLicense.UserProvided,
                Required = true,
                ObtainHint = "Положите librar.12 в каталог tapes/ (или задайте ключ 'tapes' в конфиге). CERNlib lib1 (лента 012 oct) — получите по лицензии вашего учреждения. См. docs/runtime-assets.md.",
            },
            new()
            {
                Name = "librar.37",
                TapeId = TapeImage.TapeLibrar37,
                Sha256 = "0575e9bba22a87a1d59de4a2586d698d6fbb0bc3cff0a6d1db7a63428c0f0bc7",
                Provenance = "CERN Common Software Library #2 (CERNlib lib2), лента 037 oct",
                License = RuntimeAssetLicense.UserProvided,
                Required = true,
                ObtainHint = "Положите librar.37 в каталог tapes/ (или задайте ключ 'tapes' в конфиге). CERNlib lib2 (лента 037 oct) — получите по лицензии вашего учреждения. См. docs/runtime-assets.md.",
            },
            new()
            {
                Name = "bemsh.739",
                TapeId = TapeImage.TapeBemsh,
                Sha256 = "69458c72286e9fe8ed3bc1d448ed20754d59259bfb5d7a7484850446481d0850",
                Provenance = "DISPAC/BEMSH — командный процессор БЭСМ-6, лента 0331 oct",
                License = RuntimeAssetLicense.UserProvided,
                Required = true,
                ObtainHint = "Положите bemsh.739 в каталог tapes/ (или задайте ключ 'tapes' в конфиге). DISPAC/BEMSH (лента 0331 oct) — получите по лицензии вашего учреждения. См. docs/runtime-assets.md.",
            },
            new()
            {
                Name = "b.7",
                TapeId = TapeImage.TapeB,
                Sha256 = "7d6d864a103f309b5adca2a46abacffbc3d226aadd7c4a372c4fcea912c33f80",
                Provenance = "Компилятор B (FORTRAN) для БЭСМ-6, лента 007 oct",
                License = RuntimeAssetLicense.UserProvided,
                Required = true,
                ObtainHint = "Положите b.7 в каталог tapes/ (или задайте ключ 'tapes' в конфиге). Компилятор B/FORTRAN (лента 007 oct) — получите по лицензии вашего учреждения. См. docs/runtime-assets.md.",
            },
        };

        /// <summary>Обязательный для OS/CERN-нагрузки подкаталог образов.</summary>
        public static IReadOnlyList<RuntimeAsset> RequiredSet =>
            Catalog.Where(a => a.Required).ToList();

        /// <summary>
        /// Каталог в порядке приоритета поиска (SuperPlan Task A4):
        /// 1) явно указанный абсолютный путь (config «tapes»);
        /// 2) каталог конфигурации (относительно besm6.json, через Config.ResolvePath);
        /// 3) каталог приложения (AppContext.BaseDirectory — куда кладёт dotnet publish);
        /// 4) dev-поиск вверх (tapes/, ref/tapes/, ref/dubna/tapes/) — только developer checkout.
        /// </summary>
        public static IReadOnlyList<string> SearchDirectories(Config cfg)
        {
            var dirs = new List<string>();
            void Add(string? d)
            {
                if (string.IsNullOrWhiteSpace(d)) return;
                try
                {
                    string f = Path.GetFullPath(d);
                    if (!dirs.Any(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase)))
                        dirs.Add(f);
                }
                catch (ArgumentException) { /* некорректный путь из config — пропускаем */ }
            }

            if (!string.IsNullOrWhiteSpace(cfg.Tapes) && Path.IsPathRooted(cfg.Tapes))
                Add(cfg.Tapes);
            Add(cfg.ResolvePath(string.IsNullOrWhiteSpace(cfg.Tapes) ? "tapes" : cfg.Tapes));
            Add(Path.Combine(AppContext.BaseDirectory, "tapes"));
            Add(AppContext.BaseDirectory);
            Add(TapeImage.DefaultTapesDir());
            return dirs;
        }

        /// <summary>
        /// Разрешить обязательные runtime-ресурсы (SuperPlan Task A4). Возвращает полный путь
        /// каждого ресурса; при любом отсутствии/несоответствии checksum бросает
        /// <see cref="RuntimeAssetsException"/>, перечисляющий все проблемные ресурсы и каталоги.
        /// </summary>
        public static ResolvedRuntimeAssets Resolve(Config cfg, IReadOnlyList<RuntimeAsset>? required = null)
            => ResolveInDirs(SearchDirectories(cfg), required ?? RequiredSet);

        /// <summary>
        /// Ядро разрешения на фиксированном (управляемом) наборе каталогов. Отдельно вынесено,
        /// чтобы тесты могли контролировать набор каталогов без dev-поиска вверх.
        /// </summary>
        public static ResolvedRuntimeAssets ResolveInDirs(IReadOnlyList<string> dirs, IReadOnlyList<RuntimeAsset> required)
        {
            if (required == null) throw new ArgumentNullException(nameof(required));

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            var sha = new Dictionary<string, string>(StringComparer.Ordinal);
            var missing = new List<RuntimeAsset>();
            var badSha = new List<RuntimeAsset>();

            foreach (var asset in required)
            {
                string? found = null;
                foreach (var dir in dirs)
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir, asset.Name);
                    if (File.Exists(candidate))
                    {
                        found = Path.GetFullPath(candidate);
                        break;
                    }
                }

                if (found == null)
                {
                    missing.Add(asset);
                    continue;
                }
                paths[asset.Name] = found;

                if (!string.IsNullOrEmpty(asset.Sha256))
                {
                    string actual = Sha256OfFile(found);
                    sha[asset.Name] = actual;
                    if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        badSha.Add(asset);
                }
            }

            if (missing.Count > 0 || badSha.Count > 0)
                throw new RuntimeAssetsException(
                    missing.Concat(badSha).ToList(),
                    dirs,
                    BuildDiagnostic(missing, badSha, dirs));

            string primary = paths.Count > 0
                ? Path.GetDirectoryName(paths.Values.First())!
                : (dirs.FirstOrDefault() ?? string.Empty);

            return new ResolvedRuntimeAssets
            {
                TapesDir = primary,
                PathsByAsset = paths,
                Sha256ByAsset = sha,
            };
        }

        /// <summary>SHA256 файла (hex, нижний регистр).</summary>
        public static string Sha256OfFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }

        /// <summary>Сериализованный checksum-manifest (для документации/пакетирования).</summary>
        public static string ToManifestJson()
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(Catalog, opts);
        }

        private static string BuildDiagnostic(List<RuntimeAsset> missing, List<RuntimeAsset> badSha, IReadOnlyList<string> dirs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Runtime assets check failed before starting the processor.");
            if (missing.Count > 0)
            {
                sb.AppendLine("Missing required resources:");
                foreach (var a in missing)
                    sb.AppendLine($"  - {a.Name}  [{a.Provenance}]  expected sha256 {a.Sha256}  -> {a.ObtainHint}");
            }
            if (badSha.Count > 0)
            {
                sb.AppendLine("Checksum mismatch (corrupted or wrong version):");
                foreach (var a in badSha)
                    sb.AppendLine($"  - {a.Name}  expected sha256 {a.Sha256}");
            }
            sb.AppendLine("Searched directories:");
            foreach (var d in dirs)
                sb.AppendLine($"  - {d}");
            sb.AppendLine("Provide the resources in one of the directories above (or set the 'tapes' config key / BESM6_PATH) and retry.");
            return sb.ToString();
        }
    }
}
