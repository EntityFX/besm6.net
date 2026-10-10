namespace Besm6.Runtime.Timing;

/// <summary>Explicit logical approximations, not an inferred electrical priority.</summary>
public enum MramReadSampling { AtCycleStart, AtDataReturn }
public enum MramArbitration { FifoPerBank }

/// <summary>Read latency is approximate in TO-4; write visibility and sampling are caller-selected.</summary>
public readonly record struct MramPortConfiguration(HardwareDuration BankCycle,
    HardwareDuration ReadLatency, HardwareDuration WriteVisibility,
    MramReadSampling ReadSampling, MramArbitration Arbitration);

public readonly record struct MramRequestToken
{
    private readonly object? _owner;
    public ulong Id { get; }
    internal MramRequestToken(object owner, ulong id) { _owner = owner; Id = id; }
    internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);
}

/// <summary>Raw 50-bit transfer; control-bit interpretation belongs to the receiving unit.</summary>
public readonly record struct MramTransfer(MramRequestToken Token, uint Address, bool Write,
    MemoryWord50 Word, HardwareInstant Started, HardwareInstant DataReady, HardwareInstant BankAvailable);

/// <summary>
/// MRAM (МОЗУ — магнитное оперативное запоминающее устройство) port on the machine calendar.
/// All requests use the shared PhysicalMemory. FIFO and write timing are explicit logical
/// approximations; this port does not infer buffer ownership, mapping, parity or CPU deadlines.
/// Cancellation suppresses an unreturned transfer, never shortens an accepted magnetic cycle.
/// </summary>
public sealed class MramMemoryPort
{
    private sealed class Request
    {
        internal required MramRequestToken Token;
        internal required uint Address;
        internal required Action<MramTransfer> Completion;
        internal bool Write;
        internal MemoryWord50 Word;
        internal HardwareEventToken ReturnEvent;
        internal LinkedListNode<Request>? QueueNode;
    }

    private readonly PhysicalMemory _memory;
    private readonly HardwareTimeline _timeline;
    private readonly MramReadTiming _banks;
    private readonly LinkedList<Request>[] _waiting = Enumerable.Range(0, 8).Select(_ => new LinkedList<Request>()).ToArray();
    private readonly HardwareEventToken[] _starts = new HardwareEventToken[8];
    private readonly Dictionary<ulong, Request> _requests = new();
    private ulong _nextId = 1;
    public MramPortConfiguration Configuration { get; }
    public int PendingRequests => _requests.Count;
    public HardwareInstant AvailableAt(uint address) => _banks.AvailableAt(address);

    public MramMemoryPort(PhysicalMemory memory, HardwareTimeline timeline, MramPortConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(timeline);
        if (!Enum.IsDefined(configuration.ReadSampling) || configuration.Arbitration != MramArbitration.FifoPerBank)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        if (configuration.WriteVisibility.Nanoseconds == 0 ||
            configuration.WriteVisibility.Nanoseconds > configuration.BankCycle.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        _banks = new(memory.Configuration, configuration.BankCycle, configuration.ReadLatency);
        _memory = memory; _timeline = timeline; Configuration = configuration;
    }

    public MramRequestToken Read(uint address, Action<MramTransfer> completion) => Enqueue(address, false, default, completion);
    public MramRequestToken Write(uint address, MemoryWord50 word, Action<MramTransfer> completion) => Enqueue(address, true, word, completion);

    private MramRequestToken Enqueue(uint address, bool write, MemoryWord50 word, Action<MramTransfer> completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var available = _banks.AvailableAt(address); // Validate the physical address before allocating state.
        int bank = (int)(address & 7);
        ulong start = Math.Max(available.Nanoseconds, _timeline.Now.Nanoseconds);
        // Validate the latest FIFO cycle before registering any event or consuming a token.
        start = checked(start + checked((ulong)_waiting[bank].Count * Configuration.BankCycle.Nanoseconds));
        _ = checked(start + Configuration.BankCycle.Nanoseconds);
        ulong next = checked(_nextId + 1);
        var request = new Request { Token = new(this, _nextId), Address = address, Write = write, Word = word, Completion = completion };
        if (_waiting[bank].Count == 0)
            _starts[bank] = _timeline.ScheduleAt(new(Math.Max(available.Nanoseconds, _timeline.Now.Nanoseconds)), () => Start(bank));
        request.QueueNode = _waiting[bank].AddLast(request);
        _requests.Add(_nextId, request);
        _nextId = next;
        return request.Token;
    }

    private void Start(int bank)
    {
        _starts[bank] = default;
        var request = _waiting[bank].First!.Value;
        if (!_banks.TryBeginRead(request.Address, _timeline.Now, out var cycle))
            throw new InvalidOperationException("The MRAM queue started before its bank was available.");
        _waiting[bank].RemoveFirst(); request.QueueNode = null;
        if (!request.Write && Configuration.ReadSampling == MramReadSampling.AtCycleStart)
            request.Word = _memory.ReadRaw(request.Address);
        var ready = request.Write ? cycle.Started + Configuration.WriteVisibility : cycle.DataReady;
        request.ReturnEvent = _timeline.ScheduleAt(ready, () => Return(request, cycle, ready));
        if (_waiting[bank].Count != 0)
            _starts[bank] = _timeline.ScheduleAt(cycle.BankAvailable, () => Start(bank));
    }

    private void Return(Request request, MramReadReservation cycle, HardwareInstant ready)
    {
        _requests.Remove(request.Token.Id); // The transfer is terminal even if its observer fails.
        if (request.Write) _memory.WriteRaw(request.Address, request.Word);
        else if (Configuration.ReadSampling == MramReadSampling.AtDataReturn)
            request.Word = _memory.ReadRaw(request.Address);
        request.Completion(new(request.Token, request.Address, request.Write, request.Word,
            cycle.Started, ready, cycle.BankAvailable));
    }

    public bool Cancel(MramRequestToken token)
    {
        if (!token.BelongsTo(this) || !_requests.Remove(token.Id, out var request)) return false;
        if (request.QueueNode is { } node)
        {
            int bank = (int)(request.Address & 7);
            bool head = node == _waiting[bank].First;
            _waiting[bank].Remove(node); request.QueueNode = null;
            if (head)
            {
                _timeline.Cancel(_starts[bank]); _starts[bank] = default;
                if (_waiting[bank].Count != 0)
                    _starts[bank] = _timeline.ScheduleAt(new(Math.Max(_timeline.Now.Nanoseconds,
                        _banks.AvailableAt(request.Address).Nanoseconds)), () => Start(bank));
            }
        }
        else _timeline.Cancel(request.ReturnEvent);
        return true;
    }
}
