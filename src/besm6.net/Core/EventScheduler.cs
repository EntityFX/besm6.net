using System;
using System.Collections.Generic;

namespace Besm6.Core
{
    /// <summary>
    /// Неизменяемый идентификатор запланированного события (уровень B, Task B1).
    /// Используется для отмены (<see cref="IEventScheduler.Cancel"/>).
    /// </summary>
    public readonly struct EventToken : IEquatable<EventToken>
    {
        public readonly ulong Id;
        public EventToken(ulong id) { Id = id; }

        public bool Equals(EventToken other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is EventToken t && Equals(t);
        public override int GetHashCode() => Id.GetHashCode();
        public static bool operator ==(EventToken a, EventToken b) => a.Id == b.Id;
        public static bool operator !=(EventToken a, EventToken b) => a.Id != b.Id;
    }

    /// <summary>
    /// Детерминированный планировщик событий на дискретном модельном времени
    /// (уровень B, SuperPlan Task B1). События с одинаковым тиком исполняются
    /// в порядке регистрации (monotonic sequence). Не зависит от DateTime,
    /// thread scheduling или скорости host-машины.
    /// </summary>
    public interface IEventScheduler
    {
        /// <summary>Текущее модельное время.</summary>
        ulong Now { get; }

        /// <summary>
        /// Запланировать <paramref name="callback"/> на момент <c>Now + delay</c>.
        /// Возвращает токен для возможной отмены.
        /// </summary>
        EventToken Schedule(ulong delay, Action callback);

        /// <summary>
        /// Отменить событие по токену, если оно ещё не исполнено.
        /// true — событие было в очереди и отменено; false — токена нет (уже исполнено/отменено).
        /// </summary>
        bool Cancel(EventToken token);

        /// <summary>
        /// Продвинуть модельное время до <paramref name="tick"/>, исполнив все события
        /// с временем &le; tick в порядке (время, порядок регистрации).
        /// Запрещено <paramref name="tick"/> &lt; <see cref="Now"/> (движение назад) —
        /// бросает <see cref="InvalidOperationException"/>.
        /// </summary>
        void AdvanceTo(ulong tick);
    }

    internal readonly struct QueuedEvent
    {
        public readonly ulong Token;
        public readonly ulong Time;
        public readonly Action Callback;
        public QueuedEvent(ulong token, ulong time, Action callback)
        {
            Token = token;
            Time = time;
            Callback = callback;
        }
    }

    /// <summary>
    /// Стандартный <see cref="IEventScheduler"/>: минимальная priority-очередь
    /// (время, monotonic sequence) + lazy отмена (HashSet отменённых токенов).
    /// Время движется только вперёд и привязано к конкретной <see cref="SimulationClock"/>.
    /// </summary>
    public sealed class EventScheduler : IEventScheduler
    {
        private readonly SimulationClock _clock;
        private readonly PriorityQueue<QueuedEvent, (ulong time, ulong seq)> _queue = new();
        private readonly HashSet<ulong> _cancelled = new();
        private ulong _nextToken = 1;
        private ulong _nextSeq;

        public EventScheduler() : this(new SimulationClock()) { }

        public EventScheduler(SimulationClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Используемый источник модельного времени (read-only вид).</summary>
        public ISimulationClock Clock => _clock;

        public ulong Now => _clock.Tick;

        public EventToken Schedule(ulong delay, Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ulong time = _clock.Tick + delay;
            ulong token = _nextToken++;
            ulong seq = _nextSeq++;
            _queue.Enqueue(new QueuedEvent(token, time, callback), (time, seq));
            return new EventToken(token);
        }

        public bool Cancel(EventToken token) => _cancelled.Add(token.Id);

        public void AdvanceTo(ulong tick)
        {
            if (tick < _clock.Tick)
                throw new InvalidOperationException(
                    $"Simulation time cannot move backward (current {_clock.Tick}, requested {tick}).");

            // Исполняем все события со временем <= tick в порядке (время, seq),
            // продвигая часы к времени каждого события (монотонно — очередь отсортирована).
            while (_queue.TryPeek(out _, out var prio) && prio.time <= tick)
            {
                if (!_queue.TryDequeue(out var ev, out _))
                    break;
                if (_cancelled.Contains(ev.Token))
                    continue;
                _clock.AdvanceTo(ev.Time);
                ev.Callback();
            }

            _clock.AdvanceTo(tick);
        }
    }
}
