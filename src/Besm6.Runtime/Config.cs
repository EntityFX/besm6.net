using Besm6.Runtime;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Besm6
{
    /// <summary>
    /// РљРѕРЅС„РёРіСѓСЂР°С†РёСЏ СЃРёРјСѓР»СЏС‚РѕСЂР° Р‘Р­РЎРњ-6 (Р·Р°РіСЂСѓР¶Р°РµС‚СЃСЏ РёР· besm6.json).
    /// </summary>
    public sealed class Config
    {
        /// <summary>РџСѓС‚СЊ Рє РєР°С‚Р°Р»РѕРіСѓ Р»РµРЅС‚ (tapes).</summary>
        [JsonPropertyName("tapes")]
        public string? Tapes { get; set; }

        /// <summary>РџСѓС‚СЊ Рє РѕР±СЂР°Р·Сѓ РґРёСЃРєР°.</summary>
        [JsonPropertyName("disk")]
        public string? Disk { get; set; }

        /// <summary>РџСѓС‚СЊ Рє РѕР±СЂР°Р·Сѓ Р±Р°СЂР°Р±Р°РЅР°.</summary>
        [JsonPropertyName("drum")]
        public string? Drum { get; set; }

        /// <summary>РџСЂРµРґРµР» РёРЅСЃС‚СЂСѓРєС†РёР№ РґР»СЏ `run`.</summary>
        [JsonPropertyName("defaultLimit")]
        public long DefaultLimit { get; set; } = 20_000_000;

        /// <summary>РџСЂРµРґРµР» РёРЅСЃС‚СЂСѓРєС†РёР№ РґР»СЏ `check`.</summary>
        [JsonPropertyName("checkLimit")]
        public long CheckLimit { get; set; } = 5_000;

        /// <summary>Р‘Р°Р·РѕРІС‹Р№ Р°РґСЂРµСЃ Р·Р°РіСЂСѓР·РєРё (РІРѕСЃСЊРјРµСЂРёС‡РЅС‹Р№).</summary>
        [JsonPropertyName("loadBase")]
        public string? LoadBaseOctal { get; set; } = "1000";

        /// <summary>РћР±СЉС‘Рј СЏРґСЂР° РїР°РјСЏС‚Рё (СЃР»РѕРІ).</summary>
        [JsonPropertyName("memorySize")]
        public int MemorySize { get; set; } = 32768;

        /// <summary>
        /// E50 067 (DATE*): РёСЃРїРѕР»СЊР·РѕРІР°С‚СЊ СЂРµР°Р»СЊРЅРѕРµ СЃРёСЃС‚РµРјРЅРѕРµ РІСЂРµРјСЏ (localtime).
        /// С„Р»Р°Рі -r РѕС‚РєР»СЋС‡Р°РµС‚ РµС‘ Рё РІРѕР·РІСЂР°С‰Р°РµС‚ С„РёРєСЃРёСЂРѕРІР°РЅРЅСѓСЋ РґР°С‚Сѓ.
        /// </summary>
        [JsonPropertyName("useWallClock")]
        public bool UseWallClock { get; set; } = true;

        [JsonIgnore]
        private string? SourceDirectory { get; set; }

        /// <summary>
        /// Р—Р°РіСЂСѓР·РёС‚СЊ РєРѕРЅС„РёРіСѓСЂР°С†РёСЋ РёР· С„Р°Р№Р»Р°. РџСЂРё РЅРµСЏРІРЅРѕРј РїРѕРёСЃРєРµ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰РёР№ С„Р°Р№Р»
        /// РѕР·РЅР°С‡Р°РµС‚ Р·РЅР°С‡РµРЅРёСЏ РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ; СЏРІРЅРѕ СѓРєР°Р·Р°РЅРЅС‹Р№ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰РёР№ С„Р°Р№Р» РІС‹Р·С‹РІР°РµС‚
        /// РёСЃРєР»СЋС‡РµРЅРёРµ.
        /// </summary>
        public static Config Load(string? path = null)
        {
            bool explicitPath = path != null;
            if (path == null)
            {
                // РС‰РµРј besm6.json СЂСЏРґРѕРј СЃ exe РёР»Рё РІ С‚РµРєСѓС‰РµР№ РґРёСЂРµРєС‚РѕСЂРёРё.
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
        /// Р Р°Р·СЂРµС€РёС‚СЊ РїСѓС‚СЊ Рє СЂРµСЃСѓСЂСЃСѓ СЃ СѓС‡РµС‚РѕРј СЂР°СЃРїРѕР»РѕР¶РµРЅРёСЏ РєРѕРЅС„РёРіСѓСЂР°С†РёРё,
        /// С‚РµРєСѓС‰РµРіРѕ РєР°С‚Р°Р»РѕРіР° Рё СЃС‚Р°РЅРґР°СЂС‚РЅС‹С… РєР°С‚Р°Р»РѕРіРѕРІ.
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
