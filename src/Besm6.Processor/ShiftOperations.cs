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

        private readonly ProcessorState _state;

        internal ShiftOperations(ProcessorState state)
        {
            _state = state;
        }

        /// <summary>Выполняет сдвиг A/Y на nbits бит (положительное — вправо).</summary>
        internal void Shift(int nbits)
        {
            _state.Y = Word48.Zero;
            if (nbits > 0)
            {
                if (nbits < 48)
                {
                    _state.Y = Word48.FromInt48((_state.A.Value << (48 - nbits)) & BITS48);
                    _state.A = Word48.FromInt48(_state.A.Value >> nbits);
                }
                else
                {
                    _state.Y = Word48.FromInt48(_state.A.Value >> (nbits - 48));
                    _state.A = Word48.Zero;
                }
            }
            else if (nbits < 0)
            {
                int n = -nbits;
                if (n < 48)
                {
                    _state.Y = Word48.FromInt48(_state.A.Value >> (48 - n));
                    _state.A = Word48.FromInt48((_state.A.Value << n) & BITS48);
                }
                else
                {
                    _state.Y = Word48.FromInt48((_state.A.Value << (n - 48)) & BITS48);
                    _state.A = Word48.Zero;
                }
            }
        }
    }
}
