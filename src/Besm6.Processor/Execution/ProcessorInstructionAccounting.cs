using System.Runtime.CompilerServices;

namespace Besm6.Core;

/// <summary>
/// Internal per-run opcode accounting, independent of diagnostic subscribers.
/// Runtime supplies immutable instruction costs; Processor has no timing-model
/// or host-clock dependency. Pending faults complete at the canonical boundary.
/// </summary>
internal sealed class ProcessorInstructionAccounting
{
    private readonly int[] _cycles;
    private readonly int _defaultCycles;
    private int _pendingOpcode = -1;
    internal long TotalInstructions { get; private set; }
    internal long TotalCycles { get; private set; }
    internal long CycleLimit { get; set; } = long.MaxValue;
    internal bool HasPending => _pendingOpcode >= 0;
    internal bool AtCycleLimit => TotalCycles >= CycleLimit;

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
    internal void Begin(Opcode opcode) => _pendingOpcode = (int)opcode;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Complete()
    {
        int opcode = _pendingOpcode;
        if (opcode < 0) return;
        _pendingOpcode = -1;
        TotalInstructions++;
        TotalCycles += opcode < _cycles.Length ? _cycles[opcode] : _defaultCycles;
    }

    internal void CancelPending() => _pendingOpcode = -1;
}
