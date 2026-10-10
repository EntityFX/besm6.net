using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal readonly record struct HardwareBufferedMemoryToken
{
    private readonly object? _owner;
    internal ulong Id { get; }
    internal HardwareBufferedMemoryToken(object owner, ulong id) { _owner = owner; Id = id; }
    internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);
}
internal readonly record struct HardwareBufferedRead(HardwareBufferedMemoryToken Token,
    uint Address, Word48 Word, MemoryWord50 RawWord, MemoryFaultSource Source,
    HardwareInstant CompletedAt, MemoryControlException? Failure);

/// <summary>
/// Timed BRZ/BRS (БРЗ — буферные регистры записи; БРС — буферные регистры слов)
/// bridge using the existing backend registers and raw MRAM port. No parallel cache
/// or guest arithmetic exists. Hit delay and FIFO remain explicit logical policies.
/// </summary>
internal sealed class HardwareBufferedMemory
{
    private sealed class Pending
    {
        internal required HardwareBufferedMemoryToken Token;
        internal MramRequestToken MemoryToken;
        internal HardwareEventToken LocalEvent;
        internal bool Write;
    }
    private readonly MappedMemoryBackend _backend;
    private readonly HardwareTimeline _timeline;
    private readonly MramMemoryPort _port;
    private readonly Dictionary<ulong, Pending> _pending = new();
    private ulong _nextId = 1;
    private Pending? _writeback;
    internal HardwareDuration BufferHitLatency { get; }
    internal event Action<HardwareBufferedMemoryToken>? RequestCancelled;
    internal int PendingRequests => _pending.Count;

    internal HardwareBufferedMemory(MappedMemoryBackend backend, HardwareTimeline timeline,
        MramMemoryPort port, HardwareDuration hitLatency)
    {
        _backend = backend; _timeline = timeline; _port = port; BufferHitLatency = hitLatency;
        backend.HardwareRequestsInvalidated += Invalidate;
    }

    internal HardwareBufferedMemoryToken ReadOperand(uint address, Action<HardwareBufferedRead> completed) =>
        Read(address, false, false, completed);
    internal HardwareBufferedMemoryToken FetchInstruction(uint address, bool rightHalf,
        Action<HardwareBufferedRead> completed) => Read(address, true, rightHalf, completed);

    private HardwareBufferedMemoryToken Read(uint address, bool instruction, bool rightHalf,
        Action<HardwareBufferedRead> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ulong next = checked(_nextId + 1);
        var admission = _backend.AdmitHardwareRead(address, instruction, rightHalf);
        var pending = new Pending { Token = new(this, _nextId) };
        void Receive(MemoryWord50 raw)
        {
            _pending.Remove(pending.Token.Id);
            Word48 word = raw.Data; MemoryControlException? failure = null;
            try { word = _backend.CompleteHardwareRead(admission, raw); }
            catch (MemoryControlException cause) { failure = cause; }
            completed(new(pending.Token, address, word, raw, admission.Source, _timeline.Now, failure));
        }
        if (admission.BufferedWord is { } buffered)
            pending.LocalEvent = _timeline.Schedule(BufferHitLatency, () => Receive(buffered));
        else pending.MemoryToken = _port.Read(admission.PhysicalAddress!.Value, transfer => Receive(transfer.Word));
        _pending.Add(_nextId, pending); _nextId = next;
        return pending.Token;
    }

    internal bool TryPublishOldest(Action<bool> completed, out HardwareBufferedMemoryToken token)
    {
        ArgumentNullException.ThrowIfNull(completed); token = default;
        if (_writeback is not null) return false;
        var admission = _backend.CaptureOldestHardwareWrite();
        if (admission is null) return false;
        ulong next = checked(_nextId + 1);
        var pending = new Pending { Token = new(this, _nextId), Write = true };
        pending.MemoryToken = _port.Write(admission.PhysicalAddress, admission.Entry.Word, _ =>
        {
            _pending.Remove(pending.Token.Id); _writeback = null;
            bool removed = _backend.CompleteHardwareWrite(admission);
            completed(removed);
        });
        _pending.Add(_nextId, pending); _nextId = next; _writeback = pending;
        token = pending.Token; return true;
    }

    internal bool Cancel(HardwareBufferedMemoryToken token)
    {
        if (!token.BelongsTo(this) || !_pending.Remove(token.Id, out var pending)) return false;
        _timeline.Cancel(pending.LocalEvent); _port.Cancel(pending.MemoryToken);
        if (ReferenceEquals(_writeback, pending)) _writeback = null;
        RequestCancelled?.Invoke(token);
        return true;
    }

    private void Invalidate(bool writes)
    {
        foreach (var pending in _pending.Values.ToArray())
            if (writes || !pending.Write) Cancel(pending.Token);
    }
}
