namespace Besm6.Architecture.Machine;

/// <summary>Observable architectural state. Execution bookkeeping is private to the CPU.</summary>
public interface IProcessorState : IReadOnlyRegisters
{
    bool IsRightHalf { get; }
    int InterceptCount { get; }
    uint InterceptAddress { get; }
}
