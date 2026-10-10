namespace Besm6.Runtime.Timing;

internal readonly record struct ArithmeticCommandHandle(object? Owner, ulong Sequence);
internal enum ArithmeticStageTransitionKind { OperandAccepted, OperandRejected, Started, Completed }
internal readonly record struct ArithmeticStageTransition(ArithmeticCommandHandle Command,
    uint Code, ArithmeticStageTransitionKind Kind, HardwareInstant Time,
    NormalizedArithmeticResult? Output, ArithmeticUnitFault? Fault);

internal sealed class ArithmeticStageObserverException(ArithmeticStageTransition transition, Exception cause)
    : Exception("Arithmetic stage observer failed after the transition.", cause)
{
    internal ArithmeticStageTransition Transition { get; } = transition;
}

/// <summary>
/// АУ — арифметическое устройство; БАК — буфер арифметических команд.
/// Owns the logical selection/PVR/SPOP sequence, using TO-3 §3.7's two
/// explicitly distinguished buffer-transfer phases. RPK remains an input signal;
/// IZOP is explicit by default, or owned by a supplied fixed completion sequence.
/// No average instruction duration or opcode-to-17-bit mapping is used here.
/// Operand data is sampled once at PVR, not when the command enters BAK.
/// The caller must route all changes to the owned unit through this controller.
/// </summary>
internal sealed class ArithmeticStageController
{
    private sealed class Entry(ArithmeticCommandHandle handle, uint code,
        Func<PreparedArithmeticOperation?> sampleOperand, bool ready, ArithmeticOperandRoute route,
        HardwareDuration? completionDelay)
    {
        internal readonly ArithmeticCommandHandle Handle = handle;
        internal readonly uint Code = code;
        internal readonly Func<PreparedArithmeticOperation?> SampleOperand = sampleOperand;
        internal bool Ready = ready;
        internal readonly ArithmeticOperandRoute Route = route;
        internal readonly HardwareDuration? CompletionDelay = completionDelay;
    }

    private readonly HardwareTimeline _timeline;
    private readonly ArithmeticUnitStages _unit;
    private readonly ArithmeticBufferTransferTiming _transfer;
    private readonly Queue<Entry> _waiting = new();
    private readonly Dictionary<ulong, Entry> _live = new();
    private Entry? _selected, _prepared, _active;
    private HardwareEventToken _acceptEvent, _startEvent;
    private HardwareEventToken _completionEvent;
    internal HardwareInstant? NextCompletionTime { get; private set; }
    private bool _transferElapsed, _changing;
    private ulong _sequence;
    internal Action<ArithmeticStageTransition>? Transitioned { get; set; }
    private Action<ArithmeticStageTransition>? _boundTransition;
    private object? _bindingOwner;
    private Func<bool>? _bindingCurrent;
    internal ArithmeticStageTransition? LastTransition { get; private set; }
    internal int QueuedCommands => _waiting.Count;
    internal ArithmeticCommandHandle? PreparedCommand => _prepared?.Handle;
    internal ArithmeticCommandHandle? ActiveCommand => _active?.Handle;

    internal ArithmeticStageController(HardwareTimeline timeline, ArithmeticUnitStages unit)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(unit);
        if (!ReferenceEquals(timeline, unit.Timeline))
            throw new ArgumentException("The controller and arithmetic stages must share one hardware calendar.", nameof(unit));
        _timeline = timeline;
        _unit = unit;
        _transfer = new(unit.Cycle);
        unit.AttachController();
    }

    private void Change(Action action)
    {
        if (_changing) throw new InvalidOperationException("Nested arithmetic control transition is not allowed.");
        _changing = true;
        try { action(); }
        finally { _changing = false; }
    }

    internal bool TryEnqueue(uint code, Func<PreparedArithmeticOperation> sampleOperand,
        bool operandReady, out ArithmeticCommandHandle command)
    {
        ArgumentNullException.ThrowIfNull(sampleOperand);
        if (_bindingOwner is not null) throw new InvalidOperationException("The CPU owns this arithmetic controller.");
        return TryEnqueueOperand(code, () => sampleOperand(), operandReady, out command);
    }

    internal bool TryBind(object owner, uint code, Func<PreparedArithmeticOperation?> sampleOperand,
        bool operandReady, Word48 accumulator, Word48 lowRegister,
        Action<ArithmeticStageTransition> transition, out ArithmeticCommandHandle command,
        ArithmeticOperandRoute route = ArithmeticOperandRoute.Buffered, HardwareDuration? completionDelay = null,
        Func<bool>? bindingCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(sampleOperand);
        if (!Enum.IsDefined(route)) throw new ArgumentOutOfRangeException(nameof(route));
        if (completionDelay is { Nanoseconds: 0 }) throw new ArgumentOutOfRangeException(nameof(completionDelay));
        if (_changing || _bindingOwner is not null || _live.Count != 0)
            throw new InvalidOperationException("CPU binding requires an idle unowned arithmetic controller.");
        // Validate before taking ownership or changing the cached register boundary.
        if ((code & ~ArithmeticCommandBuffer.CommandMask) != 0) throw new ArgumentOutOfRangeException(nameof(code));
        if (operandReady && route == ArithmeticOperandRoute.Buffered) _transfer.AcceptFromReadyBuffer(_timeline.Now);
        _unit.SynchronizeIdleRegisters(accumulator, lowRegister);
        _bindingOwner = owner;
        _bindingCurrent = bindingCurrent;
        _boundTransition = transition;
        try { return TryEnqueueOperand(code, sampleOperand, operandReady, out command, route, completionDelay); }
        catch { ReleaseBinding(owner); throw; }
    }

    internal void ReleaseBinding(object owner)
    {
        if (!ReferenceEquals(owner, _bindingOwner)) throw new InvalidOperationException("Foreign arithmetic binding.");
        Change(() =>
        {
            if (_live.Count != 0) _unit.DiscardLogicalRequest();
            _timeline.Cancel(_acceptEvent);
            _timeline.Cancel(_startEvent);
            CancelCompletion();
            _acceptEvent = _startEvent = default;
            _waiting.Clear();
            _live.Clear();
            _selected = _prepared = _active = null;
            _transferElapsed = false;
            _boundTransition = null;
            _bindingOwner = null;
            _bindingCurrent = null;
        });
    }

    private bool TryEnqueueOperand(uint code, Func<PreparedArithmeticOperation?> sampleOperand,
        bool operandReady, out ArithmeticCommandHandle command, ArithmeticOperandRoute route = ArithmeticOperandRoute.Buffered,
        HardwareDuration? completionDelay = null)
    {
        ArithmeticCommandHandle added = default;
        Change(() =>
        {
            ulong sequence = checked(_sequence + 1);
            // Validate the first transfer before admitting the command to BAK.
            if (_waiting.Count == 0 && operandReady && route == ArithmeticOperandRoute.Buffered) _transfer.AcceptFromReadyBuffer(_timeline.Now);
            if (!_unit.TryReceiveCommand(code)) return;
            added = new(this, sequence);
            var entry = new Entry(added, code, sampleOperand, operandReady, route, completionDelay);
            _waiting.Enqueue(entry);
            _live.Add(sequence, entry);
            _sequence = sequence;
            Progress();
        });
        command = added;
        return added.Owner is not null;
    }

    /// <summary>RPK may precede IZOP; it does not restart an elapsed transfer.</summary>
    internal void GrantCommandPermission() => Change(() => { _unit.GrantCommandPermission(); Progress(); });

    /// <summary>
    /// GBRCh releases a selected waiting transfer. For an unselected BAK slot,
    /// readiness is remembered; selecting it later uses the already-ready phase.
    /// </summary>
    internal bool TryReleaseBufferWait(ArithmeticCommandHandle command)
    {
        bool released = false;
        Change(() =>
        {
            if (!ReferenceEquals(command.Owner, this) || !_live.TryGetValue(command.Sequence, out var entry) ||
                entry.Route != ArithmeticOperandRoute.Buffered || entry.Ready ||
                ReferenceEquals(entry, _prepared) || ReferenceEquals(entry, _active)) return;
            if (ReferenceEquals(entry, _selected))
                ScheduleAcceptance(_transfer.AcceptAfterBufferWait(_timeline.Now));
            entry.Ready = true;
            released = true;
            Progress();
        });
        return released;
    }

    internal void Resume() => Change(Progress);

    /// <summary>
    /// Explicit PVRA/PVRM input-register acceptance (ПВРА/ПВРМ).
    /// The verified control sequence supplies this instant; BRCh transfer delays
    /// do not apply to an operand from the command address or index register.
    /// </summary>
    internal bool TryAcceptDirectOperand(ArithmeticCommandHandle command)
    {
        bool accepted = false;
        Change(() =>
        {
            if (_selected is not { Route: ArithmeticOperandRoute.Direct } entry ||
                entry.Handle != command || _prepared is not null || _unit.Interrupted || !_unit.CommandPermission) return;
            entry.Ready = true;
            _transferElapsed = true;
            Progress();
            accepted = true;
        });
        return accepted;
    }

    private void ScheduleAcceptance(HardwareInstant time)
    {
        _acceptEvent = _timeline.ScheduleAt(time, () => Change(() =>
        {
            _acceptEvent = default;
            _transferElapsed = true;
            Progress();
        }));
    }

    private void Progress()
    {
        if (_selected is null && _waiting.TryPeek(out var next))
        {
            if (next.Ready && next.Route == ArithmeticOperandRoute.Buffered)
                ScheduleAcceptance(_transfer.AcceptFromReadyBuffer(_timeline.Now));
            _selected = next;
            _transferElapsed = false;
        }
        if (_selected is { Ready: true } entry && _transferElapsed && _prepared is null &&
            !_unit.Interrupted && _unit.CommandPermission)
        {
            // A failing sampler leaves BAK and the PVR prerequisites unchanged.
            var operation = entry.SampleOperand();
            if (!operation.HasValue)
            {
                if (!_unit.TryRejectOperand())
                    throw new InvalidOperationException("Rejected arithmetic operand and command control disagree.");
                _waiting.Dequeue();
                _selected = null;
                _live.Remove(entry.Handle.Sequence);
                Progress();
                Publish(entry, ArithmeticStageTransitionKind.OperandRejected);
                return;
            }
            if (!_unit.TryAcceptOperand(operation.Value, true))
                throw new InvalidOperationException("Arithmetic command control and operand queue disagree.");
            _waiting.Dequeue();
            _selected = null;
            _prepared = entry;
            ScheduleStart();
            // The issue counter now selects the following BAK slot.
            Progress();
            Publish(entry, ArithmeticStageTransitionKind.OperandAccepted);
        }
        ScheduleStart();
    }

    private void ScheduleStart()
    {
        if (_prepared is null || _active is not null || _unit.Interrupted || _startEvent != default) return;
        _startEvent = _timeline.ScheduleAt(_timeline.Now, () => Change(() =>
        {
            _startEvent = default;
            if (_prepared is not { } entry || _active is not null) return;
            // Validate and register the owned deadline before changing the AU.
            // Overflow/registration failure leaves its prepared operand resumable.
            HardwareInstant? due = entry.CompletionDelay is { } delay ? _timeline.Now + delay : null;
            HardwareEventToken completion = default;
            if (due is { } time)
                completion = _timeline.ScheduleAt(time, () => Change(() =>
                {
                    if (_active?.Handle != entry.Handle || _completionEvent != completion) return;
                    _completionEvent = default;
                    NextCompletionTime = null;
                    // A reset/cancel may precede the driver's retirement boundary.
                    // Consume this stale signal without changing the AU output.
                    if (_bindingCurrent?.Invoke() == false) return;
                    CompleteActiveOperation();
                }));
            try
            {
                if (!_unit.TryStartOperation()) { _timeline.Cancel(completion); return; }
            }
            catch { _timeline.Cancel(completion); throw; }
            _completionEvent = completion;
            NextCompletionTime = due;
            _prepared = null;
            _active = entry;
            Progress();
            Publish(entry, ArithmeticStageTransitionKind.Started);
        }));
    }

    /// <summary>Explicit IZOP for operations without an owned fixed completion sequence.</summary>
    internal void CompleteOperation() => Change(() =>
    {
        if (_completionEvent != default)
            throw new InvalidOperationException("The automatic sequence owns this operation's IZOP.");
        CompleteActiveOperation();
    });

    private void CompleteActiveOperation()
    {
        if (_active is not { } entry) throw new InvalidOperationException("IZOP requires an active controller command.");
        uint code = _unit.CompleteOperation();
        if (code != entry.Code) throw new InvalidOperationException("Arithmetic completion code disagrees with its owner.");
        _active = null;
        _live.Remove(entry.Handle.Sequence);
        // Register successor start before notifying: an observer failure cannot lose it.
        Progress();
        Publish(entry, ArithmeticStageTransitionKind.Completed);
    }

    private void CancelCompletion()
    {
        _timeline.Cancel(_completionEvent);
        _completionEvent = default;
        NextCompletionTime = null;
    }

    internal void ApplyGeneralClearSignal() => Change(() =>
    {
        if (_bindingOwner is not null)
            throw new InvalidOperationException("A CPU-bound request requires its owner's cancellation boundary.");
        _timeline.Cancel(_acceptEvent);
        _timeline.Cancel(_startEvent);
        CancelCompletion();
        _acceptEvent = _startEvent = default;
        _waiting.Clear();
        _live.Clear();
        _selected = _prepared = _active = null;
        _transferElapsed = false;
        _unit.ApplyGeneralClearSignal();
    });

    private void Publish(Entry entry, ArithmeticStageTransitionKind kind)
    {
        NormalizedArithmeticResult? output = kind == ArithmeticStageTransitionKind.Completed &&
            _unit.TryReadOutput(out var result) ? result : null;
        var transition = new ArithmeticStageTransition(entry.Handle, entry.Code, kind,
            _timeline.Now, output, _unit.Fault);
        LastTransition = transition;
        try { _boundTransition?.Invoke(transition); Transitioned?.Invoke(transition); }
        catch (Exception cause) { throw new ArithmeticStageObserverException(transition, cause); }
    }
}
