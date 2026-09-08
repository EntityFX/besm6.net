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

        private readonly Processor _proc;
        private readonly NormalizationAndRounding _normalizer;

        internal MultiplicativeOperations(Processor proc, NormalizationAndRounding normalizer)
        {
            _proc = proc;
            _normalizer = normalizer;
        }

        /// <summary>Умножение аккумулятора A на операнд.</summary>
        internal void Multiply(Word48 val)
        {
            if (_proc._a.Value == 0 || val.Value == 0)
            {
                _proc._a = Word48.Zero;
                _proc._y = Word48.FromInt48(_proc._y.Value & ~BITS40);
                return;
            }

            MantissaExponent a = new MantissaExponent(_proc._a);
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

            MantissaExponent dividend = new MantissaExponent(_proc._a);
            MantissaExponent divisor = new MantissaExponent(val);

            MantissaExponent a = NrDiv(dividend, divisor);
            _normalizer.NormalizeAndRound(a, 0, false);
        }

        private MantissaExponent NrDiv(MantissaExponent n, MantissaExponent d)
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

            quot.Mantissa = 0;
            for (long bitmask = MantissaExponent.BIT40; bitmask > 0; bitmask >>= 1)
            {
                if (n.Mantissa == 0)
                    break;

                if (Math.Abs(n.Mantissa) < MantissaExponent.BIT40)
                {
                    n.Mantissa *= 2;
                }
                else if ((n.Mantissa > 0) == (d.Mantissa > 0))
                {
                    quot.Mantissa += bitmask;
                    n.Mantissa *= 2;
                    n.Mantissa -= d.Mantissa;
                }
                else
                {
                    quot.Mantissa -= bitmask;
                    n.Mantissa *= 2;
                    n.Mantissa += d.Mantissa;
                }
            }
            return quot;
        }
    }
}