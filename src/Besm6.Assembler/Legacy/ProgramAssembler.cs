namespace Besm6.Asm
{
    using global::Besm6.Assembler;

    /// <summary>Устаревший результат ассемблирования программы.</summary>
    [Obsolete("Используйте Besm6.Assembler.AssemblyResult")]
    public sealed class AsmResult
    {
        public int BaseAddr { get; set; }
        public List<long> Words { get; set; } = new();
        public Dictionary<string, int> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Устаревший адаптер к новому ассемблеру. Делегирует <see cref="AutoDetectingAssembler"/>.
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler.IBesm6Assembler (MadlenAssembler/BemshAssembler/AutoDetectingAssembler)")]
    public static class ProgramAssembler
    {
        /// <summary>Ассемблирует программу через новый ассемблер с автоматическим диалектом.</summary>
        public static AsmResult Assemble(IEnumerable<string> lines, int baseAddr = 512)
        {
            var newResult = new AutoDetectingAssembler()
                .AssembleProgram(lines, baseAddr);

            return new AsmResult
            {
                BaseAddr = newResult.BaseAddr,
                Words = newResult.Words.Select(w => (long)w).ToList(),
                Labels = newResult.Labels,
            };
        }
    }
}