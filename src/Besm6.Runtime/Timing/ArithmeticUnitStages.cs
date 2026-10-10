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
/// Error control is optional for the developing driver; default construction
/// retains the previous Dubna-compatible overflow flag. The physical mode uses
/// the shared arithmetic cause before OvfDisable delivery suppression.
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
    private bool _invalidDivisorIndicated;
    private bool _controllerAttached;
    private readonly List<HardwareEventToken> _divisionEvents = new();

    internal HardwareDuration Cycle { get; }
    internal HardwareTimeline Timeline => _timeline;
    internal int QueuedCommands => _control.Commands.Count;
    internal uint? ActiveCommand => _control.ActiveCommand;
    internal uint? PreparedCommand => _control.PreparedCommand;
    internal bool CommandPermission => _control.CommandPermission;
    internal HardwareInstant? StartedAt { get; private set; }
    internal HardwareInstant? CompletedAt { get; private set; }
    internal HardwareInstant? DivisorCheckedAt { get; private set; }
    internal ArithmeticUnitFault? Fault { get; private set; }
    internal ArithmeticErrorControl? Errors { get; }
    internal DivisionControl? Division { get; private set; }
    internal bool Interrupted => Errors?.BlocksNextOperation ?? Fault.HasValue;

    internal void AttachController()
    {
        if (_controllerAttached || QueuedCommands != 0 || PreparedCommand.HasValue || ActiveCommand.HasValue)
            throw new InvalidOperationException("The arithmetic controller requires an unowned idle command pipeline.");
        _controllerAttached = true;
    }

    internal ArithmeticUnitStages(HardwareTimeline timeline, HardwareDuration cycle,
        Word48 initialAccumulator, Word48 initialLowRegister, ArithmeticErrorPolicy? errorPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle), "A positive even cycle is required.");
        _timeline = timeline;
        Cycle = cycle;
        Errors = errorPolicy is { } policy ? new(timeline, cycle, policy) : null;
        _completedResult = new(initialAccumulator, initialLowRegister, false, false);
    }

    internal bool TryReceiveCommand(uint code) => _control.Commands.TryReceive(code);

    internal uint[] SnapshotCommands() => _control.Commands.Snapshot();

    internal void GrantCommandPermission() => _control.GrantCommandPermission();

    internal bool TryRejectOperand() => _control.TryRejectOperand();

    internal void SynchronizeIdleRegisters(Word48 accumulator, Word48 lowRegister)
    {
        if (Errors is not null || QueuedCommands != 0 || PreparedCommand.HasValue || ActiveCommand.HasValue)
            throw new InvalidOperationException("Register synchronization requires idle Dubna-compatible stages.");
        _completedResult = new(accumulator, lowRegister, false, false);
        _outputReady = true;
        Fault = null;
    }

    /// <summary>Abandon a host-owned logical request; preserve completed A/Y and external events.</summary>
    internal void DiscardLogicalRequest()
    {
        if (Errors is not null)
            throw new InvalidOperationException("Physical error control requires its documented cancellation sequence.");
        foreach (var token in _divisionEvents) _timeline.Cancel(token);
        _divisionEvents.Clear();
        Division?.Stop();
        Division = null;
        _control.DiscardLogicalRequest();
        _preparedOperation = null;
        _calculationFailure = null;
        _calculatedResult = default;
        _outputReady = true;
        _divisionInProgress = _invalidDivisorIndicated = false;
        StartedAt = CompletedAt = DivisorCheckedAt = null;
        Fault = null;
    }

    internal bool TryAcceptOperand(PreparedArithmeticOperation operation, bool operandReady)
    {
        if (Interrupted || !_control.TryAcceptOperand(operandReady)) return false;
        _preparedOperation = operation;
        return true;
    }

    internal bool TryStartOperation()
    {
        if (Interrupted || ActiveCommand.HasValue || !_preparedOperation.HasValue || !_outputReady) return false;
        var operation = _preparedOperation.Value;
        NormalizedArithmeticResult result = default;
        ProcessorException? failure = null;
        HardwareInstant? divisorCheckAt = null;
        HardwareInstant? invalidDivisorAt = null;
        if (operation.IsDivision)
            divisorCheckAt = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds * 2 + Cycle.Nanoseconds / 2));
        try { result = Errors is null ? operation.Evaluate(_completedResult.A, _completedResult.Y) :
                operation.EvaluateForErrorControl(_completedResult.A, _completedResult.Y); }
        catch (ProcessorException cause) when (operation.IsDivision)
        {
            // Calculation knows the cause early; the physical indication is later.
            failure = cause;
            invalidDivisorAt = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds * 3));
        }
        HardwareEventToken checkToken = default;
        HardwareEventToken indicationToken = default;
        try
        {
            if (divisorCheckAt is { } checkAt)
                checkToken = _timeline.ScheduleAt(checkAt, () => DivisorCheckedAt = _timeline.Now);
            if (invalidDivisorAt is { } due)
                indicationToken = _timeline.ScheduleAt(due, () =>
                {
                    _invalidDivisorIndicated = true;
                    Errors?.IndicateInvalidDivisor();
                    Fault = new(ArithmeticUnitFaultKind.InvalidDivisor, _timeline.Now, failure);
                });
            Errors?.BeginOperation();
        }
        catch
        {
            // Keep the prepared operation intact if registering its events fails.
            _timeline.Cancel(checkToken);
            _timeline.Cancel(indicationToken);
            throw;
        }
        if (!_control.TryStartOperation())
            throw new InvalidOperationException("Prepared arithmetic data and command control disagree.");
        _preparedOperation = null;
        _calculatedResult = result;
        _calculationFailure = failure;
        _outputReady = false;
        _divisionInProgress = operation.IsDivision;
        _invalidDivisorIndicated = false;
        _divisionEvents.Clear();
        if (checkToken != default) _divisionEvents.Add(checkToken);
        if (indicationToken != default) _divisionEvents.Add(indicationToken);
        DivisorCheckedAt = null;
        StartedAt = _timeline.Now;
        Division = operation.IsDivision ? new(_timeline, Cycle,
            (_completedResult.A.Value & (1UL << 40)) != 0, operation.DivisionOperandNegative) : null;
        return true;
    }

    /// <summary>Called by the microsequence's IZOP signal, not by a tabular timer.</summary>
    internal uint CompleteOperation()
    {
        if (_divisionInProgress && !DivisorCheckedAt.HasValue)
            throw new InvalidOperationException("Division completion precedes its divisor check.");
        if (_calculationFailure is not null && !_invalidDivisorIndicated)
            throw new InvalidOperationException("Invalid-divisor completion precedes its error indication.");
        // Error control validates/schedules its release before retiring the command.
        if (ActiveCommand is null) throw new InvalidOperationException("IZOP requires an active operation.");
        if (Division?.HasPendingMainRemainder == true)
            throw new InvalidOperationException("Division completion precedes a sampled main remainder.");
        Errors?.CompleteOperation(_calculationFailure is null && _calculatedResult.Overflow);
        uint command = _control.CompleteOperation();
        Division?.Stop();
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
    /// Receives the general-clear signal at the microsequence's chosen instant.
    /// Clears A/Y and command rings; does not infer the eight-cycle pulse sequence.
    /// </summary>
    internal void ApplyGeneralClearSignal()
    {
        foreach (var token in _divisionEvents) _timeline.Cancel(token);
        _divisionEvents.Clear();
        Division?.Stop();
        Division = null;
        Errors?.ApplyGeneralClearSignal();
        _control.ApplyGeneralClearSignal();
        _preparedOperation = null;
        _calculationFailure = null;
        _calculatedResult = default;
        _completedResult = default;
        _outputReady = true;
        _divisionInProgress = _invalidDivisorIndicated = false;
        StartedAt = CompletedAt = DivisorCheckedAt = null;
        Fault = null;
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
