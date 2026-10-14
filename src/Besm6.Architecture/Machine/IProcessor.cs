using Besm6.Architecture.Supervisor;

namespace Besm6.Architecture.Machine;

/// <summary>Instruction execution and architectural observation, independently of editing or diagnostics.</summary>
public interface IProcessor : IReadOnlyRegisters
{
    IProcessorState State { get; }
    bool RightInstruction { get; }
    ProcessorProfile Profile { get; }
    ISupervisorControl? Supervisor { get; }
    bool LastStepCompleted { get; }
    void Reset();
    void StartAt(uint address);
    bool Step();
}
