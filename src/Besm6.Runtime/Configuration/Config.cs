using Besm6.Runtime.Storage;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Besm6.Runtime.Configuration
{
    /// <summary>
    /// Конфигурация симулятора БЭСМ‑6 (загружается из besm6.json).
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// Путь к каталогу лент (tapes).
        /// </summary>
        [JsonPropertyName("tapes")]
        public string? Tapes { get; set; }

        /// <summary>
        /// Путь к образу диска.
        /// </summary>
        [JsonPropertyName("disk")]
        public string? Disk { get; set; }

        /// <summary>
        /// Путь к образу барабана.
        /// </summary>
        [JsonPropertyName("drum")]
        public string? Drum { get; set; }

        /// <summary>
        /// Предел инструкций для команды `run`.
        /// </summary>
        [JsonPropertyName("defaultLimit")]
        public long DefaultLimit { get; set; } = 20_000_000;

        /// <summary>
        /// Предел инструкций для команды `check`.
        /// </summary>
        [JsonPropertyName("checkLimit")]
        public long CheckLimit { get; set; } = 5_000;

        /// <summary>
        /// Базовый адрес загрузки (в восьмеричной системе счисления).
        /// </summary>
        [JsonPropertyName("loadBase")]
        public string? LoadBaseOctal { get; set; } = "1000";

        /// <summary>
        /// Объём ядра памяти (в словах).
        /// </summary>
        [JsonPropertyName("memorySize")]
        public int MemorySize { get; set; } = 32768;

        /// <summary>
        /// E50 067 (DATE*): использовать реальное системное время (localtime).
        /// Флаг -r отключает эту опцию и возвращает фиксированную дату.
        /// </summary>
        [JsonPropertyName("useWallClock")]
        public bool UseWallClock { get; set; } = true;

        [JsonPropertyName("speed")]
        public ExecutionSpeed Speed { get; set; } = ExecutionSpeed.Max;

        /// <summary>
        /// Включить модель часов CTIME/STIME с разрешением 20 мс.
        /// </summary>
        [JsonPropertyName("guestClock")]
        public bool GuestClock { get; set; }

        /// <summary>
        /// Режим совместимости: интерпретировать *forex как *fortran вместо вызова FOREX.
        /// </summary>
        [JsonPropertyName("forexAsFortran")]
        public bool ForexAsFortran { get; set; }

        [JsonIgnore]
        private string? SourceDirectory { get; set; }

        /// <summary>
        /// Загружает конфигурацию из файла.
        /// Если путь не указан явно, ищет besm6.json рядом с исполняемым файлом или в текущей директории.
        /// При неявном поиске отсутствующий файл означает использование значений по умолчанию;
        /// при явно указанном пути отсутствие файла вызовет исключение.
        /// </summary>
        public static Config Load(string? path = null)
        {
            bool explicitPath = path != null;
            if (path == null)
            {
                // Ищем besm6.json рядом с exe или в текущей директории
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
        /// Разрешает относительный путь к ресурсу с учётом расположения конфигурации,
        /// текущего каталога и стандартных директорий.
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
