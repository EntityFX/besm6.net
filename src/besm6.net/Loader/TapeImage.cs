using System;
using System.IO;
using System.Linq;
using Besm6.Core;

namespace Besm6.Loader
{
    /// <summary>
    /// Образ ленты/диска в SIMH-формате.
    /// Каждые 6 байт — одно 48-битное слово (big-endian).
    /// Порт dubna/disk.cpp (simh_to_memory / memory_to_simh) + dubna/besm6_arch.h.
    /// </summary>
    public sealed class TapeImage
    {
        public const int PageNWords = 1024;
        public const int PageNbytes = PageNWords * 6;
        public const int DiskZoneNWords = 8 + 1024;   // 1032
        public const int DiskZoneOffset = 4;          // DISK_ZONE_OFFSET
        public const int DrumNWords = 32 * 1024;      // 040 oct = 32 барабана * 1024

        // Заранее определённые ленты (порт machine.h).
        // В C++ константы заданы восьмеричными литералами в TEXT-кодировке:
        //   TAPE_MONSYS    = 055'57'56'63'71'63'00'11  -> 0xB6FBB3E73009
        //   TAPE_LIBRAR_12 = 054'51'42'62'41'62'00'22  -> 0xB298B2872012
        //   TAPE_LIBRAR_37 = 054'51'42'62'41'62'00'67  -> 0xB298B2872037
        //   TAPE_BEMSH     = 044'51'63'60'41'43'33'31  -> 0x929CF08636D9
        //   TAPE_B         = 042'00'00'00'00'00'00'07  -> 0x880000000007
        public const long TapeMonsys    = 0xB6FBB3E73009; // MONSYS, номер 9
        public const long TapeLibrar12  = 0xB298B2872012; // LIBRAR, номер 12
        public const long TapeLibrar37  = 0xB298B2872037; // LIBRAR, номер 37
        public const long TapeBemsh     = 0x929CF08636D9; // DISPAC, 739
        public const long TapeB         = 0x880000000007; // B, номер 7

        /// <summary>Идентификатор тома (tape-id).</summary>
        public long VolumeId { get; }

        /// <summary>Сырые байты образа (6 байт на слово).</summary>
        public byte[] Data { get; }

        /// <summary>Число зон диска.</summary>
        public int NumZones { get; }

        /// <summary>Число слов в образе.</summary>
        public int NumWords => Data.Length / 6;

        public bool ReadOnly { get; }

        public TapeImage(long volumeId, byte[] data, bool readOnly = true)
        {
            VolumeId = volumeId;
            Data = data;
            ReadOnly = readOnly;
            // Dense layout: 1024 words (6 bytes each) per zone.
            NumZones = data.Length / PageNWords / 6;
        }

        /// <summary>
        /// Загрузить образ ленты из файла SIMH-формата.
        /// </summary>
        public static TapeImage LoadFromFile(long volumeId, string path, bool readOnly = true)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Tape image not found: {path}");
            byte[] data = File.ReadAllBytes(path);
            if (data.Length % 6 != 0)
                throw new InvalidDataException($"Tape image '{path}' has invalid size (not multiple of 6 bytes).");
            return new TapeImage(volumeId, data, readOnly);
        }

        /// <summary>
        /// Чтение зоны/сектора с диска в память (порт simh_to_memory).
        /// Сектор = 1/4 страницы (256 слов).
        /// </summary>
        public void ReadToMemory(IMemory memory, uint zone, uint sector, int addr, int nwords)
        {
            // C++ disk_io не проверяет границы — читает нули при OOB.
            // Dense layout (embedded_to_memory / file_to_memory): word_idx = 1024*zone + 256*sector.
            uint offsetWords = (uint)PageNWords * zone + (uint)(256 * sector);
            int offsetBytes = (int)offsetWords * 6;

            for (int i = 0; i < nwords; i++)
            {
                int bytePos = offsetBytes + i * 6;
                long w = 0;
                for (int b = 0; b < 6; b++)
                {
                    byte byteVal = (bytePos + b < Data.Length) ? Data[bytePos + b] : (byte)0;
                    w = (w << 8) | byteVal;
                }
                memory.Write(((uint)(addr + i)) & 0x7FFFu, new Word48((ulong)(w & 0xFFFFFFFFFFFFL)));
            }
        }

        /// <summary>
        /// Запись из памяти в образ диска (порт memory_to_simh).
        /// </summary>
        public void WriteFromMemory(IMemory memory, uint zone, uint sector, int addr, int nwords)
        {
            if (ReadOnly) return;
            // C++ disk_io не проверяет границы — запись в OOB молча игнорируется.
            // Dense layout (memory_to_file): word_idx = 1024*zone + 256*sector.
            uint offsetWords = (uint)PageNWords * zone + (uint)(256 * sector);
            int offsetBytes = (int)offsetWords * 6;

            for (int i = 0; i < nwords; i++)
            {
                ulong w = memory.Read(((uint)(addr + i)) & 0x7FFFu).Value & 0xFFFFFFFFFFFFu;
                if (offsetBytes + i * 6 + 6 <= Data.Length)
                {
                    Data[offsetBytes + i * 6 + 0] = (byte)(w >> 40);
                    Data[offsetBytes + i * 6 + 1] = (byte)(w >> 32);
                    Data[offsetBytes + i * 6 + 2] = (byte)(w >> 24);
                    Data[offsetBytes + i * 6 + 3] = (byte)(w >> 16);
                    Data[offsetBytes + i * 6 + 4] = (byte)(w >> 8);
                    Data[offsetBytes + i * 6 + 5] = (byte)w;
                }
            }
        }

        /// <summary>
        /// Записать слово в образ по индексу слова (для построения барабана).
        /// </summary>
        public void WriteWord(int wordIndex, long value)
        {
            if (ReadOnly)
                throw new InvalidOperationException("Cannot write to read-only disk image");
            int bytePos = wordIndex * 6;
            if (bytePos + 6 > Data.Length)
                throw new IndexOutOfRangeException($"Word index {wordIndex} out of tape image range");
            value &= 0xFFFFFFFFFFFFL;
            Data[bytePos + 0] = (byte)(value >> 40);
            Data[bytePos + 1] = (byte)(value >> 32);
            Data[bytePos + 2] = (byte)(value >> 24);
            Data[bytePos + 3] = (byte)(value >> 16);
            Data[bytePos + 4] = (byte)(value >> 8);
            Data[bytePos + 5] = (byte)value;
        }

        /// <summary>
        /// Прочитать слово из образа по индексу слова.
        /// </summary>
        public long ReadWord(int wordIndex)
        {
            int bytePos = wordIndex * 6;
            long w = 0;
            for (int b = 0; b < 6; b++)
            {
                byte byteVal = (bytePos + b < Data.Length) ? Data[bytePos + b] : (byte)0;
                w = (w << 8) | byteVal;
            }
            return w & 0xFFFFFFFFFFFFL;
        }

        /// <summary>
        /// Преобразовать имя ленты (например "9/monsys") в tape-id.
        /// Формируется из номера (2-10) и 6 символов TEXT-кодировки.
        /// Упрощённо: сопоставление по известным лентам.
        /// </summary>
        public static long TapeIdByName(string name)
        {
            string n = (name ?? "").ToLowerInvariant().Trim();
            if (n.Contains("monsys") || n == "9") return TapeMonsys;
            if (n.Contains("librar"))
            {
                // Номер определяет tape-id.
                if (n.Contains("12")) return TapeLibrar12;
                return TapeLibrar37;
            }
            if (n.Contains("bemsh") || n.Contains("dispac")) return TapeBemsh;
            if (n == "b" || n.StartsWith("b/")) return TapeB;
            return 0;
        }

        /// <summary>
        /// Выбрать tape-id по карте '*tape:канал/имя,Z'.
        /// Приоритет — канал: номер ленты в tape-id — это тот же восьмеричный канал
        /// из карты (канон. константы: 011'oct→Monsys, 012'oct→Librar12,
        /// 037'oct→Librar37, 007'oct→B, 0331'oct→Bemsh). Имя — только fallback.
        /// </summary>
        public static long TapeIdByName(string name, int channel)
        {
            // channel — десятичное значение восьмеричного номера канала из карты.
            switch (channel)
            {
                case 9:   // 011 oct — MONSYS
                    return TapeMonsys;
                case 10:  // 012 oct — LIBRAR 12 (CERN librar.12)
                    return TapeLibrar12;
                case 31:  // 037 oct — LIBRAR 37
                    return TapeLibrar37;
                case 7:   // 007 oct — B (компилятор)
                    return TapeB;
                case 217: // 0331 oct — BEMSH / DISPAC
                    return TapeBemsh;
            }
            // Неизвестный канал — fallback по имени (старое поведение).
            return TapeIdByName(name);
        }
        /// <summary>
        /// Найти файл образа по tape-id в каталоге dubna/tapes (или BESM6_PATH).
        /// </summary>
        public static string? FindImagePath(long tapeId, string? tapesDir = null)
        {
            string dir = tapesDir ?? DefaultTapesDir();
            string fileName = tapeId switch
            {
                TapeMonsys => "monsys.9",
                TapeLibrar12 => "librar.12",
                TapeLibrar37 => "librar.37",
                TapeBemsh => "bemsh.739",
                TapeB => "b.7",
                _ => null
            };
            if (fileName == null) return null;
            string path = Path.Combine(dir, fileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Каталог образов по умолчанию: поиск вверх от текущей директории и от
        /// AppContext.BaseDirectory до каталога tapes/ (или ref/tapes).
        /// Переменная окружения BESM6_PATH имеет приоритет.
        /// </summary>
        public static string DefaultTapesDir()
        {
            // 1. Явный каталог из окружения.
            string? env = Environment.GetEnvironmentVariable("BESM6_PATH");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return env;

            // 2. Подъём от рабочей директории до tapes/.
            string? found = FindTapesDirUpFrom(Environment.CurrentDirectory);
            // 3. Подъём от каталога сборки (важно при запуске тестов из bin/Debug/netX.0).
            if (found == null)
                found = FindTapesDirUpFrom(Path.GetDirectoryName(AppContext.BaseDirectory));

            return found ?? "tapes";
        }

        private static string? FindTapesDirUpFrom(string? dir)
        {
            int depth = 0;
            while (dir != null && depth < 12)
            {
                string candidate = Path.Combine(dir, "tapes");
                if (Directory.Exists(candidate))
                    return candidate;
                string refCandidate = Path.Combine(dir, "ref", "tapes");
                if (Directory.Exists(refCandidate))
                    return refCandidate;
                dir = Path.GetDirectoryName(dir);
                depth++;
            }
            return null;
        }
    }
}