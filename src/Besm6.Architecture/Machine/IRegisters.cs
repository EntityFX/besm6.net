using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine
{
    public interface IRegisters
    {
        /// <summary>
        /// K — счётчик команд.
        /// </summary>
        uint K { get; set; }
        /// <summary>A — аккумулятор.</summary>
        Word48 A { get; set; }
        /// <summary>Y — регистр младших разрядов.</summary>
        Word48 Y { get; set; }
        /// <summary>R — регистр режима арифметического устройства.</summary>
        uint R { get; set; }
        /// <summary>M — индексные регистры.</summary>
        uint[] M { get; }
        /// <summary>C — регистр модификации адреса.</summary>
        uint C { get; set; }

        bool ApplyC { get; set; }

        /// <summary>
        /// AEX
        /// </summary>
        uint EffectiveAddress { get; set; }

    }
}
