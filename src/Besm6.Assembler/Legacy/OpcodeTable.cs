namespace Besm6.Asm
{
    /// <summary>
    /// Устаревшая публичная таблица мнемоник. Делегирует
    /// <see cref="global::Besm6.Assembler.OpcodeTable"/>.
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler (внутренняя OpcodeTable)")]
    public static class OpcodeTable
    {
        public static readonly string[] ShortMadlen = global::Besm6.Assembler.OpcodeTable.ShortMadlen;
        public static readonly string[] LongMadlen = global::Besm6.Assembler.OpcodeTable.LongMadlen;
        public static readonly string[] ShortBemsh = global::Besm6.Assembler.OpcodeTable.ShortBemsh;
        public static readonly string[] LongBemsh = global::Besm6.Assembler.OpcodeTable.LongBemsh;

        public static string GetOpName(int opcode)
            => global::Besm6.Assembler.OpcodeTable.GetOpName(opcode);

        public static string GetOpNameBemsh(int opcode)
            => global::Besm6.Assembler.OpcodeTable.GetOpNameBemsh(opcode);

        public static bool TryGetOpcode(string opname, out int opcode)
            => global::Besm6.Assembler.OpcodeTable.TryGetOpcode(opname, global::Besm6.Assembler.AssemblyDialect.Auto, out opcode);
    }
}