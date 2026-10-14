using Besm6.Processor.Execution;
using Besm6.Architecture.Machine;
using Besm6.Processor.Memory;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Arithmetic;
using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Runtime.Modeling;

/// <summary>
/// Physical processor model: owns its nanosecond timeline and staged АУ
/// (арифметическое устройство). The Processor is the machine's shared register
/// and arithmetic source. Completion is driven by explicit stage permission,
/// through the shared prepared-command protocol, never by Processor.Step or an
/// average opcode cost. Automatic decoding of stage timings is still being built.
/// </summary>
internal sealed partial class HardwareProcessorModel
{
    private readonly IPreparedProcessorExecution _processor;
    private ArithmeticUnitStages? _arithmeticUnitStages;
    private ArithmeticStageController? _arithmeticController;
    private ArithmeticInterruptionPort? _arithmeticInterruptions;
    internal HardwareTimeline Timeline { get; } = new();
    private readonly Func<bool> _fetchBoundary, _dataBoundary, _completionBoundary, _retirementBoundary;
    private readonly Action<HardwareBufferedRead> _fetchCompleted, _dataReadCompleted;
    private readonly Action _dataStoreCompleted;

    internal HardwareProcessorModel(IPreparedProcessorExecution processor, PhysicalMemory? physicalMemory = null, MappedMemoryBackend? mappedMemory = null)
    {
        _processor = processor; _physicalMemory = physicalMemory; _mappedMemory = mappedMemory;
        _fetchBoundary = () => _timedFetchReply.HasValue || _cancellationRequested || !HasPendingInstruction;
        _dataBoundary = () => _dataMemoryReplyReady || _cancellationRequested || !HasPendingInstruction;
        _completionBoundary = () => _completionReady || _cancellationRequested || !HasPendingInstruction;
        _retirementBoundary = () => _automaticRetirementReady || _cancellationRequested || !HasPendingInstruction;
        _fetchCompleted = reply => _timedFetchReply = reply;
        _dataReadCompleted = reply =>
        { _dataMemoryReply = reply.Word; _dataMemoryFailure = reply.Failure; _dataMemoryReplyReady = true; };
        _dataStoreCompleted = () => { _dataMemoryReply = default; _dataMemoryReplyReady = true; };
    }

    internal ArithmeticStageController CreateArithmeticController(HardwareDuration cycle,
        ArithmeticErrorPolicy? errorPolicy = null)
    {
        var unit = CreateArithmeticUnitStages(cycle, errorPolicy);
        return _arithmeticController ??= new(Timeline, unit);
    }

    internal ArithmeticUnitStages CreateArithmeticUnitStages(HardwareDuration cycle,
    ArithmeticErrorPolicy? errorPolicy = null)
    {
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
        throw new ArgumentOutOfRangeException(nameof(cycle), "A positive even cycle is required.");
        if (_arithmeticUnitStages is { } existing)
        {
            if (existing.Cycle != cycle || existing.Errors?.Policy != errorPolicy)
            throw new InvalidOperationException("This machine's arithmetic configuration is already selected.");
            return existing;
        }
        var interruptions = errorPolicy.HasValue ? new ArithmeticInterruptionPort(_processor.Supervisor) : null;
        var unit = new ArithmeticUnitStages(Timeline, cycle, _processor.A, _processor.Y, errorPolicy, interruptions);
        _arithmeticInterruptions = interruptions;
        return _arithmeticUnitStages = unit;
    }

}
