using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine
{
    public interface IAlu
    {

        /// <summary>Сложение/вычитание операнда с аккумулятором A.</summary>
        public void Add(Word48 val, bool negateA, bool negateVal);

        /// <summary>Нормализация и округление результата.</summary>
        public void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag);

        /// <summary>Прибавление к экспоненте A.</summary>
        public void AddExponent(int val);

        /// <summary>Изменение знака аккумулятора A.</summary>
        public void ChangeSign(bool negateA);

        /// <summary>Умножение аккумулятора A на операнд.</summary>
        public void Multiply(Word48 val);

        /// <summary>Деление аккумулятора A на операнд.</summary>
        public void Divide(Word48 val);

        /// <summary>Сдвиг регистров A/Y.</summary>
        public void Shift(int nbits);

        public void PublishPreparedResult(NormalizedArithmeticResult result);
    }
}