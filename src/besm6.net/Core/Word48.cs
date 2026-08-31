using System;
using System.Numerics;

namespace Besm6.Core
{
    /// <summary>
    /// Представляет 48-битное машинное слово БЭСМ-6.
    /// </summary>
    public struct Word48 : IEquatable<Word48>, IComparable<Word48>
    {
        // Используем long для хранения 48-битного значения.
        // Значение всегда хранится в диапазоне [0, 2^48 - 1].
        private readonly ulong _value;

        public const ulong Mask48 = (1L << 48) - 1;

        public static Word48 Zero = new Word48(0);

        public Word48(ulong value)
        {
            _value = value & Mask48;
        }

        public ulong Value => _value;

        #region Целочисленное представление

        /// <summary>
        /// Получает значение как знаковое 48-битное целое число (дополнительный код).
        /// </summary>
        public ulong ToInt48()
        {
            // Если 47-й бит установлен, число отрицательное
            if ((_value & (1L << 47)) != 0)
            {
                return _value | ~Mask48;
            }
            return _value;
        }

        public static Word48 FromInt48(ulong value)
        {
            return new Word48(value);
        }

        #endregion

        #region Числа с плавающей точкой

        /// <summary>
        /// Преобразует 48-битное слово в double (IEEE 754).
        /// Канонический формат БЭСМ-6: биты 47..41 — порядок (bias=64),
        /// биты 40..0 — мантисса в дополнительном коде (знак в бите 40).
        /// value = mantissa / 2^40 * 2^(exponent - 64).
        /// </summary>
        public double ToDouble()
        {
            ulong w = _value;

            // Сдвиг на 23 переносит знак мантиссы (бит 40) в знак 64-битного целого,
            // т.е. mantissa = (знаковое 41-битное) * 2^23.
            // Важно: результат интерпретируем как ЗНАКОВОЕ 64-битное число —
            // иначе знак мантиссы теряется (ulong всегда положителен).
            long shifted = (long)(w << 23);
            double mantissa = shifted;
            int exponent = (int)(w >> 41);
            return Math.ScaleB(mantissa, exponent - 64 - 63);
        }

        /// <summary>
        /// Создает Word48 из double (порт ieee_to_besm6 из besm6_arch.cpp).
        /// </summary>
        public static Word48 FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return new Word48(0);

            if (value == 0.0)
                return new Word48(0);

            // frexp: мантисса в [0.5, 1) (или (-1, -0.5]) и порядок.
            double m;
            int exponent;
            int ilog = Math.ILogB(value);
            exponent = ilog + 1;
            m = Math.ScaleB(value, -exponent);

            // ldexp(mantissa, 40)
            m = Math.ScaleB(m, 40);

            ulong word;
            if (m > 0)
            {
                // Положительное значение в диапазоне [0.5, 1) * 2^40.
                word = (ulong)m;
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
                    return new Word48(0xFEFFFFFFFFFFL); // 07757 7777 7777 7777
            }
            else
            {
                // Отрицательное значение в диапазоне (-1, -0.5] * 2^40.
                if (m == -(1L << 39))
                {
                    if (exponent == -64)
                        return new Word48(0x0BFFFFFFFFFFL); // 0027 7777 7777 7777
                    m += m;
                    exponent -= 1;
                }

                m += 1L << 40;

                word = (ulong)m;
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
                    return new Word48(0xFF0000000000L); // 07760 0000 0000 0000
                word |= 1L << 40;
            }

            if (exponent < -64)
                return new Word48(0);

            word |= ((ulong)(exponent + 64)) << 41;
            return new Word48(word);
        }

        #endregion

        #region Операции и сравнение

        public bool Equals(Word48 other) => _value == other._value;
        public override bool Equals(object? obj) => obj is Word48 other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public int CompareTo(Word48 other) => _value.CompareTo(other._value);
        public override string ToString() => $"0x{_value:X12}";

        /// <summary>
        /// Преобразует 48-битное слово в восьмеричную строку (16 символов).
        /// </summary>
        public string ToOctal()
        {
            ulong val = _value;
            char[] digits = new char[16];
            for (int i = 15; i >= 0; i--)
            {
                digits[i] = (char)((val & 7) + '0');
                val >>= 3;
            }
            return new string(digits);
        }

        /// <summary>
        /// Создает Word48 из восьмеричной строки.
        /// </summary>
        public static Word48 FromOctal(string oct)
        {
            // Допускает до 17 восьмеричных цифр: старшая (17-я) цифра может быть
            // только '0' (биты 48..50) — она отбрасывается. Берём младшие 16 цифр.
            if (oct.Length > 16)
                oct = oct.Substring(oct.Length - 16);
            ulong val = 0;
            foreach (char c in oct)
            {
                val = (val << 3) | (uint)(c - '0');
            }
            return new Word48(val);
        }

        public static bool operator ==(Word48 left, Word48 right) => left.Equals(right);
        public static bool operator !=(Word48 left, Word48 right) => !left.Equals(right);
        public static bool operator <(Word48 left, Word48 right) => left.CompareTo(right) < 0;
        public static bool operator >(Word48 left, Word48 right) => left.CompareTo(right) > 0;
        public static bool operator <=(Word48 left, Word48 right) => left.CompareTo(right) <= 0;
        public static bool operator >=(Word48 left, Word48 right) => left.CompareTo(right) >= 0;

        #endregion
    }
}
