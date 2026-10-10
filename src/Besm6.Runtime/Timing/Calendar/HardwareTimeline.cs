namespace Besm6.Runtime.Timing;

/// <summary>A token belongs to exactly one timeline; default is never active.</summary>
public readonly record struct HardwareEventToken
{
    private readonly object? _owner;
    public ulong Id { get; }
    internal HardwareEventToken(object owner, ulong id) { _owner = owner; Id = id; }
    internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);
}

/// <summary>A host callback failure, separate from a guest processor fault.</summary>
public sealed class HardwareCallbackException : Exception
{
    public HardwareInstant Time { get; }
    public HardwareEventToken Token { get; }
    internal HardwareCallbackException(HardwareInstant time, HardwareEventToken token, Exception cause)
        : base($"Hardware callback {token.Id} failed at {time.Nanoseconds} ns.", cause)
    { Time = time; Token = token; }
}

/// <summary>
/// Deterministic event calendar in nanoseconds. It may be owned by MachineCore,
/// does not wait on host clocks, and does not reinterpret
/// SimulationClock. Equal-time callbacks follow registration order.
/// </summary>
public sealed class HardwareTimeline
{
    private readonly PriorityQueue<(ulong Id, Action Callback), (ulong Time, ulong Id)> _queue = new();
    private readonly HashSet<ulong> _pending = new();
    private ulong _nextId = 1;
    private bool _advancing;
    internal bool IsAdvancing => _advancing;
    internal bool AdvancementProhibited { get; set; }
    internal Action<bool>? AdvanceStateChanged { get; set; }
    public HardwareInstant Now { get; private set; }
    public HardwareInstant? NextEventTime { get; private set; }

    public HardwareTimeline(HardwareInstant initialTime = default) => Now = initialTime;

    public HardwareEventToken Schedule(HardwareDuration delay, Action callback) =>
        ScheduleAt(Now + delay, callback);

    public HardwareEventToken ScheduleAt(HardwareInstant time, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (time.Nanoseconds < Now.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(time), "Cannot schedule in the past.");
        ulong id = _nextId;
        ulong nextId = checked(id + 1);
        _queue.Enqueue((id, callback), (time.Nanoseconds, id));
        _pending.Add(id);
        _nextId = nextId;
        if (NextEventTime is null || time.Nanoseconds < NextEventTime.Value.Nanoseconds)
            NextEventTime = time;
        return new(this, id);
    }

    public bool Cancel(HardwareEventToken token)
    {
        if (!token.BelongsTo(this) || !_pending.Remove(token.Id)) return false;
        RefreshNextEvent();
        return true;
    }

    private void RefreshNextEvent()
    {
        while (_queue.TryPeek(out var ev, out var priority))
        {
            if (_pending.Contains(ev.Id))
            { NextEventTime = new(priority.Time); return; }
            _queue.Dequeue();
        }
        NextEventTime = null;
    }

    public void AdvanceTo(HardwareInstant time) => AdvanceToCore(time, null);

    /// <summary>
    /// Release the calendar's callback guards at an execution boundary. Remaining
    /// callbacks at the same instant keep their order and remain queued. The model
    /// can then complete a shared CPU command outside a calendar callback.
    /// </summary>
    internal void AdvanceUntil(HardwareInstant time, Func<bool> boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        AdvanceToCore(time, boundary);
    }

    private void AdvanceToCore(HardwareInstant time, Func<bool>? boundary)
    {
        if (AdvancementProhibited)
            throw new InvalidOperationException("Hardware advancement inside another machine scheduler is not allowed.");
        if (_advancing) throw new InvalidOperationException("Nested hardware-time advancement is not allowed.");
        if (time.Nanoseconds < Now.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(time), "Hardware time cannot move backward.");
        _advancing = true;
        try
        {
            AdvanceStateChanged?.Invoke(true);
            if (boundary?.Invoke() == true) return;
            while (NextEventTime is { } next && next.Nanoseconds <= time.Nanoseconds)
            {
                var ev = _queue.Dequeue();
                _pending.Remove(ev.Id);
                RefreshNextEvent();
                Now = next;
                try { ev.Callback(); }
                catch (Exception cause) { throw new HardwareCallbackException(Now, new(this, ev.Id), cause); }
                if (boundary?.Invoke() == true) return;
            }
            Now = time;
        }
        finally
        {
            _advancing = false;
            AdvanceStateChanged?.Invoke(false);
        }
    }
}
