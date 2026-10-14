using Besm6.Architecture.Machine;
using Besm6.Architecture.Isa;
using System.Runtime.CompilerServices;

namespace Besm6.Processor.Execution;

/// <summary>
/// Счётчики инструкций
/// </summary>
internal sealed class ProcessorInstructionAccounting : IProcessorInstructionAccounting
{
    private readonly int[] _cycles;
    private readonly int _defaultCycles;
    private int _pendingOpcode = -1;
    public long TotalInstructions { get; private set; }
    public long TotalCycles { get; private set; }
    public long CycleLimit { get; set; } = long.MaxValue;
    public bool HasPending => _pendingOpcode >= 0;
    public bool AtCycleLimit => TotalCycles >= CycleLimit;

    internal ProcessorInstructionAccounting(ReadOnlySpan<int> cycles, int defaultCycles)
    {
        if (cycles.Length != 256 || defaultCycles < 0)
            throw new ArgumentException("Accounting requires 256 nonnegative opcode costs.");
        foreach (int cost in cycles)
            if (cost < 0) throw new ArgumentException("Opcode costs cannot be negative.");
        _cycles = cycles.ToArray();
        _defaultCycles = defaultCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Begin(Opcode opcode) => _pendingOpcode = (int)opcode;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        int opcode = _pendingOpcode;
        if (opcode < 0) return;
        _pendingOpcode = -1;
        TotalInstructions++;
        TotalCycles += opcode < _cycles.Length ? _cycles[opcode] : _defaultCycles;
    }

    public void CancelPending() => _pendingOpcode = -1;
}
