using System;
using Besm6.Core;

namespace Besm6.Loader
{
    /// <summary>
    /// Математические функции БЭСМ-6.
    /// Формат: [7-bit exponent (bias 64)][1 sign][40-bit magnitude]
    /// value = (-1)^sign * (magnitude / 2^40) * 2^(exp-64)
    /// </summary>
    public static class Besm6Math
    {
        private const long SIGN_BIT = 1L << 40;
        private const long MAGNITUDE_MASK = (1L << 40) - 1;
        private const int EXPO_BIAS = 64;

        // ─── Публичные конверсии ─────────────────────────────────────────────
        //
        // Точный порт dubna/besm6_arch.cpp: besm6_to_ieee / ieee_to_besm6.
        // Число БЭСМ-6: биты 47..41 — порядок (bias=64), биты 40..0 —
        // мантисса в ДОПОЛНИТЕЛЬНОМ коде (знак в бите 40).
        // value = mantissa / 2^40 * 2^(exponent - 64), где mantissa — знаковое
        // 41-битное целое (two's complement).

        public static double Besm6ToDouble(ulong word)
        {
            // Используем Word48.ToDouble для гарантированно правильной конверсии.
            // Word48.ToDouble реализует тот же алгоритм, но с правильным обращением
            // с 48-битными значениями и сдвигами.
            return new Word48(word).ToDouble();
        }

        public static ulong DoubleToBesm6(double input)
        {
            if (double.IsNaN(input) || double.IsInfinity(input))
                return 0;

            // Переполнение/особы точки: C++ oct-константы как 48-битные значения.
            const long OVERFLOW_POS = 0xFEFFFFFFFFFFL; // 07757 7777 7777 7777
            const long SMALLEST_NEG = 0x0BFFFFFFFFFFL; // 0027 7777 7777 7777
            const long OVERFLOW_NEG = 0xFF0000000000L; // 07760 0000 0000 0000

            // frexp(input): мантисса в [0.5, 1) (или (-1, -0.5]) и порядок.
            double m;
            int exponent;
            if (input == 0.0)
            {
                return 0;
            }
            else
            {
                int ilog = Math.ILogB(input);
                exponent = ilog + 1;
                m = Math.ScaleB(input, -exponent);
            }

            // ldexp(mantissa, 40)
            m = Math.ScaleB(m, 40);

            long word;
            if (m > 0)
            {
                // Положительное значение в диапазоне [0.5, 1) * 2^40.
                word = (long)m;
                if (m - word >= 0.5)
                {
                    word += 1;
                    if (word == 1L << 40)
                    {
                        word >>= 1;
                        exponent += 1;
                    }
                }
                if (exponent > 63)
                    return OVERFLOW_POS;
            }
            else
            {
                // Отрицательное значение в диапазоне (-1, -0.5] * 2^40.
                if (m == -(1L << 39))
                {
                    if (exponent == -64)
                        return SMALLEST_NEG;
                    m += m;
                    exponent -= 1;
                }

                // Учесть знаковый бит; значение становится положительным.
                m += 1L << 40;

                word = (long)m;
                if (m - word > 0.5)
                {
                    word += 1;
                    if (word == 1L << 40)
                    {
                        word >>= 1;
                        exponent += 1;
                    }
                }
                if (exponent > 63)
                    return OVERFLOW_NEG;
                word |= 1L << 40;
            }

            if (exponent < -64)
                return 0;

            word |= ((long)(exponent + 64)) << 41;
            return (ulong)(word & 0xFFFFFFFFFFFFL);
        }

        // ─── Публичные математические функции ─────────────────────────────────

        public static ulong Sqrt(ulong word)
        {
            double d = Besm6ToDouble(word);
            if (d < 0) d = 0;
            return DoubleToBesm6(Math.Sqrt(d));
        }

        public static ulong Sin(ulong word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Sin(d));
        }

        public static ulong Cos(ulong word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Cos(d));
        }

        public static ulong Atan(ulong word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Atan(d));
        }

        public static ulong Asin(ulong word)
        {
            double d = Besm6ToDouble(word);
            if (d < -1) d = -1;
            if (d > 1) d = 1;
            return DoubleToBesm6(Math.Asin(d));
        }

        public static ulong Log(ulong word)
        {
            double d = Besm6ToDouble(word);
            if (d <= 0) return 0;
            return DoubleToBesm6(Math.Log(d)); // натуральный логарифм ln(x)
        }

        public static ulong Exp(ulong word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Exp(d)); // e^x
        }

        public static ulong Floor(ulong word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Floor(d));
        }
    }
}
