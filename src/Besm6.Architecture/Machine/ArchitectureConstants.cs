namespace Besm6.Architecture.Machine
{
    /// <summary>
    /// Архитектурные константы БЭСМ-6: маски, номера разрядов, номера специальных
    /// регистров.
    /// </summary>
    public static class ArchitectureConstants
    {
        // Номера и маски битов (БЭСМ-6: бит 1 — младший).
        /// <summary>
        /// 40-й бит - старший разряд мантиссы
        /// </summary>
        public const long BIT40 = 1L << 39;
        /// <summary>
        /// 41-й бит - знак
        /// </summary>
        public const long BIT41 = 1L << 40;
        /// <summary>
        /// 42-й бит - дубль-знак в мантиссе
        /// </summary>
        public const long BIT42 = 1L << 41;
        /// <summary>
        /// 48-й бит
        /// </summary>
        public const long BIT48 = 1L << 47;
        public const long BITS40 = (1L << 40) - 1;
        public const long BITS41 = (1L << 41) - 1;
        public const long BITS42 = (1L << 42) - 1;
        public const long BITS48 = (1L << 48) - 1;

        /// <summary>
        /// Адресная часть слова (младшие 15 бит)
        /// </summary>
        public const uint AddrMask = 0x7FFF;

        // Специальные регистры.
        public const int IndexRegCount = 16;
        public const int StackReg = 15;
        public const int ExchangeReg = 14;
        public const int ExponentStartBit = 41;

        /// <summary>
        /// Сдвиг порядкового номера в числе с плавающей точкой.
        /// </summary>
        public const int ExponentBias = 64;

        /// <summary>Число слов в блоке ОЗУ</summary>
        public const uint WordCount = 32768;

        /// <summary>Число блоков ОЗУ</summary>
        public const uint BankCount = 8;

        /// <summary>Маска с единственным битом n (1-based) установленным.</summary>
        public static ulong OnBit(int n) => 1UL << (n - 1);

        /// <summary>Нормализует адрес: младшие 15 бит, биты 16..40 (режим) отброшены.</summary>
        public static uint NormalizeAddress(uint a) => a & AddrMask;
    }
}
