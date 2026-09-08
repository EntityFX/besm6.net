using System;

namespace Besm6.Core
{
    /// <summary>
    /// Аддитивные операции АЛУ: сложение/вычитание, изменение экспоненты и знака.
    /// Вынесено из Alu.cs (Этап 4 рефакторинга).
    /// </summary>
    internal sealed class AdditiveOperations
    {
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS42 = ArchitectureConstants.BITS42;

        private readonly ProcessorState _state;
        private readonly NormalizationAndRounding _normalizer;

        internal AdditiveOperations(ProcessorState state, NormalizationAndRounding normalizer)
        {
            _state = state;
            _normalizer = normalizer;
        }

        /// <summary>Сложение/вычитание операнда с аккумулятором A (регистры АЛУ A/Y).</summary>
        internal void Add(Word48 val, bool negateA, bool negateVal)
        {
            MantissaExponent a = new MantissaExponent(new Word48(_state.A.Value));
            MantissaExponent word = new MantissaExponent(val);

            if (!negateA)
            {
                if (!negateVal) { /* сложение */ }
                else { word.Negate(); }
            }
            else
            {
                if (!negateVal) { a.Negate(); }
                else
                {
                    if (a.IsNegative()) a.Negate();
                    if (!word.IsNegative()) word.Negate();
                }
            }

            MantissaExponent a1, a2;
            int diff = (int)(a.Exponent - word.Exponent);
            if (diff < 0)
            {
                diff = -diff;
                a1 = a;
                a2 = word;
            }
            else
            {
                a1 = word;
                a2 = a;
            }

            Word48 y = Word48.Zero;
            bool neg = a1.IsNegative();
            bool roundFlag = false;

            if (diff == 0)
            {
            }
            else if (diff <= 40)
            {
                y = Word48.FromInt48((ulong)(a1.Mantissa << (40 - diff)) & BITS40);
                roundFlag = y.Value != 0;
                a1.Mantissa = (long)((ulong)((a1.Mantissa >> diff) | (neg ? (~0L << (40 - diff)) : 0)) & BITS42);
            }
            else if (diff <= 80)
            {
                int d2 = diff - 40;
                roundFlag = a1.Mantissa != 0;
                y = Word48.FromInt48((ulong)((a1.Mantissa >> d2) | (neg ? (~0L << (40 - d2)) : 0)) & BITS40);
                a1.Mantissa = neg ? (long)BITS42 : 0;
            }
            else
            {
                roundFlag = a1.Mantissa != 0;
                if (neg)
                {
                    y = Word48.FromInt48(BITS40);
                    a1.Mantissa = (long)BITS42;
                }
                else
                {
                    y = Word48.Zero;
                    a1.Mantissa = 0;
                }
            }

            a.Exponent = a2.Exponent;
            a.Mantissa = a1.Mantissa + a2.Mantissa;

            if (a.IsDenormal())
            {
                roundFlag |= (a.Mantissa & 1) != 0;
                y = Word48.FromInt48((y.Value >> 1) | ((ulong)(a.Mantissa & 1) << 39));
                a.NormalizeToTheRight();
            }

            _normalizer.NormalizeAndRound(a, y.Value, roundFlag);
        }

        /// <summary>Прибавляет к экспоненте A заданное значение (Э50: добавление к порядку).</summary>
        internal void AddExponent(int val)
        {
            MantissaExponent a = new MantissaExponent(_state.A);
            a.Exponent += (uint)val;
            _state.Y = Word48.Zero;
            _normalizer.NormalizeAndRound(a, 0, false);
        }

        /// <summary>Изменение знака аккумулятора A (прямое или через операнд).</summary>
        internal void ChangeSign(bool negateA)
        {
            MantissaExponent a = new MantissaExponent(_state.A);
            if (negateA)
            {
                a.Negate();
                if (a.IsDenormal())
                    a.NormalizeToTheRight();
            }
            _state.Y = Word48.Zero;
            _normalizer.NormalizeAndRound(a, 0, false);
        }
    }
}
