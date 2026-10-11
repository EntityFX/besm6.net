namespace Besm6.Runtime;

/// <summary>
/// Table-cycle accounting for one hosted run. These are instruction notifications,
/// not instruction-clock ticks; exceptional extracode completion preserves its
/// existing notification/counting boundary.
/// </summary>
internal sealed class ExecutionAccountingSession
{
    internal long TotalInstructions { get; private set; }
    internal long TotalCycles { get; private set; }

    internal void Observe(Opcode opcode)
    {
        TotalInstructions++;
        TotalCycles += Besm6Timing.CyclesOf(opcode);
    }
}
