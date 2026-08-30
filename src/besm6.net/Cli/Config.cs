using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Besm6.Loader;

namespace Besm6
{
    /// <summary>
    /// Конфигурация симулятора БЭСМ-6 (загружается из besm6.json).
    /// </summary>
    public sealed class Config
    {
        /// <summary>Путь к каталогу лент (tapes).</summary>
        [JsonPropertyName("tapes")]
        public string? Tapes { get; set; }

        /// <summary>Путь к образу диска.</summary>
        [JsonPropertyName("disk")]
        public string? Disk { get; set; }

        /// <summary>Путь к образу барабана.</summary>
        [JsonPropertyName("drum")]
        public string? Drum { get; set; }

        /// <summary>Предел инструкций для `run`.</summary>
        [JsonPropertyName("defaultLimit")]
        public long DefaultLimit { get; set; } = 20_000_000;

        /// <summary>Предел инструкций для `check`.</summary>
        [JsonPropertyName("checkLimit")]
        public long CheckLimit { get; set; } = 5_000;

        /// <summary>Базовый адрес загрузки (восьмеричный).</summary>
        [JsonPropertyName("loadBase")]
        public string? LoadBaseOctal { get; set; } = "1000";

        /// <summary>Объём ядра памяти (слов).</summary>
        [JsonPropertyName("memorySize")]
        public int MemorySize { get; set; } = 32768;

        /// <summary>
        /// E50 067 (DATE*): использовать реальное системное время (localtime).
        /// флаг -r отключает её и возвращает фиксированную дату.
        /// </summary>
        [JsonPropertyName("useWallClock")]
        public bool UseWallClock { get; set; } = true;

        [JsonIgnore]
        private string? SourceDirectory { get; set; }

        /// <summary>
        /// Загрузить конфигурацию из файла. При неявном поиске отсутствующий файл
        /// означает значения по умолчанию; явно указанный отсутствующий файл вызывает
        /// исключение.
        /// </summary>
        public static Config Load(string? path = null)
        {
            bool explicitPath = path != null;
            if (path == null)
            {
                // Ищем besm6.json рядом с exe или в текущей директории.
                path = Path.Combine(AppContext.BaseDirectory, "besm6.json");
                if (!File.Exists(path))
                    path = "besm6.json";
            }

            if (!File.Exists(path))
            {
                if (explicitPath)
                    throw new FileNotFoundException("Configuration file not found", path);
                return new Config();
            }

            string fullPath = Path.GetFullPath(path);
            string json = File.ReadAllText(fullPath);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Config config = JsonSerializer.Deserialize<Config>(json, opts) ?? new Config();
            config.SourceDirectory = Path.GetDirectoryName(fullPath);
            return config;
        }

        /// <summary>
        /// Разрешить относительный путь к ресурсу относительно корня проекта.
        /// </summary>
        public string ResolvePath(string relative)
        {
            if (Path.IsPathRooted(relative) && (File.Exists(relative) || Directory.Exists(relative)))
                return Path.GetFullPath(relative);

            if (SourceDirectory != null)
            {
                string fromConfig = Path.Combine(SourceDirectory, relative);
                if (File.Exists(fromConfig) || Directory.Exists(fromConfig))
                    return Path.GetFullPath(fromConfig);
            }

            if (File.Exists(relative) || Directory.Exists(relative))
                return Path.GetFullPath(relative);

            string fromApp = Path.Combine(AppContext.BaseDirectory, relative);
            if (File.Exists(fromApp) || Directory.Exists(fromApp))
                return Path.GetFullPath(fromApp);

            if (string.Equals(relative.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              "tapes", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(TapeImage.DefaultTapesDir());

            return Path.GetFullPath(SourceDirectory == null
                ? relative
                : Path.Combine(SourceDirectory, relative));
        }
    }
}
