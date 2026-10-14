using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Memory;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Calendar;
using Besm6.Runtime.Timing.Memory;

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
internal sealed partial class HardwareBufferedMemory
{
    private sealed class Pending
    {
        internal required HardwareBufferedMemoryToken Token;
        internal MramRequestToken MemoryToken;
        internal HardwareEventToken LocalEvent;
        internal bool Write;
        internal bool Store;
        internal uint Address;
        internal HardwareBufferedMemory ReadOwner = null!;
        internal MappedReadAdmission Admission = null!;
        internal Action<HardwareBufferedRead>? ReadCompleted;
    }
    private readonly MappedMemoryBackend _backend;
    private readonly HardwareTimeline _timeline;
    private readonly MramMemoryPort _port;
    private readonly Dictionary<ulong, Pending> _pending = new();
    private readonly Stack<Pending> _spareReads = new();
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
        backend.HardwareStoresInvalidated += () => { if (_store is { } store) Cancel(store.Pending.Token); };
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
        var pending = _spareReads.TryPop(out var spare) ? spare : new Pending { Token = default, ReadOwner = this };
        pending.Token = new(this, _nextId); pending.Address = address;
        pending.Admission = admission; pending.ReadCompleted = completed;
        if (admission.BufferedWord is { } buffered)
            pending.LocalEvent = _timeline.Schedule(BufferHitLatency, static (context, _) =>
            {
                var current = (Pending)context;
                current.ReadOwner.Receive(current.Token.Id, current.Admission.BufferedWord!.Value);
            }, pending);
        else pending.MemoryToken = _port.Read(admission.PhysicalAddress!.Value,
            static (context, id, transfer) => ((HardwareBufferedMemory)context).Receive(id, transfer.Word), this, pending.Token.Id);
        _pending.Add(_nextId, pending); _nextId = next;
        return pending.Token;
    }

    private void Receive(ulong id, MemoryWord50 raw)
    {
        if (!_pending.Remove(id, out var pending)) return;
        try
        {
            Word48 word = raw.Data; MemoryControlException? failure = null;
            try { word = _backend.CompleteHardwareRead(pending.Admission, raw); }
            catch (MemoryControlException cause) { failure = cause; }
            pending.ReadCompleted!(new(pending.Token, pending.Address, word, raw,
                pending.Admission.Source, _timeline.Now, failure));
        }
        finally { ReleaseRead(pending); }
    }

    private void ReleaseRead(Pending pending, bool reusable = true)
    {
        pending.Admission = null!; pending.ReadCompleted = null;
        pending.MemoryToken = default; pending.LocalEvent = default;
        // A cancelled local event can still retain its context in a tombstone.
        // Such a request is never reused; completed events have left the queue.
        if (reusable && _spareReads.Count < 128) _spareReads.Push(pending);
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
            try { completed(removed); }
            finally { ResumeWaitingStore(); }
        });
        _pending.Add(_nextId, pending); _nextId = next; _writeback = pending;
        token = pending.Token; return true;
    }

    internal bool Cancel(HardwareBufferedMemoryToken token)
    {
        if (!token.BelongsTo(this) || !_pending.Remove(token.Id, out var pending)) return false;
        _timeline.Cancel(pending.LocalEvent); _port.Cancel(pending.MemoryToken);
        if (ReferenceEquals(_writeback, pending)) _writeback = null;
        CancelWaitingStore(token);
        try { RequestCancelled?.Invoke(token); }
        finally { if (!pending.Write && !pending.Store) ReleaseRead(pending, pending.LocalEvent == default); }
        return true;
    }

    private void Invalidate(bool writes)
    {
        foreach (var pending in _pending.Values.ToArray())
            if (writes || (!pending.Write && !pending.Store)) Cancel(pending.Token);
    }
}
