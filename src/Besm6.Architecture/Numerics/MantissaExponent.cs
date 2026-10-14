using Besm6.Architecture.Machine;
using System;
using System.Numerics;

namespace Besm6.Architecture.Numerics
{
    /// <summary>
    /// Вспомогательная структура для работы с числом БЭСМ-6 в формате "порядок и мантисса".
    /// Используется для точной эмуляции АЛУ.
    /// Мантисса хранится как знаковое 64-битное значение (знаковое расширение из бита 40).
    /// </summary>
    public struct MantissaExponent
    {
        public uint Exponent;
        public long Mantissa;


        public MantissaExponent(Word48 word)
        {
            ulong val = word.Value;
            Exponent = (uint)((val >> 41) & 0x7F);
            Mantissa = (long)(val & ArchitectureConstants.BITS41);

            // Sign extend: 41-битное значение (биты 40..0, знак в бите 40) -> 64 бита.
            Mantissa <<= ArchitectureConstants.ExponentBias - ArchitectureConstants.ExponentStartBit;
            Mantissa >>= ArchitectureConstants.ExponentBias - ArchitectureConstants.ExponentStartBit;
        }

        public MantissaExponent(uint exponent, long mantissa)
        {
            Exponent = exponent;
            Mantissa = mantissa;
        }

        public void Negate()
        {
            // Изменение знака мантиссы.
            // Примечание: число может стать денормализованным.
            Mantissa = -Mantissa;
        }

        /// <summary>
        /// Отрицательное ли число.
        /// Проверяется бит 42 вместо бита 40, так как значение может быть
        /// </summary>
        public bool IsNegative()
        {
            return (Mantissa & ArchitectureConstants.BIT42) != 0;
        }

        /// <summary>
        /// Возвращает true, если число ненормализованное.
        /// У нормализованного числа биты 42 и 41 совпадают.
        /// </summary>
        public bool IsDenormal()
        {
            return (((Mantissa >> 40) ^ (Mantissa >> ArchitectureConstants.ExponentStartBit)) & 1) != 0;
        }

        public void NormalizeToTheRight()
        {
            // Арифметический сдвиг вправо (для отрицательных - sign fill).
            Mantissa >>= 1;
            Exponent++;
        }

        /// <summary>
        /// Умножение мантиссы на знаковое 41-битное целое.
        /// Верхние 41 бит (знаковые) сохраняются в мантиссе.
        /// Возвращает младшие 40 бит (беззнаковые).
        /// </summary>
        public long Multiply(long x)
        {
            // Вычислить знак.
            long negative = 0;
            if (Mantissa < 0)
            {
                Mantissa = -Mantissa;
                negative ^= 1;
            }
            if (x < 0)
            {
                x = -x;
                negative ^= 1;
            }

            long a = x >> 20;
            long b = x & 0xFFFFF;

            long mr = Mantissa * b;
            Mantissa *= a;
            mr += (Mantissa & 0xFFFFF) << 20;
            Mantissa >>= 20;
            Mantissa += mr >> 40;
            mr &= ArchitectureConstants.BITS40;

            // Negate.
            if (negative != 0)
            {
                Mantissa = ~Mantissa;
                mr ^= ArchitectureConstants.BITS40;
                mr += 1;
                Mantissa += mr >> 40;
                mr &= ArchitectureConstants.BITS40;
            }
            return mr;
        }

        public static int HighestBit(long value)
        {
            if (value == 0) return -1;
            // Возвращает индекс самого старшего установленного бита (0-63).
            return 63 - BitOperations.LeadingZeroCount((ulong)value);
        }
    }
}