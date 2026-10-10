using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

/// <summary>
/// Physical processor model: owns its nanosecond timeline and staged АУ
/// (арифметическое устройство). The Processor is the machine's shared register
/// and arithmetic source. This component never calls Step or performs hosted
/// execution. An automatic physical instruction driver is still being built.
/// </summary>
internal sealed class HardwareProcessorModel
{
    private readonly Processor _processor;
    private ArithmeticUnitStages? _arithmeticUnitStages;
    internal HardwareTimeline Timeline { get; } = new();

    internal HardwareProcessorModel(Processor processor) => _processor = processor;

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
        return _arithmeticUnitStages = new(Timeline, cycle, _processor.GetA(), _processor.GetY(), errorPolicy);
    }

}
