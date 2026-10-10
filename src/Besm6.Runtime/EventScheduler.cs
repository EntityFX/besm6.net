using System;
using System.Collections.Generic;

namespace Besm6.Runtime
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

    /// <summary>A host callback failed; this is not a guest processor fault.</summary>
    public sealed class SchedulerCallbackException : Exception
    {
        public EventToken Token { get; }
        public ulong Tick { get; }

        internal SchedulerCallbackException(EventToken token, ulong tick, Exception cause)
            : base($"Scheduled callback {token.Id} failed at tick {tick}.", cause)
        {
            Token = token;
            Tick = tick;
        }
    }

    /// <summary>
    /// Стандартный <see cref="IEventScheduler"/>: минимальная priority-очередь
    /// (время, monotonic sequence) + lazy отмена с учётом активных токенов.
    /// Время движется только вперёд и привязано к конкретной <see cref="SimulationClock"/>.
    /// </summary>
    public sealed class EventScheduler : IEventScheduler
    {
        private readonly SimulationClock _clock;
        private readonly PriorityQueue<QueuedEvent, (ulong time, ulong seq)> _queue = new();
        private readonly HashSet<ulong> _pending = new();
        private ulong _nextToken = 1;
        private ulong _nextSeq;
        internal ulong? NextEventTick { get; private set; }
        internal bool IsAdvancing { get; private set; }
        internal bool AdvancementProhibited { get; set; }
        internal Action<bool>? AdvanceStateChanged { get; set; }

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
            ulong time = checked(_clock.Tick + delay);
            ulong token = _nextToken;
            ulong seq = _nextSeq;
            _nextToken = checked(token + 1);
            _nextSeq = checked(seq + 1);
            _queue.Enqueue(new QueuedEvent(token, time, callback), (time, seq));
            _pending.Add(token);
            if (NextEventTick is null || time < NextEventTick.Value)
                NextEventTick = time;
            return new EventToken(token);
        }

        public bool Cancel(EventToken token)
        {
            if (!_pending.Remove(token.Id)) return false;
            RefreshNextEvent();
            return true;
        }

        private void RefreshNextEvent()
        {
            while (_queue.TryPeek(out var ev, out var priority))
            {
                if (_pending.Contains(ev.Token))
                {
                    NextEventTick = priority.time;
                    return;
                }
                _queue.Dequeue();
            }
            NextEventTick = null;
        }

        public void AdvanceTo(ulong tick)
            => AdvanceToCore(tick, allowOverdue: false);

        // CPU hooks can schedule delay-zero events before the step commits its
        // tick. Such events are delivered at the committed instruction boundary.
        internal void DeliverCurrentEvents() => AdvanceToCore(_clock.Tick, allowOverdue: true);

        private void AdvanceToCore(ulong tick, bool allowOverdue)
        {
            if (AdvancementProhibited)
                throw new InvalidOperationException("Scheduler advancement inside a hardware callback is not allowed.");
            if (IsAdvancing)
                throw new InvalidOperationException("Nested scheduler advancement is not allowed.");
            if (tick < _clock.Tick)
                throw new InvalidOperationException(
                    $"Simulation time cannot move backward (current {_clock.Tick}, requested {tick}).");

            // Advancing a shared clock past a pending event outside this scheduler
            // is a contract violation. Do not consume the event or move time back.
            if (!allowOverdue && NextEventTick is ulong overdue && overdue < _clock.Tick)
                throw new InvalidOperationException("The shared clock advanced past a pending event.");

            IsAdvancing = true;
            AdvanceStateChanged?.Invoke(true);
            try
            {
                while (NextEventTick is ulong next && next <= tick)
                {
                    var ev = _queue.Dequeue();
                    _pending.Remove(ev.Token);
                    RefreshNextEvent();
                    _clock.AdvanceTo(Math.Max(_clock.Tick, ev.Time));
                    _clock.AdvancementProhibited = true;
                    try { ev.Callback(); }
                    catch (Exception exception)
                    {
                        throw new SchedulerCallbackException(new EventToken(ev.Token), _clock.Tick, exception);
                    }
                    finally { _clock.AdvancementProhibited = false; }
                }
                _clock.AdvanceTo(tick);
            }
            finally
            {
                IsAdvancing = false;
                AdvanceStateChanged?.Invoke(false);
            }
        }
    }
}
