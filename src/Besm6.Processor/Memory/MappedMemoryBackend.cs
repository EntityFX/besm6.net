namespace Besm6.Core;

public enum MemoryFaultSource { Protection, OperandBuffer, InstructionBuffer, PhysicalMemory, Panel }

/// <summary>Latched signal for a future hardware interrupt controller; not a Dubna avost.</summary>
public readonly record struct MemoryFault(MemoryRequestAddress Request, MemoryAccessKind Access,
    MemoryFaultSource Source, uint? PhysicalAddress);

/// <summary>Snapshot in MRU order. FetchAddress records where a possibly stale BRS word came from.</summary>
public readonly record struct AddressedMemoryBufferEntry(MemoryRequestAddress Request,
    MemoryWord50 Word, uint? FetchAddress = null);

/// <summary>
/// Untimed mathematical-address memory, with host-controlled mode bits and RP/RZ.
/// Shares ordinary instruction handlers; the supervisor profile attaches admission
/// callbacks. In-flight memory requests and prefetch timing remain a later stage.
/// </summary>
public sealed partial class MappedMemoryBackend : IInstructionMemory
{
    private readonly AddressBuffer _operands = new(8);
    private readonly AddressBuffer _instructions = new(4);
    private readonly MemoryWord50[] _panel = new MemoryWord50[8];
    private int _panelStores;
    private bool _reserveOperandSlot;

    /// <summary>Serial settled boundary of TO-2 §4.19; the oldest write is published
    /// when the last free BRZ is filled. Untimed host Mapped retains its eight slots.</summary>
    internal void EnableSupervisorWritePublication()
    {
        if (_operands.Count == 8)
            throw new InvalidOperationException("Publish pending writes before attaching the supervisor CPU.");
        _reserveOperandSlot = true;
    }

    public PhysicalMemory PhysicalMemory { get; }
    public PageAssignment Assignment { get; }
    public IMemory HostMemory { get; }
    public int Size => (int)PhysicalMemory.CapacityWords;
    // Software bootstrap defaults, not hardware cold-reset semantics or CPU M17.
    public bool Supervisor { get; set; } = true;
    public bool AssignmentBlocked { get; set; } = true;
    public bool ProtectionBlocked { get; set; } = true;
    public bool InvertLeftStoreControl { get; set; } = true;
    public bool InvertRightStoreControl { get; set; } = true;
    public MemoryFault? LastFault { get; private set; }
    public int PendingWriteCount => _operands.Count;

    public MappedMemoryBackend() : this(MemoryConfiguration.Classical32K) { }

    public MappedMemoryBackend(MemoryConfiguration configuration)
    {
        PhysicalMemory = new PhysicalMemory(configuration);
        Assignment = new PageAssignment(configuration);
        for (uint address = 0; address < PhysicalMemory.CapacityWords; address++)
            PhysicalMemory.Store(address, Word48.Zero, true, true);
        for (int i = 1; i < 8; i++) _panel[i] = MemoryWord50.Form(Word48.Zero, false, false);
        HostMemory = new HostView(this);
    }

    public AddressedMemoryBufferEntry[] GetOperandSnapshot() => _operands.Snapshot();
    public AddressedMemoryBufferEntry[] GetInstructionSnapshot() => _instructions.Snapshot();
    /// <summary>Physical BRZ storage, independent of its current recency/tag validity.</summary>
    public MemoryWord50 ReadOperandBufferRegister(int register) => _operands.ReadRegister(register);
    public void WriteOperandBufferRegister(int register, Word48 value) => _operands.WriteRegister(register,
        MemoryWord50.Form(value, InvertLeftStoreControl, InvertRightStoreControl));
    public int FindOperandBufferRegister(MemoryRequestAddress request) => _operands.FindRegister(request);
    internal Action<MemoryRequestAddress, bool>? OperandAccessAdmitted { get; set; }
    internal Action<MemoryRequestAddress, bool>? InstructionAccessAdmitted { get; set; }
    /// <summary>
    /// Explicit host bridge for the two operand-memory controls. Other M17 flags,
    /// supervisor mode and store parity are independent; no guest instruction runs.
    /// </summary>
    public void ApplyControlStatus(ControlUnitStatus status)
    {
        AssignmentBlocked = status.AssignmentBlocked;
        ProtectionBlocked = status.ProtectionBlocked;
    }

    public void ClearFault() => LastFault = null;
    public void ClearInstructionBuffer() { InvalidateHardwareRequests(false); _instructions.Clear(); }

    public MemoryWord50 GetPanel(uint address) { ValidatePanel(address); return _panel[address]; }
    public void SetPanel(uint address, MemoryWord50 word) { ValidatePanel(address); _panel[address] = word; }

    public Word48 Read(uint address)
    {
        var request = new MemoryRequestAddress(address, AssignmentBlocked);
        var resolved = AdmitOperand(request, false);
        if (resolved.Kind == MemoryAddressKind.ZeroOperand) return Word48.Zero;
        OperandAccessAdmitted?.Invoke(request, false);
        if (resolved.Kind == MemoryAddressKind.PanelRegister)
            return CheckOperand(request, _panel[address], address, MemoryFaultSource.Panel);
        int index = _operands.Find(request);
        MemoryWord50 word = index < 0 ? PhysicalMemory.ReadRaw(resolved.Address) : _operands.Touch(index).Word;
        return CheckOperand(request, word, resolved.Address,
            index < 0 ? MemoryFaultSource.PhysicalMemory : MemoryFaultSource.OperandBuffer);
    }

    public void Write(uint address, Word48 word)
    {
        var request = new MemoryRequestAddress(address, AssignmentBlocked);
        var resolved = AdmitOperand(request, true);
        if (resolved.Kind == MemoryAddressKind.ZeroOperand) return;
        OperandAccessAdmitted?.Invoke(request, true);
        if (resolved.Kind == MemoryAddressKind.PanelRegister)
        {
            // Untimed publication sequence: first store starts the sequence, next
            // eight publish BRZ oldest first. No guest write changes a panel register.
            if (_panelStores > 0) FlushOldest();
            _panelStores = Math.Min(_panelStores + 1, 8);
            return;
        }
        _panelStores = 0;
        int index = _operands.Find(request);
        if (index < 0 && _operands.IsFull) FlushOldest();
        _operands.Put(new(request, MemoryWord50.Form(word, InvertLeftStoreControl, InvertRightStoreControl)), index);
        if (_reserveOperandSlot && _operands.IsFull) FlushOldest();
    }

    public Word48 FetchInstruction(uint address, bool rightHalf)
    {
        var request = new MemoryRequestAddress(address, Supervisor);
        MemoryAccessKind access = rightHalf ? MemoryAccessKind.InstructionRight : MemoryAccessKind.InstructionLeft;
        ResolvedMemoryAddress resolved;
        try { resolved = Assignment.ResolveInstruction(address, Supervisor, rightHalf); }
        catch (MemoryProtectionException) { LastFault = new(request, access, MemoryFaultSource.Protection, null); throw; }
        InstructionAccessAdmitted?.Invoke(request, rightHalf);
        int index = _instructions.Find(request);
        AddressedMemoryBufferEntry entry;
        MemoryFaultSource source;
        if (index < 0)
        {
            var word = resolved.Kind == MemoryAddressKind.PanelRegister ? _panel[address] : PhysicalMemory.ReadRaw(resolved.Address);
            entry = new(request, word, resolved.Address);
            _instructions.Put(entry, -1);
            source = resolved.Kind == MemoryAddressKind.PanelRegister ? MemoryFaultSource.Panel : MemoryFaultSource.PhysicalMemory;
        }
        else { entry = _instructions.Touch(index); source = MemoryFaultSource.InstructionBuffer; }
        if (!entry.Word.HasValidInstructionControl(rightHalf))
        {
            LastFault = new(request, access, source, entry.FetchAddress);
            throw new MemoryControlException(entry.FetchAddress!.Value, access);
        }
        return entry.Word.Data;
    }

    /// <summary>Publish and release BRZ using current RP; does not invalidate BRS.</summary>
    public void FlushOperands()
    {
        while (_operands.Count != 0) FlushOldest();
    }

    /// <summary>Host serialization: validate, publish under old RP, then change assignment.</summary>
    public void SetPhysicalPage(uint mathematicalPage, uint physicalPage)
    {
        if (mathematicalPage >= PageAssignment.PageCount) throw new ArgumentOutOfRangeException(nameof(mathematicalPage));
        if (physicalPage >= Assignment.PhysicalPageCount) throw new ArgumentOutOfRangeException(nameof(physicalPage));
        InvalidateHardwareRequests(true);
        FlushOperands();
        Assignment.SetPhysicalPage(mathematicalPage, physicalPage);
    }

    public void ImportAssignmentGroup(uint group, Word48 accumulator)
    {
        Assignment.ValidateImportAssignmentGroup(group, accumulator);
        InvalidateHardwareRequests(true);
        FlushOperands();
        Assignment.ImportAssignmentGroup(group, accumulator);
    }

    /// <summary>
    /// Hosted Dubna CTX contract: command control, actual MRAM publication and coherent
    /// next fetch. Not an implementation of the historical supervisor subroutine.
    /// </summary>
    public void StoreCommand(uint address, Word48 word)
    {
        var request = new MemoryRequestAddress(address, AssignmentBlocked);
        var resolved = AdmitOperand(request, true);
        if (resolved.Kind == MemoryAddressKind.ZeroOperand) return;
        if (resolved.Kind == MemoryAddressKind.PanelRegister) { Write(address, word); return; }
        // Older aliased pending values must be published before the final command.
        FlushOperands();
        ReplacePhysicalFromHost(resolved.Address, MemoryWord50.Form(word, false, false));
    }

    private ResolvedMemoryAddress AdmitOperand(MemoryRequestAddress request, bool write)
    {
        try { return Assignment.ResolveOperand(request.Address, write, request.IsPhysical, ProtectionBlocked, Supervisor); }
        catch (MemoryProtectionException)
        {
            LastFault = new(request, write ? MemoryAccessKind.OperandWrite : MemoryAccessKind.OperandRead,
                MemoryFaultSource.Protection, null);
            throw;
        }
    }

    private Word48 CheckOperand(MemoryRequestAddress request, MemoryWord50 word, uint physical, MemoryFaultSource source)
    {
        if (!word.HasValidOperandControl)
        {
            LastFault = new(request, MemoryAccessKind.OperandRead, source, physical);
            throw new MemoryControlException(physical, MemoryAccessKind.OperandRead);
        }
        return word.Data;
    }

    private void FlushOldest()
    {
        if (_operands.Count == 0) return;
        // A synchronous host/legacy flush is a barrier against an older delayed write.
        if (HardwareRequestsInvalidated is not null) InvalidateHardwareRequests(true);
        var entry = _operands.At(_operands.Count - 1);
        PhysicalMemory.WriteRaw(Assignment.TranslateRequest(entry.Request), entry.Word);
        _operands.RemoveAt(_operands.Count - 1);
    }

    private Word48 PeekPhysical(uint address)
    {
        // Observe the last value in publication order; guest lookup remains by tag.
        for (int i = 0; i < _operands.Count; i++)
        {
            var entry = _operands.At(i);
            if (Assignment.TranslateRequest(entry.Request) == address) return entry.Word.Data;
        }
        return PhysicalMemory.ReadRaw(address).Data;
    }

    private void ReplacePhysicalFromHost(uint address, MemoryWord50 word)
    {
        _ = PhysicalMemory.ReadRaw(address); // Validate before cancelling any admitted transfer.
        InvalidateHardwareRequests(true);
        PhysicalMemory.WriteRaw(address, word);
        for (int i = _operands.Count - 1; i >= 0; i--)
            if (Assignment.TranslateRequest(_operands.At(i).Request) == address) _operands.RemoveAt(i);
        for (int i = _instructions.Count - 1; i >= 0; i--)
            if (_instructions.At(i).FetchAddress == address) _instructions.RemoveAt(i);
    }

    private static void ValidatePanel(uint address)
    {
        if (address is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(address));
    }

    // Separate physical loader/debugger view: no protection, control checks or recency.
    private sealed class HostView(MappedMemoryBackend backend) : IMemory
    {
        public int Size => backend.Size;
        public Word48 Read(uint address)
        {
            if (address >= backend.PhysicalMemory.CapacityWords) throw new ArgumentOutOfRangeException(nameof(address));
            return backend.PeekPhysical(address);
        }
        public void Write(uint address, Word48 word) => backend.ReplacePhysicalFromHost(address, MemoryWord50.Form(word, false, false));
    }

    private sealed class AddressBuffer(int capacity)
    {
        private readonly AddressedMemoryBufferEntry[] _entries = new AddressedMemoryBufferEntry[capacity];
        private readonly int[] _slots = new int[capacity];
        private readonly MemoryWord50[] _registers = new MemoryWord50[capacity];
        private readonly ulong[] _versions = new ulong[capacity];
        public int Count { get; private set; }
        public bool IsFull => Count == capacity;
        public AddressedMemoryBufferEntry At(int index) => _entries[index];
        public int Find(MemoryRequestAddress request)
        {
            for (int i = 0; i < Count; i++) if (_entries[i].Request == request) return i;
            return -1;
        }
        public AddressedMemoryBufferEntry Touch(int index)
        {
            var entry = _entries[index];
            Put(entry, index, false);
            return entry;
        }
        public void Put(AddressedMemoryBufferEntry entry, int index, bool changed = true)
        {
            int slot = index < 0 ? FreeSlot() : _slots[index];
            int shifted = index < 0 ? Math.Min(Count, capacity - 1) : index;
            Array.Copy(_entries, 0, _entries, 1, shifted);
            Array.Copy(_slots, 0, _slots, 1, shifted);
            _entries[0] = entry;
            _slots[0] = slot;
            _registers[slot] = entry.Word;
            if (changed) _versions[slot] = unchecked(_versions[slot] + 1);
            if (index < 0 && !IsFull) Count++;
        }
        public void RemoveAt(int index)
        {
            Array.Copy(_entries, index + 1, _entries, index, Count - index - 1);
            Array.Copy(_slots, index + 1, _slots, index, Count - index - 1);
            _entries[--Count] = default;
        }
        public void Clear() { Array.Clear(_entries); Count = 0; }
        public AddressedMemoryBufferEntry[] Snapshot() => _entries.AsSpan(0, Count).ToArray();
        private int FreeSlot()
        {
            for (int slot = 0; slot < capacity; slot++)
            {
                bool used = false;
                for (int i = 0; i < Count; i++) if (_slots[i] == slot) { used = true; break; }
                if (!used) return slot;
            }
            return _slots[Count - 1];
        }
        public int SlotAt(int index) => _slots[index];
        public ulong VersionAt(int slot) => _versions[slot];
        public int FindRegister(MemoryRequestAddress request)
        {
            int index = Find(request);
            return index < 0 ? -1 : _slots[index];
        }
        public MemoryWord50 ReadRegister(int register)
        {
            if ((uint)register >= capacity) throw new ArgumentOutOfRangeException(nameof(register));
            return _registers[register];
        }
        public void WriteRegister(int register, MemoryWord50 word)
        {
            if ((uint)register >= capacity) throw new ArgumentOutOfRangeException(nameof(register));
            _registers[register] = word;
            _versions[register] = unchecked(_versions[register] + 1);
            for (int i = 0; i < Count; i++)
                if (_slots[i] == register) _entries[i] = _entries[i] with { Word = word };
        }
    }
}
