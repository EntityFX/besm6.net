namespace Besm6.Runtime;

public sealed record MachineExecutionResult(LoadResult Outcome, ExecutionStatistics? Statistics);
