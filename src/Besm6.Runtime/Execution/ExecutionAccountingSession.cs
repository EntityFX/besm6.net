using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Processor.Execution;
using Besm6.Runtime.Profiling;

namespace Besm6.Runtime.Execution;

/// <summary>
/// Table-cycle accounting for one hosted run. These are instruction notifications,
/// not instruction-clock ticks; exceptional extracode completion preserves its
/// existing notification/counting boundary.
/// </summary>
internal sealed class ExecutionAccountingSession : IDisposable
{
    private static readonly int[] Cycles = Enumerable.Range(0, 256)
        .Select(opcode => Besm6Timing.CyclesOf((Opcode)opcode)).ToArray();
    private readonly IProcessor _cpu;
    private readonly IProcessorInstructionAccounting _session;
    private bool _disposed;
    internal long TotalInstructions => _session.TotalInstructions;
    internal long TotalCycles => _session.TotalCycles;

    internal ExecutionAccountingSession(IProcessor cpu)
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
