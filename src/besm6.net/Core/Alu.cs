using System;

namespace Besm6.Core
{
    /// <summary>
    /// Арифметическое логическое устройство БЭСМ-6.
    /// Вынесено из Processor.cs (Этап 2 рефакторинга).
    /// Работает через ссылки на внутренние поля процессора.
    /// </summary>
    public class Alu
    {
        private const ulong R_NORM_DISABLE  = (ulong)RFlags.NormDisable;
        private const ulong R_ROUND_DISABLE = (ulong)RFlags.RoundDisable;
        private const ulong R_OVF_DISABLE   = (ulong)RFlags.OvfDisable;

        private const ulong BIT41  = Besm6Constants.BIT41;
        private const ulong BIT48  = Besm6Constants.BIT48;
        private const ulong BITS40 = Besm6Constants.BITS40;
        private const ulong BITS41 = Besm6Constants.BITS41;
        private const ulong BITS42 = Besm6Constants.BITS42;
        private const ulong BITS48 = Besm6Constants.BITS48;

        private readonly Processor _proc;

        public Alu(Processor proc)
        {
            _proc = proc;
        }

        public void Add(Word48 val, bool negateA, bool negateVal)
        {
            MantissaExponent a = new MantissaExponent(new Word48(_proc._a.Value));
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

            NormalizeAndRound(a, y.Value, roundFlag);
        }

        public void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag)
        {
            ulong rr = 0;
            ulong normalizedBits;
            ulong r = _proc._r;

            if ((r & R_NORM_DISABLE) != 0)
                goto chk_rnd;

            int i = (int)((a.Mantissa >> 39) & 3);
            if (i == 0)
            {
                normalizedBits = (ulong)a.Mantissa & BITS40;
                if (normalizedBits != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit((long)normalizedBits);
                    normalizedBits <<= cnt;
                    rr = y >> (40 - cnt);
                    a.Mantissa = (long)(normalizedBits | rr);
                    y <<= cnt;
                    a.Exponent -= (uint)cnt;
                    goto chk_zero;
                }
                normalizedBits = y & BITS40;
                if (normalizedBits != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit((long)normalizedBits);
                    rr = y;
                    normalizedBits <<= cnt;
                    a.Mantissa = (long)normalizedBits;
                    y = 0;
                    a.Exponent -= 40u + (uint)cnt;
                    goto chk_zero;
                }
                goto zero;
            }
            else if (i == 3)
            {
                normalizedBits = ~(ulong)a.Mantissa & BITS40;
                if (normalizedBits != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit((long)normalizedBits);
                    normalizedBits = (normalizedBits << cnt) | ((1UL << cnt) - 1);
                    rr = y >> (40 - cnt);
                    a.Mantissa = (long)(BIT41 | (~normalizedBits & BITS40) | rr);
                    y <<= cnt;
                    a.Exponent -= (uint)cnt;
                    goto chk_zero;
                }
                normalizedBits = ~y & BITS40;
                if (normalizedBits != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit((long)normalizedBits);
                    rr = y;
                    normalizedBits = (normalizedBits << cnt) | ((1UL << cnt) - 1);
                    a.Mantissa = (long)(BIT41 | (~normalizedBits & BITS40));
                    y = 0;
                    a.Exponent -= 40u + (uint)cnt;
                    goto chk_zero;
                }
                else
                {
                    rr = 1;
                    a.Mantissa = (long)BIT41;
                    y = 0;
                    a.Exponent -= 80;
                    goto chk_zero;
                }
            }

        chk_zero:
            if (rr != 0)
                roundFlag = false;

        chk_rnd:
            if ((a.Exponent & 0x8000u) != 0)
                goto zero;

            if ((r & R_ROUND_DISABLE) == 0 && roundFlag)
                a.Mantissa |= 1;

            if (a.Mantissa == 0 && (r & R_NORM_DISABLE) == 0)
                goto zero;

            _proc._a = Word48.FromInt48((((ulong)a.Exponent & 0x7Fu) << 41) | ((ulong)a.Mantissa & BITS41));
            _proc._y = Word48.FromInt48(y & BITS40);

            if ((a.Exponent & 0x80u) != 0)
            {
                if ((r & R_OVF_DISABLE) == 0)
                    throw new ProcessorException("Arithmetic overflow");
            }
            return;

        zero:
            _proc._a = Word48.Zero;
            _proc._y = Word48.FromInt48(_proc._y.Value & ~BITS40);
        }

        public void AddExponent(int val)
        {
            MantissaExponent a = new MantissaExponent(_proc._a);
            a.Exponent += (uint)val;
            _proc._y = Word48.Zero;
            NormalizeAndRound(a, 0, false);
        }

        public void ChangeSign(bool negateA)
        {
            MantissaExponent a = new MantissaExponent(_proc._a);
            if (negateA)
            {
                a.Negate();
                if (a.IsDenormal())
                    a.NormalizeToTheRight();
            }
            _proc._y = Word48.Zero;
            NormalizeAndRound(a, 0, false);
        }

        public void Multiply(Word48 val)
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

            NormalizeAndRound(a, y, y != 0);
        }

        public void Divide(Word48 val)
        {
            if (((val.Value ^ (val.Value << 1)) & BIT41) == 0)
                throw new ProcessorException("Division by zero");

            MantissaExponent dividend = new MantissaExponent(_proc._a);
            MantissaExponent divisor = new MantissaExponent(val);

            MantissaExponent a = NrDiv(dividend, divisor);
            NormalizeAndRound(a, 0, false);
        }

        public void Shift(int nbits)
        {
            _proc._y = Word48.Zero;
            if (nbits > 0)
            {
                if (nbits < 48)
                {
                    _proc._y = Word48.FromInt48( (_proc._a.Value << (48 - nbits)) & BITS48);
                    _proc._a = Word48.FromInt48(_proc._a.Value >> nbits);
                }
                else
                {
                    _proc._y = Word48.FromInt48(_proc._a.Value >> (nbits - 48));
                    _proc._a = Word48.Zero;
                }
            }
            else if (nbits < 0)
            {
                int n = -nbits;
                if (n < 48)
                {
                    _proc._y = Word48.FromInt48(_proc._a.Value >> (48 - n));
                    _proc._a = Word48.FromInt48((_proc._a.Value << n) & BITS48);
                }
                else
                {
                    _proc._y = Word48.FromInt48((_proc._a.Value << (n - 48)) & BITS48);
                    _proc._a = Word48.Zero;
                }
            }
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
