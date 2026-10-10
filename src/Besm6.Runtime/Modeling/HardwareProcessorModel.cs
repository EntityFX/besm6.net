using Besm6.Runtime.Timing;

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
    private readonly Processor _processor;
    private ArithmeticUnitStages? _arithmeticUnitStages;
    private ArithmeticStageController? _arithmeticController;
    private ArithmeticInterruptionPort? _arithmeticInterruptions;
    internal HardwareTimeline Timeline { get; } = new();

    internal HardwareProcessorModel(Processor processor, PhysicalMemory? physicalMemory = null, MappedMemoryBackend? mappedMemory = null)
    { _processor = processor; _physicalMemory = physicalMemory; _mappedMemory = mappedMemory; }

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
        var unit = new ArithmeticUnitStages(Timeline, cycle, _processor.GetA(), _processor.GetY(), errorPolicy, interruptions);
        _arithmeticInterruptions = interruptions;
        return _arithmeticUnitStages = unit;
    }

}
