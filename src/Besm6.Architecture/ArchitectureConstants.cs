namespace Besm6.Architecture
{
    /// <summary>
    /// Архитектурные константы БЭСМ-6: маски, номера разрядов, номера специальных
    /// регистров. Чистая архитектура — не зависит от исполнения, загрузчика и CLI.
    /// </summary>
    public static class ArchitectureConstants
    {
        // Номера и маски битов (БЭСМ-6: бит 1 — младший).
        public const long BIT40 = 1L << 39;
        public const long BIT41 = 1L << 40;
        public const long BIT48 = 1L << 47;
        public const long BITS40 = (1L << 40) - 1;
        public const long BITS41 = (1L << 41) - 1;
        public const long BITS42 = (1L << 42) - 1;
        public const long BITS48 = (1L << 48) - 1;

        // Адресная часть слова (младшие 15 бит), см. Architecture.Address.
        public const uint AddrMask = 0x7FFF;

        // Специальные регистры.
        public const int IndexRegCount = 16;
        public const int StackReg = 15;
        public const int ExchangeReg = 14;

        // Сдвиг порядкового номера в числе с плавающей точкой.
        public const int ExponentBias = 64;

        /// <summary>Маска с единственным битом n (1-based) установленным.</summary>
        public static ulong OnBit(int n) => 1UL << (n - 1);

        /// <summary>Нормализует адрес: младшие 15 бит, биты 16..40 (режим) отброшены.</summary>
        public static uint NormalizeAddress(uint a) => a & AddrMask;
    }
}
