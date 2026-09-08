namespace Besm6.Asm
{
    using global::Besm6.Assembler;

    /// <summary>
    /// Устаревший статический ассемблер. Делегирует <see cref="AutoDetectingAssembler"/>.
    /// Возвращает 48-битное слово с инструкцией в левом полуслове (совместимо со старым API).
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler.IBesm6Assembler (MadlenAssembler/BemshAssembler/AutoDetectingAssembler)")]
    public static class Assembler
    {
        private static readonly AutoDetectingAssembler _impl = new();

        /// <summary>Преобразует строку ассемблера в 48-битное слово.</summary>
        public static ulong Asm(string src)
        {
            ulong half = _impl.AssembleWord(src);
            // Старый API: одиночное полуслово помещалось в левые 24 бита 48-битного слова.
            // Если значение уже 48-битное (left,right) — не сдвигаем.
            if (half < 0x1000000)
                return half << 24;
            return half;
        }
    }
}