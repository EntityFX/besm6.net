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
    internal ProcessorMemoryAccess WithTransferredOperand(uint address, Word48 word, Exception? failure) =>
        new(_debugWatch, new TransferredOperandMemory(_memory.Size, address, word, failure), _allowZeroInstructionAddress);

    private sealed class TransferredOperandMemory(int size, uint address, Word48 word, Exception? failure) : IMemory
    {
        private bool _consumed;
        public int Size => size;
        public Word48 Read(uint requested)
        {
            if (_consumed || requested != address)
                throw new InvalidOperationException("The CPU load does not match its single accepted transfer.");
            _consumed = true;
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return word;
        }
        public void Write(uint requested, Word48 value) => throw new InvalidOperationException("A transferred operand cannot store.");
    }
}
