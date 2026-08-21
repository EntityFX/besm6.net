using System;

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

        public static double Besm6ToDouble(long word)
        {
            if (word == 0) return 0.0;
            int exponent = (int)((word >> 41) & 0x7F);
            bool negative = (word & SIGN_BIT) != 0;
            long magnitude = word & MAGNITUDE_MASK;
            double value = (double)magnitude / (1L << 40) * Math.Pow(2, exponent - EXPO_BIAS);
            return negative ? -value : value;
        }

        public static long DoubleToBesm6(double val)
        {
            if (double.IsNaN(val) || double.IsInfinity(val)) return 0;
            if (val == 0.0) return 0;

            bool negative = val < 0;
            double absVal = Math.Abs(val);

            int exp = (int)Math.Floor(Math.Log2(absVal));
            double mantissa = absVal / Math.Pow(2, exp); // [1, 2)

            // magnitude = mantissa * 2^39 (since value = mantissa/2 * 2^(exp+1))
            long magnitude = (long)(mantissa * (1L << 39));

            // Rounding
            double remainder = mantissa * (1L << 39) - magnitude;
            if (remainder >= 0.5)
            {
                magnitude++;
                if (magnitude >= (1L << 40))
                {
                    magnitude >>= 1;
                    exp++;
                }
            }

            int stored_exp = exp + 1;
            if (stored_exp > 63) return (1L << 48) - (1L << 41); // overflow
            if (stored_exp < -63) return 0; // underflow

            long word = magnitude | ((long)(stored_exp + EXPO_BIAS) << 41);
            if (negative) word |= SIGN_BIT;
            return word;
        }

        // ─── Публичные математические функции ─────────────────────────────────

        public static long Sqrt(long word)
        {
            double d = Besm6ToDouble(word);
            if (d < 0) d = 0;
            return DoubleToBesm6(Math.Sqrt(d));
        }

        public static long Sin(long word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Sin(d));
        }

        public static long Cos(long word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Cos(d));
        }

        public static long Atan(long word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Atan(d));
        }

        public static long Asin(long word)
        {
            double d = Besm6ToDouble(word);
            if (d < -1) d = -1;
            if (d > 1) d = 1;
            return DoubleToBesm6(Math.Asin(d));
        }

        public static long Log(long word)
        {
            double d = Besm6ToDouble(word);
            if (d <= 0) return 0;
            return DoubleToBesm6(Math.Log(d, 2));
        }

        public static long Exp(long word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Pow(2, d));
        }

        public static long Floor(long word)
        {
            double d = Besm6ToDouble(word);
            return DoubleToBesm6(Math.Floor(d));
        }
    }
}