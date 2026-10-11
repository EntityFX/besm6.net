using System.Runtime.CompilerServices;

namespace Besm6.Runtime.Timing;

public sealed partial class HardwareTimeline
{
    // Calendar keys and callback data travel together. Small CPU/device queues
    // need neither a separate ValueTuple key nor an indirect comparer call.
    private sealed class ScheduledEventQueue
    {
        private ScheduledEvent[] _events = Array.Empty<ScheduledEvent>();
        internal int Count { get; private set; }
        internal ulong RootId => _events[0].Id;
        internal ulong RootTime => _events[0].Time;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Before(in ScheduledEvent left, in ScheduledEvent right) =>
            left.Time < right.Time || left.Time == right.Time && left.Id < right.Id;

        internal void Enqueue(in ScheduledEvent entry)
        {
            if (Count == _events.Length)
                Array.Resize(ref _events, _events.Length == 0 ? 4 : checked(_events.Length * 2));
            int index = Count++;
            while (index != 0)
            {
                int parent = (index - 1) / 2;
                if (!Before(in entry, in _events[parent])) break;
                _events[index] = _events[parent];
                index = parent;
            }
            _events[index] = entry;
        }

        internal ScheduledEvent Dequeue()
        {
            if (Count == 0) throw new InvalidOperationException("The hardware event queue is empty.");
            var first = _events[0];
            int lastIndex = --Count;
            if (lastIndex != 0)
            {
                var last = _events[lastIndex];
                int index = 0;
                while (index < lastIndex / 2)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < lastIndex && Before(in _events[child + 1], in _events[child])) child++;
                    if (!Before(in _events[child], in last)) break;
                    _events[index] = _events[child];
                    index = child;
                }
                _events[index] = last;
            }
            // Release callbacks/contexts as soon as their entries leave the heap.
            _events[lastIndex] = default;
            return first;
        }
    }
}
