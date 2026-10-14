using Besm6.Assembler.Api;

namespace Besm6.Assembler.Legacy
{
    /// <summary>
    /// Устаревшая публичная таблица мнемоник. Делегирует
    /// <see cref="Assembler.OpcodeTable"/>.
    /// </summary>
    [Obsolete("Используйте Besm6.Assembler (внутренняя OpcodeTable)")]
    public static class OpcodeTable
    {
        public static readonly string[] ShortMadlen = Parsing.OpcodeTable.ShortMadlen;
        public static readonly string[] LongMadlen = Parsing.OpcodeTable.LongMadlen;
        public static readonly string[] ShortBemsh = Parsing.OpcodeTable.ShortBemsh;
        public static readonly string[] LongBemsh = Parsing.OpcodeTable.LongBemsh;

        public static string GetOpName(int opcode)
            => Parsing.OpcodeTable.GetOpName(opcode);

        public static string GetOpNameBemsh(int opcode)
            => Parsing.OpcodeTable.GetOpNameBemsh(opcode);

        public static bool TryGetOpcode(string opname, out int opcode)
            => Parsing.OpcodeTable.TryGetOpcode(opname, AssemblyDialect.Auto, out opcode);
    }
}