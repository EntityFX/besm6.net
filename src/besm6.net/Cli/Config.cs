using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        /// Загрузить конфигурацию из файла. Если файл не найден — дефолтные значения.
        /// </summary>
        public static Config Load(string? path = null)
        {
            if (path == null)
            {
                // Ищем besm6.json рядом с exe или в текущей директории.
                path = Path.Combine(AppContext.BaseDirectory, "besm6.json");
                if (!File.Exists(path))
                    path = "besm6.json";
            }

            if (!File.Exists(path))
                return new Config();

            string json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Config>(json, opts) ?? new Config();
        }

        /// <summary>
        /// Разрешить относительный путь к ресурсу относительно корня проекта.
        /// </summary>
        public string ResolvePath(string relative)
        {
            // Пробуем относительно текущей директории, затем относительно AppContext.
            if (File.Exists(relative) || Directory.Exists(relative))
                return Path.GetFullPath(relative);

            string appDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relative);
            if (File.Exists(appDir) || Directory.Exists(appDir))
                return Path.GetFullPath(appDir);

            return Path.GetFullPath(relative);
        }
    }
}