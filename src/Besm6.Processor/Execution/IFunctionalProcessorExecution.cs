using Besm6.Architecture.Machine;

namespace Besm6.Processor.Execution;

/// <summary>The immediate execution path and its bounded batching contract.</summary>
internal interface IFunctionalProcessorExecution : IProcessor, IExecutionGuard, IInstructionAccountingSource
{
    bool HasPreparedInstruction { get; }
    bool CanExecuteUnobservedBlock { get; }
    bool ExecuteUnobservedBlock(int count, ref long completed, ref ulong tick);
}
