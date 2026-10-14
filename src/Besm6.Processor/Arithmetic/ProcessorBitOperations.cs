using Besm6.Architecture.Machine;
using System;
using System.Numerics;

namespace Besm6.Processor.Arithmetic
{
    /// <summary>
    /// Битовые операции БЭСМ-6: pack, unpack, подсчёт единиц и старшего бита,
    /// нормализация адреса и установка бита.
    /// </summary>
    internal static class ProcessorBitOperations
    {
        /// <summary>Нормализует адрес к 15 битам (маска 0x7FFF).</summary>
        internal static uint Addr(uint x) => ArchitectureConstants.NormalizeAddress(x);

        /// <summary>Возвращает 48-битное слово с установленным битом n.</summary>
        internal static ulong OnBit(int n) => ArchitectureConstants.OnBit(n);

        /// <summary>
        /// Индекс старшего установленного бита в 48-битном слове
        /// (порт besm6_arch.cpp). Возвращает номер бита в нумерации БЭСМ-6.
        /// </summary>
        internal static int Besm6HighestBit(ulong val)
        {
            return val == 0 ? 48 : BitOperations.LeadingZeroCount(val) - 15;
        }

        /// <summary>Подсчитывает число установленных битов в 48-битном слове.</summary>
        internal static int Besm6CountOnes(ulong word)
        {
            return BitOperations.PopCount(word);
        }

        /// <summary>
        /// Упаковывает значение по маске (порт besm6_arch.cpp): единичные биты маски
        /// выбирают биты значения, собирая их к младшему концу результата.
        /// </summary>
        internal static ulong Pack(ulong val, ulong mask)
        {
            ulong result = 0;
            while (mask != 0)
            {
                if ((mask & 1) != 0)
                {
                    result >>= 1;
                    if ((val & 1) != 0)
                        result |= ArchitectureConstants.BIT48;
                }
                mask >>= 1;
                val >>= 1;
            }
            return result & ArchitectureConstants.BITS48;
        }

        /// <summary>
        /// Распаковывает значение по маске (порт besm6_arch.cpp): единичные биты маски
        /// указывают позиции, в которые вставляются биты значения от младшего к старшему.
        /// </summary>
        internal static ulong Unpack(ulong val, ulong mask)
        {
            ulong result = 0;
            for (int i = 0; i < 48; i++)
            {
                result <<= 1;
                if ((mask & ArchitectureConstants.BIT48) != 0)
                {
                    if ((val & ArchitectureConstants.BIT48) != 0)
                        result |= 1;
                    val <<= 1;
                }
                mask <<= 1;
            }
            return result & ArchitectureConstants.BITS48;
        }
    }
}
