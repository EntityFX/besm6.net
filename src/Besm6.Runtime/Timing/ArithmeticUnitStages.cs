namespace Besm6.Runtime.Timing;

internal enum ArithmeticUnitFaultKind { InvalidDivisor, Overflow }

/// <summary>A latched guest cause, never an exception thrown from the calendar.</summary>
internal readonly record struct ArithmeticUnitFault(
    ArithmeticUnitFaultKind Kind, HardwareInstant Time, ProcessorException? Cause);

/// <summary>
/// АУ — арифметическое устройство. Data attached to TO-3 §3.2 control transitions.
/// Inputs are accepted at PVR and evaluated at SPOP using the preceding output;
/// completion makes the common arithmetic result available at IZOP (§3.22).
/// The caller supplies command decoding and completion/RPK instants. No average
/// instruction cost, physical accumulator register layout, or CPU retirement is
/// inferred here. The §3.28 divisor check is at 2.5 supplied cycles from SPOP;
/// invalid-divisor UDO follows at three cycles, rather than at host evaluation.
/// Error-policy recovery and input control checking belong to the subsequent driver.
/// </summary>
internal sealed class ArithmeticUnitStages
{
    private readonly HardwareTimeline _timeline;
    private readonly ArithmeticPipelineControl _control = new();
    private PreparedArithmeticOperation? _preparedOperation;
    private NormalizedArithmeticResult _calculatedResult;
    private ProcessorException? _calculationFailure;
    private NormalizedArithmeticResult _completedResult;
    private bool _outputReady = true;
    private bool _divisionInProgress;

    internal HardwareDuration Cycle { get; }
    internal int QueuedCommands => _control.Commands.Count;
    internal uint? ActiveCommand => _control.ActiveCommand;
    internal uint? PreparedCommand => _control.PreparedCommand;
    internal bool CommandPermission => _control.CommandPermission;
    internal HardwareInstant? StartedAt { get; private set; }
    internal HardwareInstant? CompletedAt { get; private set; }
    internal HardwareInstant? DivisorCheckedAt { get; private set; }
    internal ArithmeticUnitFault? Fault { get; private set; }
    internal bool Interrupted => Fault.HasValue;

    internal ArithmeticUnitStages(HardwareTimeline timeline, HardwareDuration cycle,
        Word48 initialAccumulator, Word48 initialLowRegister)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle), "A positive even cycle is required.");
        _timeline = timeline;
        Cycle = cycle;
        _completedResult = new(initialAccumulator, initialLowRegister, false, false);
    }

    internal bool TryReceiveCommand(uint code) => _control.Commands.TryReceive(code);

    internal uint[] SnapshotCommands() => _control.Commands.Snapshot();

    internal void GrantCommandPermission() => _control.GrantCommandPermission();

    internal bool TryAcceptOperand(PreparedArithmeticOperation operation, bool operandReady)
    {
        if (Interrupted || !_control.TryAcceptOperand(operandReady)) return false;
        _preparedOperation = operation;
        return true;
    }

    internal bool TryStartOperation()
    {
        if (Interrupted || ActiveCommand.HasValue || !_preparedOperation.HasValue) return false;
        var operation = _preparedOperation.Value;
        NormalizedArithmeticResult result = default;
        ProcessorException? failure = null;
        HardwareInstant? divisorCheckAt = null;
        HardwareInstant? invalidDivisorAt = null;
        if (operation.IsDivision)
            divisorCheckAt = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds * 2 + Cycle.Nanoseconds / 2));
        try { result = operation.Evaluate(_completedResult.A, _completedResult.Y); }
        catch (ProcessorException cause) when (operation.IsDivision)
        {
            // Calculation knows the cause early; the physical indication is later.
            failure = cause;
            invalidDivisorAt = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds * 3));
        }
        HardwareEventToken checkToken = default;
        try
        {
            if (divisorCheckAt is { } checkAt)
                checkToken = _timeline.ScheduleAt(checkAt, () => DivisorCheckedAt = _timeline.Now);
            if (invalidDivisorAt is { } due)
                _timeline.ScheduleAt(due, () =>
                    Fault = new(ArithmeticUnitFaultKind.InvalidDivisor, _timeline.Now, failure));
        }
        catch
        {
            // Keep the prepared operation intact if registering its events fails.
            _timeline.Cancel(checkToken);
            throw;
        }
        if (!_control.TryStartOperation())
            throw new InvalidOperationException("Prepared arithmetic data and command control disagree.");
        _preparedOperation = null;
        _calculatedResult = result;
        _calculationFailure = failure;
        _outputReady = false;
        _divisionInProgress = operation.IsDivision;
        DivisorCheckedAt = null;
        StartedAt = _timeline.Now;
        return true;
    }

    /// <summary>Called by the microsequence's IZOP signal, not by a tabular timer.</summary>
    internal uint CompleteOperation()
    {
        if (_divisionInProgress && !DivisorCheckedAt.HasValue)
            throw new InvalidOperationException("Division completion precedes its divisor check.");
        if (_calculationFailure is not null && !Fault.HasValue)
            throw new InvalidOperationException("Invalid-divisor completion precedes its error indication.");
        uint command = _control.CompleteOperation();
        _divisionInProgress = false;
        CompletedAt = _timeline.Now;
        if (_calculationFailure is null)
        {
            _completedResult = _calculatedResult;
            _outputReady = true;
            if (_calculatedResult.Overflow)
                Fault = new(ArithmeticUnitFaultKind.Overflow, _timeline.Now, null);
        }
        return command;
    }

    /// <summary>
    /// Logical output with the rounding overlay; the unrounded value remains
    /// distinct. This does not assert the time of physical low-bit storage.
    /// </summary>
    internal bool TryReadOutput(out NormalizedArithmeticResult result)
    {
        result = _outputReady ? _completedResult : default;
        return _outputReady;
    }
}
