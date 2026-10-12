namespace Besm6.Runtime;

/// <summary>
/// Table-cycle accounting for one hosted run. These are instruction notifications,
/// not instruction-clock ticks; exceptional extracode completion preserves its
/// existing notification/counting boundary.
/// </summary>
internal sealed class ExecutionAccountingSession : IDisposable
{
    private static readonly int[] Cycles = Enumerable.Range(0, 256)
        .Select(opcode => Besm6Timing.CyclesOf((Opcode)opcode)).ToArray();
    private readonly Processor _cpu;
    private readonly ProcessorInstructionAccounting _session;
    private bool _disposed;
    internal long TotalInstructions => _session.TotalInstructions;
    internal long TotalCycles => _session.TotalCycles;

    internal ExecutionAccountingSession(Processor cpu)
    {
        _cpu = cpu;
        _session = cpu.BeginInstructionAccounting(Cycles, Besm6Timing.DefaultCycles);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cpu.EndInstructionAccounting(_session);
        _disposed = true;
    }
}
