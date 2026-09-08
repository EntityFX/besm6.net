using System;

namespace Besm6.Core
{
    /// <summary>
    /// Сдвиговые операции АЛУ: арифметический/логический сдвиг регистров A/Y.
    /// Вынесено из Alu.cs (Этап 4 рефакторинга).
    /// </summary>
    internal sealed class ShiftOperations
    {
        private const ulong BITS48 = ArchitectureConstants.BITS48;

        private readonly Processor _proc;

        internal ShiftOperations(Processor proc)
        {
            _proc = proc;
        }

        /// <summary>Выполняет сдвиг A/Y на nbits бит (положительное — вправо).</summary>
        internal void Shift(int nbits)
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
    }
}