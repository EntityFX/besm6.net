using System;

namespace Besm6.Core
{
    /// <summary>
    /// Мультипликативные операции АЛУ: умножение и деление. Вынесено из Alu.cs
    /// (Этап 4 рефакторинга).
    /// </summary>
    internal sealed class MultiplicativeOperations
    {
        private const ulong BIT41  = ArchitectureConstants.BIT41;
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS48 = ArchitectureConstants.BITS48;

        private readonly ProcessorState _state;
        private readonly NormalizationAndRounding _normalizer;

        internal MultiplicativeOperations(ProcessorState state, NormalizationAndRounding normalizer)
        {
            _state = state;
            _normalizer = normalizer;
        }

        /// <summary>Умножение аккумулятора A на операнд.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal void Multiply(Word48 val)
        {
            if (_state.A.Value == 0 || val.Value == 0)
            {
                _state.A = Word48.Zero;
                _state.Y = Word48.FromInt48(_state.Y.Value & ~BITS40);
                return;
            }

            MantissaExponent a = new MantissaExponent(_state.A);
            MantissaExponent word = new MantissaExponent(val);

            ulong y = (ulong)a.Multiply(word.Mantissa);
            a.Exponent += word.Exponent - 64;

            if (a.IsDenormal())
                a.NormalizeToTheRight();

            _normalizer.NormalizeAndRound(a, y, y != 0);
        }

        /// <summary>Деление аккумулятора A на операнд; деление на ноль выбрасывает исключение.</summary>
        internal void Divide(Word48 val)
        {
            if (((val.Value ^ (val.Value << 1)) & BIT41) == 0)
                throw new ProcessorException("Division by zero");

            MantissaExponent dividend = new MantissaExponent(_state.A);
            MantissaExponent divisor = new MantissaExponent(val);

            MantissaExponent a = NrDiv(dividend, divisor);
            _normalizer.NormalizeAndRound(a, 0, false);
        }

        internal static MantissaExponent NrDiv(MantissaExponent n, MantissaExponent d)
        {
            MantissaExponent quot = new MantissaExponent(0, 0);

            if (d.Mantissa == MantissaExponent.BIT40)
            {
                quot.Mantissa = n.Mantissa;
                quot.Exponent = n.Exponent - d.Exponent + 64 + 1;
                return quot;
            }

            n.Mantissa <<= 1;
            d.Mantissa <<= 1;

            if (Math.Abs(n.Mantissa) >= Math.Abs(d.Mantissa))
                n.NormalizeToTheRight();

            quot.Exponent = n.Exponent - d.Exponent + 64;

            long remainder = n.Mantissa;
            long divisor = d.Mantissa;
            long quotient = 0;
            for (long bitmask = MantissaExponent.BIT40; bitmask > 0; bitmask >>= 1)
            {
                if (remainder == 0)
                    break;

                // The unsigned range excludes both +/- BIT40 without computing
                // an absolute value on every step of non-restoring division.
                if ((ulong)(remainder + MantissaExponent.BIT40 - 1) <
                    (ulong)(2 * MantissaExponent.BIT40 - 1))
                {
                    remainder *= 2;
                }
                else if ((remainder ^ divisor) >= 0)
                {
                    quotient += bitmask;
                    remainder = remainder * 2 - divisor;
                }
                else
                {
                    quotient -= bitmask;
                    remainder = remainder * 2 + divisor;
                }
            }
            quot.Mantissa = quotient;
            return quot;
        }
    }
}
