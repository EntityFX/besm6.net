using System;

namespace Besm6.Core
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

        public SimulationClock(ulong initialTick = 0)
        {
            _tick = initialTick;
        }

        public ulong Tick => _tick;

        /// <summary>Сдвинуть модельное время вперёд на <paramref name="delta"/> тиков.</summary>
        public void Advance(ulong delta)
        {
            _tick += delta;
        }

        /// <summary>
        /// Установить модельное время. Запрещено движение назад
        /// (tick &lt; текущего) — бросает <see cref="InvalidOperationException"/>.
        /// </summary>
        public void AdvanceTo(ulong tick)
        {
            if (tick < _tick)
                throw new InvalidOperationException(
                    $"Simulation time cannot move backward (current {_tick}, requested {tick}).");
            _tick = tick;
        }
    }
}
