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
        private readonly long _value;

        public const long Mask48 = (1L << 48) - 1;

        public Word48(long value)
        {
            _value = value & Mask48;
        }

        public long Value => _value;

        #region Целочисленное представление

        /// <summary>
        /// Получает значение как знаковое 48-битное целое число (дополнительный код).
        /// </summary>
        public long ToInt48()
        {
            // Если 47-й бит установлен, число отрицательное
            if ((_value & (1L << 47)) != 0)
            {
                return _value | ~Mask48;
            }
            return _value;
        }

        public static Word48 FromInt48(long value)
        {
            return new Word48(value);
        }

        #endregion

        #region Числа с плавающей точкой

        /// <summary>
        /// Преобразует 48-битное слово в double (IEEE 754).
        /// Формат БЭСМ-6: [47: Знак] [46-41: Порядок] [40-1: Мантисса] [0: 0]
        /// </summary>
        public double ToDouble()
        {
            if (_value == 0) return 0.0;

            long signBit = (_value >> 47) & 1;
            long order = (_value >> 41) & 0x3F;
            long mantissa = (_value >> 1) & 0xFFFFFFFFFF;

            // BESM-6: Value = Mantissa * 2^(Order - 47)
            double val = (double)mantissa * Math.Pow(2, order - 47);

            return signBit == 1 ? -val : val;
        }

        /// <summary>
        /// Создает Word48 из double.
        /// </summary>
        public static Word48 FromDouble(double value)
        {
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value)) return new Word48(0);

            long sign = value < 0 ? 1L : 0L;
            double absValue = Math.Abs(value);

            // Поиск порядка: 2^39 <= M < 2^40
            // Order = floor(log2(Value)) + 47 - 39 = floor(log2(Value)) + 8
            int log2Val = (int)Math.Floor(Math.Log2(absValue));
            long order = (long)log2Val + 8;

            // Ограничение порядка [0, 63]
            if (order < 0) order = 0;
            if (order > 63) order = 63;

            // Вычисляем мантиссу
            double mDouble = absValue / Math.Pow(2, order - 47);
            long mantissa = (long)Math.Round(mDouble);

            // Итеративная корректировка для точного попадания в диапазон [2^39, 2^40)
            while (mantissa < (1L << 39) && order > 0)
            {
                order--;
                mDouble = absValue / Math.Pow(2, order - 47);
                mantissa = (long)Math.Round(mDouble);
            }
            while (mantissa >= (1L << 40) && order < 63)
            {
                order++;
                mDouble = absValue / Math.Pow(2, order - 47);
                mantissa = (long)Math.Round(mDouble);
            }

            if (mantissa >= (1L << 40)) mantissa = (1L << 40) - 1;
            if (mantissa < 0) mantissa = 0;

            long result = (sign << 47) | (order << 41) | (mantissa << 1);
            return new Word48(result);
        }

        #endregion

        #region Операции и сравнение

        public bool Equals(Word48 other) => _value == other._value;
        public override bool Equals(object obj) => obj is Word48 other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public int CompareTo(Word48 other) => _value.CompareTo(other._value);
        public override string ToString() => $"0x{_value:X12}";

        /// <summary>
        /// Преобразует 48-битное слово в восьмеричную строку (16 символов).
        /// </summary>
        public string ToOctal()
        {
            long val = _value;
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
            if (oct.Length > 16)
                oct = oct.Substring(0, 16);
            long val = 0;
            foreach (char c in oct)
            {
                val = (val << 3) | (long)(c - '0');
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
