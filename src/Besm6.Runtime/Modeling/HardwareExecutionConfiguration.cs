using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

/// <summary>
/// Explicit logical configuration of UU (УУ) retirement and AU (АУ) completion.
/// Control duration is a minimum from decoded acceptance, overlapping transfers.
/// Arithmetic durations run from SPOP; absent variable durations are errors.
/// These caller-selected values do not claim a reconstruction of microcode.
/// </summary>
internal sealed class HardwareExecutionConfiguration
{
    internal MramPortConfiguration Memory { get; }
    internal HardwareDuration BufferHit { get; }
    internal HardwareDuration ArithmeticCycle { get; }
    private readonly Dictionary<Opcode, HardwareDuration> _control, _arithmetic;

    internal HardwareExecutionConfiguration(MramPortConfiguration memory, HardwareDuration bufferHit,
        HardwareDuration arithmeticCycle, IReadOnlyDictionary<Opcode, HardwareDuration> control,
        IReadOnlyDictionary<Opcode, HardwareDuration>? arithmetic = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (arithmeticCycle.Nanoseconds == 0 || (arithmeticCycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(arithmeticCycle));
        Memory = memory; BufferHit = bufferHit; ArithmeticCycle = arithmeticCycle;
        _control = Copy(control, false);
        _arithmetic = Copy(arithmetic ?? new Dictionary<Opcode, HardwareDuration>(), true);
    }

    private static Dictionary<Opcode, HardwareDuration> Copy(IReadOnlyDictionary<Opcode, HardwareDuration> source, bool positive)
    {
        var copy = new Dictionary<Opcode, HardwareDuration>();
        foreach (var pair in source)
        {
            if (!Enum.IsDefined(pair.Key) || (positive && pair.Value.Nanoseconds == 0))
                throw new ArgumentException("Timing entries require a defined opcode and positive AU duration.", nameof(source));
            copy.Add(pair.Key, pair.Value);
        }
        return copy;
    }

    internal HardwareDuration ControlDuration(Opcode opcode) => _control.TryGetValue(opcode, out var delay) ? delay :
        throw new NotSupportedException($"No explicit UU duration configured for {opcode}.");

    internal HardwareDuration ArithmeticDuration(Opcode opcode) => _arithmetic.TryGetValue(opcode, out var delay) ? delay :
        ArithmeticFixedCompletionTiming.Duration(opcode, ArithmeticCycle);
}
