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
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag)
        {
            var result = Evaluate(a, y, roundFlag, _state.R, _state.Y);
            _state.A = result.A;
            _state.Y = result.Y;
            if (result.Overflow)
                throw new ProcessorException("Arithmetic overflow");
        }

        /// <summary>
        /// Computes the same result without changing processor registers. The
        /// caller owns publication and fault delivery; no hardware time is inferred.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult Evaluate(MantissaExponent a, ulong y,
            bool roundFlag, uint r, Word48 previousY)
        {
            ulong rr = 0;
            ulong normalizedBits;

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

            bool roundOnOutput = (r & R_ROUND_DISABLE) == 0 && roundFlag;
            long unroundedMantissa = a.Mantissa;
            if (roundOnOutput)
                a.Mantissa |= 1;

            if (a.Mantissa == 0 && (r & R_NORM_DISABLE) == 0)
                goto zero;

            return new(
                Word48.FromInt48((((ulong)a.Exponent & 0x7Fu) << 41) | ((ulong)unroundedMantissa & BITS41)),
                Word48.FromInt48(y & BITS40), roundOnOutput,
                (a.Exponent & 0x80u) != 0 && (r & R_OVF_DISABLE) == 0);

        zero:
            return new(Word48.Zero, Word48.FromInt48(previousY.Value & ~BITS40), false, false);
        }
    }
}
