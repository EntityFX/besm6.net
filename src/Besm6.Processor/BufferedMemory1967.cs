namespace Besm6.Core;

/// <summary>A diagnostic copy of one register; snapshots do not count as accesses.</summary>
public readonly record struct MemoryBufferEntry1967(uint PhysicalAddress, MemoryWord50 Word, bool PendingWrite);

/// <summary>
/// Untimed, physical-address buffer model for TO-8 (1967), §4.2–4.4, §4.10–4.11.
/// Eight operand write registers and four command word registers have independent LRU.
/// Not a CPU adapter: mathematical tags, privileged flush operations and asynchronous
/// writeback timing still require a configuration-specific specification.
/// </summary>
public sealed class BufferedMemory1967
{
    public const int OperandRegisterCount = 8;
    public const int InstructionRegisterCount = 4;

    private readonly PhysicalMemory1967 _memory;
    private readonly RegisterBuffer _operands = new(OperandRegisterCount);
    private readonly RegisterBuffer _instructions = new(InstructionRegisterCount);

    public BufferedMemory1967(PhysicalMemory1967 memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
    }

    public int PendingWriteCount => _operands.PendingWriteCount;

    /// <summary>Copies ordered from most recently to least recently accessed.</summary>
    public MemoryBufferEntry1967[] GetOperandSnapshot() => _operands.Snapshot();
    public MemoryBufferEntry1967[] GetInstructionSnapshot() => _instructions.Snapshot();

    /// <summary>Side-effect-free host observation of the current operand value, including BRZ.</summary>
    public MemoryWord50 PeekOperand(uint physicalAddress)
    {
        ValidateAddress(physicalAddress);
        int index = _operands.Find(physicalAddress);
        return index < 0 ? _memory.ReadRaw(physicalAddress) : _operands.At(index).Word;
    }

    /// <summary>
    /// Explicit coherent host replacement for loaders and hosted extracodes.
    /// Replaces MOZU and discards matching BRZ/BRS copies, including a pending store.
    /// This is not a hardware bus transfer or a privileged instruction sequence.
    /// </summary>
    public void ReplaceFromHost(uint physicalAddress, MemoryWord50 word)
    {
        ValidateAddress(physicalAddress);
        _memory.WriteRaw(physicalAddress, word);
        _operands.Remove(physicalAddress);
        _instructions.Remove(physicalAddress);
    }

    public void Store(uint physicalAddress, Word48 data, bool invertLeft, bool invertRight) =>
        StoreRaw(physicalAddress, MemoryWord50.Form(data, invertLeft, invertRight));

    /// <summary>Stores a word with its control bits, without making it visible to commands.</summary>
    public void StoreRaw(uint physicalAddress, MemoryWord50 word)
    {
        ValidateAddress(physicalAddress);
        int index = _operands.Find(physicalAddress);
        if (index < 0)
        {
            if (_operands.IsFull)
                WriteBack(_operands.LeastRecent);
            _operands.Insert(new(physicalAddress, word, true));
        }
        else
            _operands.ReplaceAndTouch(index, new(physicalAddress, word, true));
    }

    /// <summary>Only stores populate BRZ. A read miss goes to MOZU without allocating a register.</summary>
    public Word48 LoadOperand(uint physicalAddress)
    {
        ValidateAddress(physicalAddress);
        int index = _operands.Find(physicalAddress);
        MemoryWord50 word = index < 0 ? _memory.ReadRaw(physicalAddress) : _operands.Touch(index).Word;
        if (!word.HasValidOperandControl)
            throw new MemoryControlException(physicalAddress, MemoryAccessKind1967.OperandRead);
        return word.Data;
    }

    /// <summary>Commands come from BRS or MOZU, never from BRZ; only the selected half is checked.</summary>
    public Word48 FetchInstruction(uint physicalAddress, bool rightHalf)
    {
        ValidateAddress(physicalAddress);
        int index = _instructions.Find(physicalAddress);
        MemoryWord50 word;
        if (index < 0)
        {
            word = _memory.ReadRaw(physicalAddress);
            _instructions.Insert(new(physicalAddress, word, false));
        }
        else
            word = _instructions.Touch(index).Word;
        if (!word.HasValidInstructionControl(rightHalf))
            throw new MemoryControlException(physicalAddress, rightHalf
                ? MemoryAccessKind1967.InstructionRight : MemoryAccessKind1967.InstructionLeft);
        return word.Data;
    }

    /// <summary>
    /// Explicit host synchronization, not an emulated interrupt or CTX instruction.
    /// Commits pending BRZ values oldest first; retains registers and their recency.
    /// Does not invalidate BRS or advance any clock.
    /// </summary>
    public void WriteBackPendingOperands()
    {
        for (int index = _operands.Count - 1; index >= 0; index--)
        {
            MemoryBufferEntry1967 entry = _operands.At(index);
            WriteBack(entry);
            _operands.SetAt(index, entry with { PendingWrite = false });
        }
    }

    /// <summary>Explicit host operation, not an inferred hardware reset or automatic store invalidation.</summary>
    public void ClearInstructionBuffer() => _instructions.Clear();

    private void WriteBack(MemoryBufferEntry1967 entry)
    {
        if (entry.PendingWrite)
            _memory.WriteRaw(entry.PhysicalAddress, entry.Word);
    }

    private static void ValidateAddress(uint address)
    {
        if (address >= PhysicalMemory1967.WordCount)
            throw new ArgumentOutOfRangeException(nameof(address));
    }

    // Bounded recency order avoids timestamp rollover and needs no host or model clock.
    private sealed class RegisterBuffer(int capacity)
    {
        private readonly MemoryBufferEntry1967[] _entries = new MemoryBufferEntry1967[capacity];
        public int Count { get; private set; }
        public bool IsFull => Count == _entries.Length;
        public MemoryBufferEntry1967 LeastRecent => _entries[Count - 1];
        public int PendingWriteCount
        {
            get
            {
                int pending = 0;
                for (int i = 0; i < Count; i++) if (_entries[i].PendingWrite) pending++;
                return pending;
            }
        }

        public int Find(uint address)
        {
            for (int i = 0; i < Count; i++)
                if (_entries[i].PhysicalAddress == address) return i;
            return -1;
        }

        public MemoryBufferEntry1967 At(int index) => _entries[index];
        public void SetAt(int index, MemoryBufferEntry1967 entry) => _entries[index] = entry;

        public MemoryBufferEntry1967 Touch(int index)
        {
            MemoryBufferEntry1967 entry = _entries[index];
            ReplaceAndTouch(index, entry);
            return entry;
        }

        public void ReplaceAndTouch(int index, MemoryBufferEntry1967 entry)
        {
            Array.Copy(_entries, 0, _entries, 1, index);
            _entries[0] = entry;
        }

        public void Insert(MemoryBufferEntry1967 entry)
        {
            int shifted = Math.Min(Count, _entries.Length - 1);
            Array.Copy(_entries, 0, _entries, 1, shifted);
            _entries[0] = entry;
            if (!IsFull) Count++;
        }

        public MemoryBufferEntry1967[] Snapshot() => _entries.AsSpan(0, Count).ToArray();
        public void Remove(uint address)
        {
            int index = Find(address);
            if (index < 0) return;
            Array.Copy(_entries, index + 1, _entries, index, Count - index - 1);
            _entries[--Count] = default;
        }
        public void Clear()
        {
            Array.Clear(_entries);
            Count = 0;
        }
    }
}
