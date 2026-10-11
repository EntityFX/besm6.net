namespace Besm6.Core;

internal sealed partial class ProcessorMemoryAccess
{
    internal void CheckTransferredAccess(uint address, bool write)
    {
        if (_debugWatch.MemoryWatchArmed && _debugWatch.DebugCheckMemory(address & 0x7FFF, write ? 1u : 2u))
            throw new Processor.DebugWatchAbortException();
    }
    internal ulong AcceptTransferredFetch(uint address, Word48 word)
    {
        if ((address & 0x7FFF) == 0 && !_allowZeroInstructionAddress) ThrowJumpToZero();
        return word.Value;
    }

    // Hardware transfer uses the same load-watchpoint/zero-address path; the fast
    // interpreter never probes a transfer flag or delegate on an ordinary read.
    private TransferredOperandMemory? _transferMemory;
    private ProcessorMemoryAccess? _transferAccess;

    internal ProcessorMemoryAccess BeginTransferredOperand(uint address, Word48 word, Exception? failure)
    {
        if (_transferMemory is null)
        {
            _transferMemory = new TransferredOperandMemory(_memory.Size);
            _transferAccess = new ProcessorMemoryAccess(_debugWatch, _transferMemory, _allowZeroInstructionAddress);
        }
        _transferMemory.Begin(address, word, failure);
        return _transferAccess!;
    }

    internal void EndTransferredOperand() => _transferMemory!.End();

    // Only the synchronous capture owns this view. No calendar entry retains it.
    private sealed class TransferredOperandMemory(int size) : IMemory
    {
        private bool _active, _consumed;
        private uint _address;
        private Word48 _word;
        private Exception? _failure;
        public int Size => size;

        internal void Begin(uint address, Word48 word, Exception? failure)
        {
            if (_active) throw new InvalidOperationException("A transferred operand capture is already active.");
            _address = address;
            _word = word;
            _failure = failure;
            _consumed = false;
            _active = true;
        }

        internal void End()
        {
            _active = false;
            _word = default;
            _failure = null;
        }

        public Word48 Read(uint requested)
        {
            if (!_active || _consumed || requested != _address)
                throw new InvalidOperationException("The CPU load does not match its single accepted transfer.");
            _consumed = true;
            if (_failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_failure).Throw();
            return _word;
        }
        public void Write(uint requested, Word48 value) => throw new InvalidOperationException("A transferred operand cannot store.");
    }
}
