namespace Besm6.Core
{
    /// <summary>
    /// Флаги регистра режима АЛУ (RAU).
    /// Порт из dubna/processor.cpp (RAU_* константы).
    /// </summary>
    [Flags]
    public enum RauFlags : byte
    {
        NormDisable  = 0b000001, // 001 oct
        RoundDisable = 0b000010, // 002 oct
        Log          = 0b000100, // 004 oct
        Mult         = 0b001000, // 010 oct
        Add          = 0b010000, // 020 oct
        OvfDisable   = 0b100000, // 040 oct

        /// <summary>Маска режима (Log|Mult|Add).</summary>
        Mode = Log | Mult | Add,
    }
}
