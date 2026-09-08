namespace Besm6.Architecture
{
    /// <summary>
    /// Флаги регистра режима арифметического устройства R (РАУ).
    /// Порт флагов регистра режима из dubna/processor.cpp.
    /// </summary>
    [Flags]
    public enum RFlags : byte
    {
        /// <summary>Запрет нормализации.</summary>
        NormDisable  = 0b000001, // 001 oct
        /// <summary>Запрет округления.</summary>
        RoundDisable = 0b000010, // 002 oct
        /// <summary>Логический режим.</summary>
        Log          = 0b000100, // 004 oct
        /// <summary>Мультипликативный режим.</summary>
        Mult         = 0b001000, // 010 oct
        /// <summary>Аддитивный режим.</summary>
        Add          = 0b010000, // 020 oct
        /// <summary>Запрет прерывания по переполнению.</summary>
        OvfDisable   = 0b100000, // 040 oct

        /// <summary>Маска режима (Log|Mult|Add).</summary>
        Mode = Log | Mult | Add,
    }
}
