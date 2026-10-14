using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Cpu;
using System;

namespace Besm6.Processor.Arithmetic
{
    /// <summary>
    /// Мультипликативные операции АЛУ: умножение и деление.
    /// </summary>
    internal sealed class MultiplicativeOperations
    {
        private readonly IProcessorState _state;
        private readonly NormalizationAndRounding _normalizer;

        internal MultiplicativeOperations(IProcessorState state, NormalizationAndRounding normalizer)
        {
            _state = state;
            _normalizer = normalizer;
        }

        /// <summary>Умножение аккумулятора A на операнд.</summary>
        // Preserve the original dispatcher call boundary despite the small wrapper.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Multiply(Word48 val)
        {
            var sink = new ImmediateArithmeticResultSink(_normalizer);
            ComputeMultiply(_state.A, val, ref sink);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult EvaluateMultiply(Word48 accumulator, Word48 previousY,
            uint mode, Word48 val)
        {
            var sink = new PreparedArithmeticResultSink(mode, previousY);
            ComputeMultiply(accumulator, val, ref sink);
            return sink.Result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization |
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ComputeMultiply<TSink>(Word48 accumulator, Word48 val, ref TSink sink)
            where TSink : struct, IArithmeticResultSink
        {
            if (accumulator.Value == 0 || val.Value == 0)
            {
                sink.Zero();
                return;
            }

            MantissaExponent a = new MantissaExponent(accumulator);
            MantissaExponent word = new MantissaExponent(val);

            ulong y = (ulong)a.Multiply(word.Mantissa);
            a.Exponent += word.Exponent - 64;

            if (a.IsDenormal())
                a.NormalizeToTheRight();

            sink.Normalize(a, y, y != 0);
        }

        /// <summary>Деление аккумулятора A на операнд; деление на ноль выбрасывает исключение.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Divide(Word48 val)
        {
            var sink = new ImmediateArithmeticResultSink(_normalizer);
            ComputeDivide(_state.A, val, ref sink);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static NormalizedArithmeticResult EvaluateDivide(Word48 accumulator, Word48 previousY,
            uint mode, Word48 val)
        {
            var sink = new PreparedArithmeticResultSink(mode, previousY);
            ComputeDivide(accumulator, val, ref sink);
            return sink.Result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ComputeDivide<TSink>(Word48 accumulator, Word48 val, ref TSink sink)
            where TSink : struct, IArithmeticResultSink
        {
            if (((val.Value ^ (val.Value << 1)) & ArchitectureConstants.BIT41) == 0)
                throw new ProcessorException("Division by zero");

            MantissaExponent dividend = new MantissaExponent(accumulator);
            MantissaExponent divisor = new MantissaExponent(val);

            MantissaExponent a = NrDiv(dividend, divisor);
            sink.Normalize(a, 0, false);
        }

        internal static MantissaExponent NrDiv(MantissaExponent n, MantissaExponent d)
        {
            MantissaExponent quot = new MantissaExponent(0, 0);

            if (d.Mantissa == ArchitectureConstants.BIT40)
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
            for (long bitmask = ArchitectureConstants.BIT40; bitmask > 0; bitmask >>= 1)
            {
                if (remainder == 0)
                    break;

                // The unsigned range excludes both +/- BIT40 without computing
                // an absolute value on every step of non-restoring division.
                if ((ulong)(remainder + ArchitectureConstants.BIT40 - 1) <
                    (ulong)(2 * ArchitectureConstants.BIT40 - 1))
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
