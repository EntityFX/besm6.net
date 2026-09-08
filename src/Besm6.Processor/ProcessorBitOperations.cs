using System;

namespace Besm6.Core
{
    /// <summary>
    /// Битовые операции БЭСМ-6: pack, unpack, подсчёт единиц и старшего бита,
    /// нормализация адреса и установка бита. Вынесено из Processor.cs
    /// (Этап 4 рефакторинга). Все методы чистые и не имеют состояния.
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
            int n = 32, cnt = 0;
            do
            {
                ulong tmp = val;
                if ((tmp >>= n) != 0)
                {
                    cnt += n;
                    val = tmp;
                }
            } while ((n >>= 1) != 0);
            return 48 - cnt;
        }

        /// <summary>Подсчитывает число установленных битов в 48-битном слове.</summary>
        internal static int Besm6CountOnes(ulong word)
        {
            int c = 0;
            while (word != 0)
            {
                word &= word - 1;
                c++;
            }
            return c;
        }

        /// <summary>
        /// Упаковывает значение по маске (порт besm6_arch.cpp): единичные биты маски
        /// выбирают биты значения, собирая их к младшему концу результата.
        /// </summary>
        internal static ulong Besm6Pack(ulong val, ulong mask)
        {
            const ulong BIT48 = ArchitectureConstants.BIT48;
            const ulong BITS48 = ArchitectureConstants.BITS48;
            ulong result = 0;
            while (mask != 0)
            {
                if ((mask & 1) != 0)
                {
                    result >>= 1;
                    if ((val & 1) != 0)
                        result |= BIT48;
                }
                mask >>= 1;
                val >>= 1;
            }
            return result & BITS48;
        }

        /// <summary>
        /// Распаковывает значение по маске (порт besm6_arch.cpp): единичные биты маски
        /// указывают позиции, в которые вставляются биты значения от младшего к старшему.
        /// </summary>
        internal static ulong Besm6Unpack(ulong val, ulong mask)
        {
            const ulong BIT48 = ArchitectureConstants.BIT48;
            const ulong BITS48 = ArchitectureConstants.BITS48;
            ulong result = 0;
            for (int i = 0; i < 48; i++)
            {
                result <<= 1;
                if ((mask & BIT48) != 0)
                {
                    if ((val & BIT48) != 0)
                        result |= 1;
                    val <<= 1;
                }
                mask <<= 1;
            }
            return result & BITS48;
        }
    }
}