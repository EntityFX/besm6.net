using Besm6.Runtime.Execution;
using Besm6.Runtime.Loading;

namespace Besm6.Runtime.Machine;

public sealed record MachineExecutionResult(LoadResult Outcome, ExecutionStatistics? Statistics);
