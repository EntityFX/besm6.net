using System;

namespace Besm6.Core
{
    /// <summary>
    /// Арифметическое логическое устройство БЭСМ-6 (публичный фасад).
    /// Делегирует специализированным подсистемам: аддитивные, мультипликативные,
    /// сдвиговые операции и нормализация/округление (Этап 4 рефакторинга).
    /// </summary>
    public class Alu
    {
        private readonly NormalizationAndRounding _normalizer;
        private readonly AdditiveOperations _additive;
        private readonly MultiplicativeOperations _multiplicative;
        private readonly ShiftOperations _shift;

        public Alu(Processor proc)
            : this(proc.State)
        {
        }

        internal Alu(ProcessorState state)
        {
            _normalizer = new NormalizationAndRounding(state);
            _additive = new AdditiveOperations(state, _normalizer);
            _multiplicative = new MultiplicativeOperations(state, _normalizer);
            _shift = new ShiftOperations(state);
        }

        /// <summary>Сложение/вычитание операнда с аккумулятором A.</summary>
        public void Add(Word48 val, bool negateA, bool negateVal) => _additive.Add(val, negateA, negateVal);

        /// <summary>Нормализация и округление результата.</summary>
        public void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag)
            => _normalizer.NormalizeAndRound(a, y, roundFlag);

        /// <summary>Прибавление к экспоненте A.</summary>
        public void AddExponent(int val) => _additive.AddExponent(val);

        /// <summary>Изменение знака аккумулятора A.</summary>
        public void ChangeSign(bool negateA) => _additive.ChangeSign(negateA);

        /// <summary>Умножение аккумулятора A на операнд.</summary>
        public void Multiply(Word48 val) => _multiplicative.Multiply(val);

        /// <summary>Деление аккумулятора A на операнд.</summary>
        public void Divide(Word48 val) => _multiplicative.Divide(val);

        /// <summary>Сдвиг регистров A/Y.</summary>
        public void Shift(int nbits) => _shift.Shift(nbits);
    }
}
