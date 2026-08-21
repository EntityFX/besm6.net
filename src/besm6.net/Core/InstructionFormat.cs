namespace Besm6.Core
{
    /// <summary>
    /// Формат инструкции БЭСМ-6.
    /// </summary>
    public enum InstructionFormat
    {
        Short,  // 019 bit — короткая (12-bit addr, 6-bit opcode)
        Long,   // 020 bit — длинная (15-bit addr, 7-bit opcode)
    }
}
