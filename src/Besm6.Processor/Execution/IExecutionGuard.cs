namespace Besm6.Processor.Execution;

/// <summary>Single-machine ownership controls; unavailable to public CPU clients.</summary>
internal interface IExecutionGuard
{
    bool ExecutionProhibited { get; set; }
    bool PreparedExecutionOnly { get; set; }
}
