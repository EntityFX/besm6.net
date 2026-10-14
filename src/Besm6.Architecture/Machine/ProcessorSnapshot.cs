using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine
{
    /// <summary>
    /// Неизменяемый снимок регистров процессора до или после инструкции.
    /// </summary>
    public sealed class ProcessorSnapshot : IRegisters
    {
        public ProcessorSnapshot(IProcessorState state)
        {
            K = state.K;
            IsRightHalf = state.IsRightHalf;
            A = state.A;
            Y = state.Y;
            R = state.R;
            C = state.C;
            ApplyC = state.ApplyC;
            EffectiveAddress = state.EffectiveAddress;
            InterceptCount = state.InterceptCount;
            InterceptAddress = state.InterceptAddress;
            M = (uint[])state.M.Clone();
        }

        /// <summary>Счётчик команд K.</summary>
        public uint K { get; set; }

        /// <summary>Признак правого полуслова.</summary>
        public bool IsRightHalf { get; }

        /// <summary>Аккумулятор A.</summary>
        public Word48 A { get; set; }

        /// <summary>Регистр младших разрядов Y.</summary>
        public Word48 Y { get; set; }

        /// <summary>Регистр режима R.</summary>
        public uint R { get; set; }

        /// <summary>Регистр модификации адреса C.</summary>
        public uint C { get; set; }

        /// <summary>Признак применения C к следующей инструкции.</summary>
        public bool ApplyC { get; set; }

        /// <summary>Последний исполнительный адрес.</summary>
        public uint EffectiveAddress { get; set; }

        /// <summary>Счётчик перехвата арифметической ошибки.</summary>
        public int InterceptCount { get; }

        /// <summary>Адрес перехвата арифметической ошибки.</summary>
        public uint InterceptAddress { get; }

        /// <summary>Копия индексных регистров M0–M15.</summary>
        public uint[] M { get; set; }
    }
}
