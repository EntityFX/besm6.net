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
        // Preserve the original dispatcher call boundary despite the small wrapper.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Add(Word48 val, bool negateA, bool negateVal)
        {
            var sink = new ImmediateArithmeticResultSink(_normalizer);
            ComputeAdd(_state.A, val, negateA, negateVal, ref sink);
        }

        /// <summary>Shared arithmetic calculation; no architectural state is changed.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult EvaluateAdd(Word48 accumulator, Word48 previousY,
            uint mode, Word48 val, bool negateA, bool negateVal)
        {
            var sink = new PreparedArithmeticResultSink(mode, previousY);
            ComputeAdd(accumulator, val, negateA, negateVal, ref sink);
            return sink.Result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ComputeAdd<TSink>(Word48 accumulator, Word48 val,
            bool negateA, bool negateVal, ref TSink sink) where TSink : struct, IArithmeticResultSink
        {
            MantissaExponent a = new MantissaExponent(accumulator);
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

            sink.Normalize(a, y.Value, roundFlag);
        }

        /// <summary>Прибавляет к экспоненте A заданное значение (Э50: добавление к порядку).</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void AddExponent(int val)
        {
            var sink = new ImmediateArithmeticResultSink(_normalizer);
            ComputeAddExponent(_state.A, val, ref sink);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult EvaluateAddExponent(Word48 accumulator, uint mode, int val)
        {
            var sink = new PreparedArithmeticResultSink(mode, Word48.Zero);
            ComputeAddExponent(accumulator, val, ref sink);
            return sink.Result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ComputeAddExponent<TSink>(Word48 accumulator, int val, ref TSink sink)
            where TSink : struct, IArithmeticResultSink
        {
            MantissaExponent a = new MantissaExponent(accumulator);
            a.Exponent += (uint)val;
            sink.ClearLowRegister();
            sink.Normalize(a, 0, false);
        }

        /// <summary>Изменение знака аккумулятора A (прямое или через операнд).</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void ChangeSign(bool negateA)
        {
            var sink = new ImmediateArithmeticResultSink(_normalizer);
            ComputeChangeSign(_state.A, negateA, ref sink);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult EvaluateChangeSign(Word48 accumulator, uint mode, bool negateA)
        {
            var sink = new PreparedArithmeticResultSink(mode, Word48.Zero);
            ComputeChangeSign(accumulator, negateA, ref sink);
            return sink.Result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ComputeChangeSign<TSink>(Word48 accumulator, bool negateA, ref TSink sink)
            where TSink : struct, IArithmeticResultSink
        {
            MantissaExponent a = new MantissaExponent(accumulator);
            if (negateA)
            {
                a.Negate();
                if (a.IsDenormal())
                    a.NormalizeToTheRight();
            }
            sink.ClearLowRegister();
            sink.Normalize(a, 0, false);
        }
    }
}
