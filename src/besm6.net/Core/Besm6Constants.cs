namespace Besm6.Core
{
    /// <summary>
    /// Константы архитектуры БЭСМ-6.
    /// Все магические числа из Processor.cs, MachineCore.cs, TapeImage.cs собраны в одном месте.
    /// </summary>
    public static class Besm6Constants
    {
        // ─── Память ───────────────────────────────────────────────────────────
        /// <summary>Размер памяти ядра, слов (0x7FFF + 1 = 32768).</summary>
        public const int MemoryWords = 32768;

        /// <summary>Маска 15-битного адреса ядра.</summary>
        public const uint AddrMask = 0x7FFF;

        // ─── Слово (нумерация БЭСМ-6: BIT40 = 40-й бит = битовый индекс 39) ───
        /// <summary>40-й бит (индекс 39): старший бит мантиссы.</summary>
        public const long BIT40 = 1L << 39;

        /// <summary>41-й бит (индекс 40): первый бит экспоненты.</summary>
        public const long BIT41 = 1L << 40;

        /// <summary>48-й бит (индекс 47): старший бит слова (знак).</summary>
        public const long BIT48 = 1L << 47;

        /// <summary>49-й бит (индекс 48): перенос / бит вне слова.</summary>
        public const long BIT49 = 1L << 48;

        /// <summary>Маска нижних 40 бит (индексы 0..39).</summary>
        public const long BITS40 = (1L << 40) - 1;

        /// <summary>Маска нижних 41 бит (индексы 0..40).</summary>
        public const long BITS41 = (1L << 41) - 1;

        /// <summary>Маска нижних 42 бит (индексы 0..41).</summary>
        public const long BITS42 = (1L << 42) - 1;

        /// <summary>Маска 48-битного слова (индексы 0..47).</summary>
        public const long BITS48 = (1L << 48) - 1;

        // ─── Регистры ─────────────────────────────────────────────────────────
        /// <summary>Число регистров модификаторов.</summary>
        public const int RegCount = 16;

        /// <summary>Регистр стека (M[15]).</summary>
        public const int StackReg = 15;

        /// <summary>Регистр модификатора (M[0]).</summary>
        public const int ModReg = 0;

        /// <summary>Регистр обменника (M[14]) — используется для E50/E64/E70.</summary>
        public const int ExchangeReg = 14;

        /// <summary>Смещение (bias) экспоненты: 64.</summary>
        public const int ExponentBias = 64;

        // ─── Устройство I/O ───────────────────────────────────────────────────
        /// <summary>Адрес устройства: консоль (teletype).</summary>
        public const int DeviceAddr_Console = 0x1000;

        /// <summary>Адрес устройства: магнитный диск.</summary>
        public const int DeviceAddr_Disk = 0x2000;

        /// <summary>Адрес устройства: магнитный барабан.</summary>
        public const int DeviceAddr_Drum = 0x3000;

        /// <summary>Адрес устройства: перфоратор / читалка.</summary>
        public const int DeviceAddr_Teletype = 0x4000;

        // ─── Диск / Барабан ───────────────────────────────────────────────────
        /// <summary>Слов на страницу диска.</summary>
        public const int PageWords = 1024;

        /// <summary>Слов на сектор (1/4 страницы).</summary>
        public const int SectorWords = 256;

        /// <summary>Слов на зону диска (8 + 1024).</summary>
        public const int DiskZoneWords = 1032;

        /// <summary>Смещение зоны диска (DISK_ZONE_OFFSET).</summary>
        public const int DiskZoneOffset = 4;

        /// <summary>Слов на барабан (32 × 1024).</summary>
        public const int DrumWords = 32 * 1024;

        // ─── Загрузчик ────────────────────────────────────────────────────────
        /// <summary>Базовый адрес загрузки raw-программ (01000 oct = 512 dec).</summary>
        public const int DefaultLoadBase = 512;

        // ─── Операции ─────────────────────────────────────────────────────────
        /// <summary>n-й бит (1-index, нумерация БЭСМ-6).</summary>
        public static long OnBit(int n) => 1L << (n - 1);

        /// <summary>Нормализация адреса: 15-битная маска.</summary>
        public static uint Addr(uint a) => a & AddrMask;
    }
}