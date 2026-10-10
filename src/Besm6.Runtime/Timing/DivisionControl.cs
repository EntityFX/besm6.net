namespace Besm6.Runtime.Timing;

internal enum DivisionAction { ShiftOnly, AddDivisor, SubtractDivisor }

internal readonly record struct DivisionControlSample(
    HardwareInstant Time, HardwareInstant MainRemainderAvailableAt,
    byte CarryLeadingBits, byte SumLeadingBits, bool PreviousActionWasAdd,
    DivisionAction Action);

/// <summary>
/// АУ: division control registers (РСУД/РПУД), TO-3 §4.16, table 4.4.
/// The caller supplies the partially reduced leading bits, not the sign of a
/// fully reduced host remainder. A control sample precedes the corresponding
/// main two-row remainder by half a cycle (printed sheets 96–101).
/// This models control history and visibility; it neither generates the full
/// remainder nor infers counter/quotient formation or an IZOP deadline.
/// </summary>
internal sealed class DivisionControl
{
    private readonly HardwareTimeline _timeline;
    private readonly HardwareDuration _cycle;
    private readonly bool _divisorNegative;
    private HardwareEventToken _arrival;
    private HardwareInstant? _nextSampleAt;
    private bool _stopped;
    private bool _sampleShifted;

    internal bool PreviousActionWasAdd { get; private set; }
    internal DivisionControlSample? LatestSample { get; private set; }
    internal DivisionControlSample? AvailableMainRemainder { get; private set; }
    internal bool HasPendingMainRemainder => _arrival != default;
    internal DivisionQuotientParts QuotientParts { get; private set; }

    internal DivisionControl(HardwareTimeline timeline, HardwareDuration cycle,
        bool dividendNegative, bool divisorNegative)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle));
        _timeline = timeline;
        _cycle = cycle;
        _divisorNegative = divisorNegative;
        // First action has no history: TO-3 printed sheet 100 sets ЗУ+ЧВР
        // only for a negative dividend and a positive divisor.
        PreviousActionWasAdd = dividendNegative && !divisorNegative;
    }

    internal static DivisionAction Decode(byte carryLeadingBits, byte sumLeadingBits,
        bool previousActionWasAdd, bool divisorNegative)
    {
        if (carryLeadingBits > 3) throw new ArgumentOutOfRangeException(nameof(carryLeadingBits));
        if (sumLeadingBits > 7) throw new ArgumentOutOfRangeException(nameof(sumLeadingBits));
        // First two rows of table 4.4 retain the previous +/- action.
        if (sumLeadingBits < 4)
            return previousActionWasAdd ? DivisionAction.AddDivisor : DivisionAction.SubtractDivisor;
        // Four indeterminate combinations: shift without adding or subtracting.
        if ((carryLeadingBits, sumLeadingBits) is (2, 5) or (1, 6) or (3, 4) or (0, 7))
            return DivisionAction.ShiftOnly;
        bool negativeCategory = (carryLeadingBits, sumLeadingBits) is (1, 7) or (3, 5)
            || (carryLeadingBits >= 2 && sumLeadingBits >= 6);
        return negativeCategory == divisorNegative ? DivisionAction.AddDivisor : DivisionAction.SubtractDivisor;
    }

    internal DivisionControlSample Sample(byte carryLeadingBits, byte sumLeadingBits)
    {
        if (_stopped) throw new InvalidOperationException("Division control has stopped.");
        var action = Decode(carryLeadingBits, sumLeadingBits, PreviousActionWasAdd, _divisorNegative);
        if (_nextSampleAt is { } next && _timeline.Now.Nanoseconds < next.Nanoseconds)
            throw new InvalidOperationException("Division control samples are one cycle apart.");
        // Calculate both deadlines before changing history or registering an event.
        var availableAt = _timeline.Now + new HardwareDuration(_cycle.Nanoseconds / 2);
        var nextSampleAt = _timeline.Now + _cycle;
        var sample = new DivisionControlSample(_timeline.Now, availableAt,
            carryLeadingBits, sumLeadingBits, PreviousActionWasAdd, action);
        var token = _timeline.ScheduleAt(availableAt, () =>
        {
            _arrival = default;
            AvailableMainRemainder = sample;
        });
        _arrival = token;
        _nextSampleAt = nextSampleAt;
        LatestSample = sample;
        _sampleShifted = false;
        // TO-3 printed sheet 100: shift-only clears the preceding-add latch too.
        PreviousActionWasAdd = action == DivisionAction.AddDivisor;
        return sample;
    }

    /// <summary>
    /// Receives the quotient-register shift pulse for the latest decoded action.
    /// Its physical phase is supplied by the caller, not inferred from arrival
    /// of the main remainder. At most one shift is accepted for a sample.
    /// </summary>
    internal void ShiftQuotientParts()
    {
        if (_stopped || LatestSample is not { } sample || _sampleShifted)
            throw new InvalidOperationException("A quotient shift requires an unconsumed active control sample.");
        var next = QuotientParts.Shift(sample.Action);
        QuotientParts = next;
        _sampleShifted = true;
    }

    internal void Stop()
    {
        _timeline.Cancel(_arrival);
        _arrival = default;
        _stopped = true;
    }
}
