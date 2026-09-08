using System;

namespace Besm6.Core
{
    /// <summary>
    /// Нормализация и округление результата АЛУ (порт dubna). Вынесено из Alu.cs
    /// (Этап 4 рефакторинга). Записывает результат в регистры A/Y владельца.
    /// </summary>
    internal sealed class NormalizationAndRounding
    {
        private const ulong R_NORM_DISABLE  = (ulong)RFlags.NormDisable;
        private const ulong R_ROUND_DISABLE = (ulong)RFlags.RoundDisable;
        private const ulong R_OVF_DISABLE   = (ulong)RFlags.OvfDisable;

        private const ulong BIT41  = ArchitectureConstants.BIT41;
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS41 = ArchitectureConstants.BITS41;

        private readonly ProcessorState _state;

        internal NormalizationAndRounding(ProcessorState state)
        {
            _state = state;
        }

        /// <summary>Нормализует мантиссу, применяет округление и записывает A/Y.</summary>
        internal void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag)
        {
            ulong rr = 0;
            ulong normalizedBits;
            ulong r = _state.R;

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

            _state.A = Word48.FromInt48((((ulong)a.Exponent & 0x7Fu) << 41) | ((ulong)a.Mantissa & BITS41));
            _state.Y = Word48.FromInt48(y & BITS40);

            if ((a.Exponent & 0x80u) != 0)
            {
                if ((r & R_OVF_DISABLE) == 0)
                    throw new ProcessorException("Arithmetic overflow");
            }
            return;

        zero:
            _state.A = Word48.Zero;
            _state.Y = Word48.FromInt48(_state.Y.Value & ~BITS40);
        }
    }
}
