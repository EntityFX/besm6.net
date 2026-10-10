using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

/// <summary>A model-owned identity; the underlying CPU lease never escapes.</summary>
internal readonly struct HardwareInstructionHandle
{
    private readonly object? _owner;
    internal ulong Sequence { get; }
    internal uint Address { get; }
    internal bool RightHalf { get; }
    internal DecodedInstruction? Instruction { get; }
    internal HardwareInstant PreparedAt { get; }

    internal HardwareInstructionHandle(object owner, ulong sequence,
        in InstructionExecutor.PreparedInstruction instruction, HardwareInstant time)
    {
        _owner = owner;
        Sequence = sequence;
        Address = instruction.Address;
        RightHalf = instruction.RightHalf;
        Instruction = instruction.Instruction;
        PreparedAt = time;
    }

    internal bool BelongsTo(object owner, ulong sequence) =>
        ReferenceEquals(_owner, owner) && Sequence == sequence;
}

internal enum HardwareInstructionStatus { Completed, GuestFault, Cancelled, HostFailure }

internal readonly record struct HardwareInstructionOutcome(HardwareInstructionHandle Instruction,
    HardwareInstant Time, HardwareInstructionStatus Status, bool Stopped);

internal sealed class HardwareExecutionObserverException : Exception
{
    internal HardwareInstructionOutcome Outcome { get; }
    internal Exception? ExecutionFailure { get; }
    internal HardwareExecutionObserverException(HardwareInstructionOutcome outcome, Exception cause, Exception? executionFailure)
        : base("Hardware instruction observer failed after the execution boundary.", cause)
    { Outcome = outcome; ExecutionFailure = executionFailure; }
}

internal sealed partial class HardwareProcessorModel
{
    private InstructionExecutor.PreparedInstruction? _pendingInstruction;
    private HardwareInstructionHandle _pendingHandle;
    private HardwareEventToken _readyEvent;
    private bool _completionReady;
    private bool _cancellationRequested;
    private bool _publishingInstruction;
    private bool _capturingOperand;
    private ulong _sequence;
    internal bool IsDriving { get; private set; }
    internal Action<bool>? ExecutionStateChanged { get; set; }
    internal Action<HardwareInstructionOutcome>? InstructionCompleted { get; set; }
    internal ulong IssuedInstructions { get; private set; }
    internal ulong CompletedInstructions { get; private set; }
    internal ulong CancelledInstructions { get; private set; }
    internal HardwareInstructionOutcome? LastOutcome { get; private set; }
    internal bool HasPendingInstruction => _pendingInstruction is { } instruction &&
        _processor.IsPreparedInstructionActive(in instruction);

    private void EnsureDriverAllowed()
    {
        if (IsDriving || _capturingOperand || Timeline.IsAdvancing || _processor.ExecutionProhibited)
            throw new InvalidOperationException("Nested hardware processor execution is not allowed.");
    }

    private void BeginDriving()
    {
        EnsureDriverAllowed();
        IsDriving = true;
        try { ExecutionStateChanged?.Invoke(true); }
        catch { IsDriving = false; throw; }
    }

    private void EndDriving()
    {
        IsDriving = false;
        ExecutionStateChanged?.Invoke(false);
    }

    internal HardwareInstructionHandle PrepareNextInstruction()
    {
        BeginDriving();
        try
        {
            RetireInvalidatedPreparation();
            if (_pendingInstruction.HasValue || _processor.HasPreparedInstruction)
                throw new InvalidOperationException("A CPU command is already prepared.");
            // Device/control events at the current instant precede the next fetch.
            Timeline.AdvanceTo(Timeline.Now);
            ulong sequence = checked(_sequence + 1);
            var instruction = _processor.PrepareInstruction();
            _pendingInstruction = instruction;
            _pendingHandle = new(this, sequence, in instruction, Timeline.Now);
            _sequence = sequence;
            IssuedInstructions++;
            return _pendingHandle;
        }
        finally { EndDriving(); }
    }

    private bool IsCurrent(in HardwareInstructionHandle instruction) =>
        instruction.BelongsTo(this, _pendingHandle.Sequence) && HasPendingInstruction;

    /// <summary>
    /// The controller supplies the instant of its completion permission. This is
    /// an explicit signal, not a substitute for operand-dependent AU/MRAM timing.
    /// Rescheduling first registers the new signal, preserving the old one on error.
    /// </summary>
    internal HardwareEventToken ScheduleCompletionAt(HardwareInstructionHandle instruction, HardwareInstant time)
    {
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested)
            throw new InvalidOperationException("There is no waiting model instruction to schedule.");
        HardwareEventToken signal = default;
        signal = Timeline.ScheduleAt(time, () =>
        {
            if (_readyEvent == signal) _readyEvent = default;
            TryIndicateCompletionReady(in instruction);
        });
        Timeline.Cancel(_readyEvent);
        _readyEvent = signal;
        return signal;
    }

    // АУ/УУ (арифметическое устройство/устройство управления) may supply this
    // signal inside a calendar callback; the CPU itself runs only after it returns.
    internal bool TryIndicateCompletionReady(in HardwareInstructionHandle instruction)
    {
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested || _capturingOperand ||
            _processor.RequiresArithmeticResult(_pendingInstruction!.Value)) return false;
        _completionReady = true;
        return true;
    }

    internal PreparedArithmeticOperation? CaptureArithmeticOperand(HardwareInstructionHandle instruction)
    {
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested || _publishingInstruction ||
            _capturingOperand || (_processor.ExecutionProhibited && !Timeline.IsAdvancing))
            throw new InvalidOperationException("No waiting model command can accept this operand.");
        bool prohibited = Timeline.AdvancementProhibited;
        _capturingOperand = true;
        Timeline.AdvancementProhibited = true;
        try
        {
            ExecutionStateChanged?.Invoke(true);
            return _processor.CaptureArithmeticOperand(_pendingInstruction!.Value);
        }
        finally
        {
            Timeline.AdvancementProhibited = prohibited;
            _capturingOperand = false;
            ExecutionStateChanged?.Invoke(IsDriving);
        }
    }

    internal bool TryIndicateArithmeticCompletion(HardwareInstructionHandle instruction,
        NormalizedArithmeticResult? result, ProcessorException? failure = null)
    {
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested || _publishingInstruction ||
            _capturingOperand || (_processor.ExecutionProhibited && !Timeline.IsAdvancing)) return false;
        _processor.SupplyArithmeticResult(_pendingInstruction!.Value, result, failure);
        _completionReady = true;
        return true;
    }

    internal bool TryRequestCancellation(in HardwareInstructionHandle instruction)
    {
        if (!IsCurrent(in instruction) || _cancellationRequested || _publishingInstruction || _capturingOperand) return false;
        _cancellationRequested = true;
        return true;
    }

    /// <summary>
    /// Advance events until a stage permits completion, execute the common handler
    /// outside a callback, then deliver the remaining same-time/later events. One
    /// invocation can complete at most one command; a waiting command is resumable.
    /// No legacy instruction ticks, host waits, or average costs are introduced.
    /// </summary>
    internal HardwareInstructionOutcome? AdvanceTo(HardwareInstant time)
    {
        BeginDriving();
        try
        {
            if (time.Nanoseconds < Timeline.Now.Nanoseconds)
                throw new ArgumentOutOfRangeException(nameof(time), "Hardware time cannot move backward.");
            HardwareInstructionOutcome? outcome = RetireInvalidatedPreparation();
            if (!_pendingInstruction.HasValue && _processor.HasPreparedInstruction)
                throw new InvalidOperationException("The pending CPU command belongs to another execution owner.");
            if (_pendingInstruction is { } instruction)
            {
                Timeline.AdvanceUntil(time, () => _completionReady || _cancellationRequested || !HasPendingInstruction);
                if (!HasPendingInstruction)
                    outcome = RetireInvalidatedPreparation();
                else if (_cancellationRequested)
                {
                    _processor.CancelInstruction(in instruction);
                    outcome = RetireInvalidatedPreparation();
                }
                else if (_completionReady)
                    outcome = CompletePendingInstruction(in instruction);
                else return null;
            }
            Timeline.AdvanceTo(time);
            return outcome;
        }
        finally { EndDriving(); }
    }

    /// <summary>One bounded event boundary; no invented deadline when the queue is empty.</summary>
    internal HardwareInstructionOutcome? AdvanceNextEvent() =>
        AdvanceTo(_completionReady || _cancellationRequested ||
            (_pendingInstruction.HasValue && !HasPendingInstruction) ? Timeline.Now :
            Timeline.NextEventTime ?? Timeline.Now);

    private HardwareInstructionOutcome CompletePendingInstruction(in InstructionExecutor.PreparedInstruction instruction)
    {
        HardwareInstructionOutcome outcome;
        bool prohibited = Timeline.AdvancementProhibited;
        Timeline.AdvancementProhibited = true; // Extracodes/diagnostics may schedule, but cannot advance time mid-command.
        _publishingInstruction = true;
        try
        {
            bool stopped;
            try { stopped = _processor.CompleteInstruction(in instruction); }
            catch (Exception failure)
            {
                outcome = new(_pendingHandle, Timeline.Now,
                    failure is ProcessorException ? HardwareInstructionStatus.GuestFault : HardwareInstructionStatus.HostFailure, false);
                ClearPendingInstruction();
                NotifyCompleted(outcome, failure);
                throw;
            }
            outcome = new(_pendingHandle, Timeline.Now, _processor.LastStepCompleted ?
                HardwareInstructionStatus.Completed : HardwareInstructionStatus.GuestFault, stopped);
            ClearPendingInstruction();
            if (outcome.Status == HardwareInstructionStatus.Completed) CompletedInstructions++;
            NotifyCompleted(outcome);
            return outcome;
        }
        finally
        {
            _publishingInstruction = false;
            Timeline.AdvancementProhibited = prohibited;
        }
    }

    private HardwareInstructionOutcome? RetireInvalidatedPreparation()
    {
        if (!_pendingInstruction.HasValue || HasPendingInstruction) return null;
        var outcome = new HardwareInstructionOutcome(_pendingHandle, Timeline.Now, HardwareInstructionStatus.Cancelled, false);
        ClearPendingInstruction();
        CancelledInstructions++;
        NotifyCompleted(outcome);
        return outcome;
    }

    private void ClearPendingInstruction()
    {
        Timeline.Cancel(_readyEvent);
        _readyEvent = default;
        _pendingInstruction = null;
        _pendingHandle = default;
        _completionReady = _cancellationRequested = false;
    }

    private void NotifyCompleted(HardwareInstructionOutcome outcome, Exception? executionFailure = null)
    {
        LastOutcome = outcome;
        bool cpuProhibited = _processor.ExecutionProhibited;
        bool timeProhibited = Timeline.AdvancementProhibited;
        _processor.ExecutionProhibited = Timeline.AdvancementProhibited = true;
        try { InstructionCompleted?.Invoke(outcome); }
        catch (Exception cause) { throw new HardwareExecutionObserverException(outcome, cause, executionFailure); }
        finally
        {
            _processor.ExecutionProhibited = cpuProhibited;
            Timeline.AdvancementProhibited = timeProhibited;
        }
    }
}
