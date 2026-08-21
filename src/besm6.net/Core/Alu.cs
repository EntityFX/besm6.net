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
        private const long RAU_NORM_DISABLE  = (long)RauFlags.NormDisable;
        private const long RAU_ROUND_DISABLE = (long)RauFlags.RoundDisable;
        private const long RAU_OVF_DISABLE   = (long)RauFlags.OvfDisable;

        private const long BIT41  = Besm6Constants.BIT41;
        private const long BIT48  = Besm6Constants.BIT48;
        private const long BITS40 = Besm6Constants.BITS40;
        private const long BITS41 = Besm6Constants.BITS41;
        private const long BITS42 = Besm6Constants.BITS42;
        private const long BITS48 = Besm6Constants.BITS48;

        private readonly Processor _proc;

        public Alu(Processor proc)
        {
            _proc = proc;
        }

        public void Add(long val, bool negateAcc, bool negateVal)
        {
            MantissaExponent acc = new MantissaExponent(new Word48(_proc._acc));
            MantissaExponent word = new MantissaExponent(new Word48(val));

            if (!negateAcc)
            {
                if (!negateVal) { /* сложение */ }
                else { word.Negate(); }
            }
            else
            {
                if (!negateVal) { acc.Negate(); }
                else
                {
                    if (acc.IsNegative()) acc.Negate();
                    if (!word.IsNegative()) word.Negate();
                }
            }

            MantissaExponent a1, a2;
            int diff = acc.Exponent - word.Exponent;
            if (diff < 0)
            {
                diff = -diff;
                a1 = acc;
                a2 = word;
            }
            else
            {
                a1 = word;
                a2 = acc;
            }

            long mr = 0;
            bool neg = a1.IsNegative();
            bool roundFlag = false;

            if (diff == 0)
            {
            }
            else if (diff <= 40)
            {
                mr = (a1.Mantissa << (40 - diff)) & BITS40;
                roundFlag = mr != 0;
                a1.Mantissa = ((a1.Mantissa >> diff) | (neg ? (~0L << (40 - diff)) : 0)) & BITS42;
            }
            else if (diff <= 80)
            {
                int d2 = diff - 40;
                roundFlag = a1.Mantissa != 0;
                mr = ((a1.Mantissa >> d2) | (neg ? (~0L << (40 - d2)) : 0)) & BITS40;
                a1.Mantissa = neg ? BITS42 : 0;
            }
            else
            {
                roundFlag = a1.Mantissa != 0;
                if (neg)
                {
                    mr = BITS40;
                    a1.Mantissa = BITS42;
                }
                else
                {
                    mr = a1.Mantissa = 0;
                }
            }

            acc.Exponent = a2.Exponent;
            acc.Mantissa = a1.Mantissa + a2.Mantissa;

            if (acc.IsDenormal())
            {
                roundFlag |= (acc.Mantissa & 1) != 0;
                mr = (mr >> 1) | ((acc.Mantissa & 1) << 39);
                acc.NormalizeToTheRight();
            }

            NormalizeAndRound(acc, mr, roundFlag);
        }

        public void NormalizeAndRound(MantissaExponent acc, long mr, bool roundFlag)
        {
            long rr = 0;
            long r;
            long rau = _proc._rau;

            if ((rau & RAU_NORM_DISABLE) != 0)
                goto chk_rnd;

            int i = (int)((acc.Mantissa >> 39) & 3);
            if (i == 0)
            {
                r = acc.Mantissa & BITS40;
                if (r != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit(r);
                    r <<= cnt;
                    rr = mr >> (40 - cnt);
                    acc.Mantissa = r | rr;
                    mr <<= cnt;
                    acc.Exponent -= cnt;
                    goto chk_zero;
                }
                r = mr & BITS40;
                if (r != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit(r);
                    rr = mr;
                    r <<= cnt;
                    acc.Mantissa = r;
                    mr = 0;
                    acc.Exponent -= 40 + cnt;
                    goto chk_zero;
                }
                goto zero;
            }
            else if (i == 3)
            {
                r = ~acc.Mantissa & BITS40;
                if (r != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit(r);
                    r = (r << cnt) | ((1L << cnt) - 1);
                    rr = mr >> (40 - cnt);
                    acc.Mantissa = BIT41 | (~r & BITS40) | rr;
                    mr <<= cnt;
                    acc.Exponent -= cnt;
                    goto chk_zero;
                }
                r = ~mr & BITS40;
                if (r != 0)
                {
                    int cnt = 39 - MantissaExponent.HighestBit(r);
                    rr = mr;
                    r = (r << cnt) | ((1L << cnt) - 1);
                    acc.Mantissa = BIT41 | (~r & BITS40);
                    mr = 0;
                    acc.Exponent -= 40 + cnt;
                    goto chk_zero;
                }
                else
                {
                    rr = 1;
                    acc.Mantissa = BIT41;
                    mr = 0;
                    acc.Exponent -= 80;
                    goto chk_zero;
                }
            }

        chk_zero:
            if (rr != 0)
                roundFlag = false;

        chk_rnd:
            if ((acc.Exponent & 0x8000) != 0)
                goto zero;

            if ((rau & RAU_ROUND_DISABLE) == 0 && roundFlag)
                acc.Mantissa |= 1;

            if (acc.Mantissa == 0 && (rau & RAU_NORM_DISABLE) == 0)
                goto zero;

            _proc._acc = ((long)(acc.Exponent & 0x7F) << 41) | (acc.Mantissa & BITS41);
            _proc._rmr = mr & BITS40;

            if ((acc.Exponent & 0x80) != 0)
            {
                if ((rau & RAU_OVF_DISABLE) == 0)
                    throw new ProcessorException("Arithmetic overflow");
            }
            return;

        zero:
            _proc._acc = 0;
            _proc._rmr &= ~BITS40;
        }

        public void AddExponent(int val)
        {
            MantissaExponent acc = new MantissaExponent(new Word48(_proc._acc));
            acc.Exponent += val;
            _proc._rmr = 0;
            NormalizeAndRound(acc, 0, false);
        }

        public void ChangeSign(bool negateAcc)
        {
            MantissaExponent acc = new MantissaExponent(new Word48(_proc._acc));
            if (negateAcc)
            {
                acc.Negate();
                if (acc.IsDenormal())
                    acc.NormalizeToTheRight();
            }
            _proc._rmr = 0;
            NormalizeAndRound(acc, 0, false);
        }

        public void Multiply(long val)
        {
            if (_proc._acc == 0 || val == 0)
            {
                _proc._acc = 0;
                _proc._rmr &= ~BITS40;
                return;
            }

            MantissaExponent acc = new MantissaExponent(new Word48(_proc._acc));
            MantissaExponent word = new MantissaExponent(new Word48(val));

            long mr = acc.Multiply(word.Mantissa);
            acc.Exponent += word.Exponent - 64;

            if (acc.IsDenormal())
                acc.NormalizeToTheRight();

            NormalizeAndRound(acc, mr, mr != 0);
        }

        public void Divide(long val)
        {
            if (((val ^ (val << 1)) & BIT41) == 0)
                throw new ProcessorException("Division by zero");

            MantissaExponent dividend = new MantissaExponent(new Word48(_proc._acc));
            MantissaExponent divisor = new MantissaExponent(new Word48(val));

            MantissaExponent acc = NrDiv(dividend, divisor);
            NormalizeAndRound(acc, 0, false);
        }

        public void Shift(int nbits)
        {
            _proc._rmr = 0;
            if (nbits > 0)
            {
                if (nbits < 48)
                {
                    _proc._rmr = (_proc._acc << (48 - nbits)) & BITS48;
                    _proc._acc = (long)((ulong)_proc._acc >> nbits);
                }
                else
                {
                    _proc._rmr = (long)((ulong)_proc._acc >> (nbits - 48));
                    _proc._acc = 0;
                }
            }
            else if (nbits < 0)
            {
                int n = -nbits;
                if (n < 48)
                {
                    _proc._rmr = (long)((ulong)_proc._acc >> (48 - n));
                    _proc._acc = (_proc._acc << n) & BITS48;
                }
                else
                {
                    _proc._rmr = (_proc._acc << (n - 48)) & BITS48;
                    _proc._acc = 0;
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