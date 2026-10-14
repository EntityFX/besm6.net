namespace Besm6.Architecture.Machine;

/// <summary>Scoped opcode accounting, separate from execution and trace subscriptions.</summary>
public interface IInstructionAccountingSource
{
    IProcessorInstructionAccounting? InstructionAccounting { get; }
    IProcessorInstructionAccounting BeginInstructionAccounting(ReadOnlySpan<int> cycles, int defaultCycles);
    void EndInstructionAccounting(IProcessorInstructionAccounting session);
}
