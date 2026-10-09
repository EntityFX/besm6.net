using System;

namespace Besm6.Runtime
{
    /// <summary>
    /// Дискретное модельное время симулятора (уровень B, SuperPlan Task B1).
    /// Чисто монотонный счётчик тиков: НЕ зависит от DateTime, thread scheduling
    /// или скорости host-машины. Единственный источник модельного времени уровня B.
    /// Интерфейс — read-only вид для потребителей; advancement — на конкретной
    /// <see cref="SimulationClock"/> (методы Advance/AdvanceTo).
    /// </summary>
    public interface ISimulationClock
    {
        /// <summary>Текущий тик модельного времени (монотонно неубывающий).</summary>
        ulong Tick { get; }
    }

    /// <summary>
    /// Стандартная реализация <see cref="ISimulationClock"/>: монотонный счётчик тиков.
    /// Время движется только вперёд; движение назад — <see cref="InvalidOperationException"/>.
    /// Детерминирован: один и тот же workload всегда даёт одну и ту же последовательность тиков.
    /// </summary>
    public sealed class SimulationClock : ISimulationClock
    {
        private ulong _tick;
        internal bool AdvancementProhibited { get; set; }

        public SimulationClock(ulong initialTick = 0)
        {
            _tick = initialTick;
        }

        public ulong Tick => _tick;
        internal ref ulong TickReference => ref _tick;

        /// <summary>Сдвинуть модельное время вперёд на <paramref name="delta"/> тиков.</summary>
        public void Advance(ulong delta)
        {
            EnsureAdvancementAllowed();
            _tick += delta;
        }

        /// <summary>
        /// Установить модельное время. Запрещено движение назад
        /// (tick &lt; текущего) — бросает <see cref="InvalidOperationException"/>.
        /// </summary>
        public void AdvanceTo(ulong tick)
        {
            EnsureAdvancementAllowed();
            if (tick < _tick)
                throw new InvalidOperationException(
                    $"Simulation time cannot move backward (current {_tick}, requested {tick}).");
            _tick = tick;
        }

        private void EnsureAdvancementAllowed()
        {
            if (AdvancementProhibited)
                throw new InvalidOperationException("Clock advancement inside a scheduler callback is not allowed.");
        }
    }
}
