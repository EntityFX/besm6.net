namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private readonly record struct FetchedInstruction(uint Address, bool RightHalf,
        ulong RawWord, uint RawInstruction, DecodedInstruction Instruction, bool ShouldExecute);

    /// <summary>
    /// A single-use lease on a fetched command. The model driver may advance its
    /// calendar before completion. Shared handlers either execute at completion
    /// or accept an arithmetic operand at an explicit PVR boundary; the model
    /// supplies the common result later. There is no second instruction interpreter.
    /// </summary>
    internal readonly struct PreparedInstruction
    {
        private readonly InstructionExecutor _owner;
        private readonly ulong _generation;
        internal uint Address { get; }
        internal bool RightHalf { get; }
        internal DecodedInstruction? Instruction { get; }

        internal PreparedInstruction(InstructionExecutor owner, ulong generation,
            uint address, bool rightHalf, DecodedInstruction? instruction)
        {
            _owner = owner;
            _generation = generation;
            Address = address;
            RightHalf = rightHalf;
            Instruction = instruction;
        }

        internal bool BelongsTo(InstructionExecutor owner, ulong generation) =>
            ReferenceEquals(_owner, owner) && _generation == generation;
    }

    private bool _preparationActive;
    private bool _preparationTransition;
    private ulong _preparationGeneration;
    private FetchedInstruction _prepared;
    private uint _preparationStart;
    private bool _preparationRight;
    private bool _terminalPreparation;
    private Exception? _preparationFailure;
    private bool _terminalStopped;
    private bool _terminalCompleted;
    internal bool HasPreparedInstruction => _preparationActive;
    internal bool IsPreparedInstructionActive(in PreparedInstruction instruction) =>
        _preparationActive && instruction.BelongsTo(this, _preparationGeneration);

    private void EnsureNoPreparedInstruction()
    {
        if (_preparationActive)
            throw new InvalidOperationException("A prepared CPU instruction is still pending.");
    }

    private void EnsurePreparationAllowed()
    {
        if (_processor.ExecutionProhibited)
            throw new InvalidOperationException("CPU execution inside a scheduler callback is not allowed.");
    }

    private bool _fetchPending;

    internal PreparedInstruction BeginInstructionFetch()
    {
        EnsurePreparationAllowed(); EnsureNoPreparedInstruction();
        ulong generation = checked(_preparationGeneration + 1);
        _preparationGeneration = generation; _preparationActive = _fetchPending = true;
        _preparationTransition = true;
        _terminalPreparation = false; _preparationFailure = null; _prepared = default;
        _processor.LastStepCompleted = true;
        try
        {
            var supervisor = _processor.Supervisor;
            if (supervisor?.Io is { PendingExternalInterrupts: not 0 } &&
                (supervisor.Status.Flags & ControlUnitFlags.ExternalInterruptsBlocked) == 0)
                supervisor.EnterExternal();
            _preparationStart = _state.K; _preparationRight = _state.IsRightHalf;
            _processor.LastStepCompleted = false;
            return new(this, generation, ArchitectureConstants.NormalizeAddress(_preparationStart), _preparationRight, null);
        }
        catch { _preparationActive = _fetchPending = false; throw; }
        finally { _preparationTransition = false; }
    }

    internal PreparedInstruction PrepareInstruction()
    {
        var lease = BeginInstructionFetch();
        return AcceptInstructionFetch(in lease, null, null);
    }

    internal PreparedInstruction AcceptInstructionFetch(in PreparedInstruction lease, Word48? word, Exception? failure)
    {
        ValidatePreparation(in lease);
        if (!_fetchPending) throw new InvalidOperationException("The CPU fetch has already been accepted.");
        if (ArchitectureConstants.NormalizeAddress(_state.K) != lease.Address || _state.IsRightHalf != lease.RightHalf)
            throw new InvalidOperationException("The CPU fetch position changed during its transfer.");
        _preparationTransition = true; _processor.LastStepCompleted = true;
        var supervisor = _processor.Supervisor;
        try
        {
            try
            {
                if (failure is not null)
                {
                    _state.StackCorrection = 0; _state.K = ArchitectureConstants.NormalizeAddress(_state.K);
                    throw failure;
                }
                _prepared = FetchInstruction(word);
            }
            catch (Exception fetchFailure) when (supervisor is not null && IsSupervisorGuestFailure(fetchFailure))
            {
                // A model fetch records the guest cause. Delivering its interrupt
                // belongs to the completion boundary, not the host fetch call.
                _preparationFailure = fetchFailure;
                _terminalPreparation = true;
            }
            catch (Processor.DebugWatchAbortException)
            {
                _terminalStopped = false;
                _terminalCompleted = _processor.LastStepCompleted;
                _terminalPreparation = true;
            }
            _fetchPending = false; _processor.LastStepCompleted = false;
            return new(this, _preparationGeneration, lease.Address, lease.RightHalf,
                _terminalPreparation ? null : _prepared.Instruction);
        }
        catch { _preparationActive = _fetchPending = false; throw; }
        finally { _preparationTransition = false; }
    }

    private void ValidatePreparation(in PreparedInstruction instruction)
    {
        EnsurePreparationAllowed();
        if (_preparationTransition)
            throw new InvalidOperationException("A CPU preparation transition is already in progress.");
        if (!_preparationActive || !instruction.BelongsTo(this, _preparationGeneration))
            throw new InvalidOperationException("The prepared CPU instruction is not active on this processor.");
    }

    internal bool CompleteInstruction(in PreparedInstruction instruction)
    {
        ValidatePreparation(in instruction);
        if (_fetchPending) throw new InvalidOperationException("The CPU command is waiting for its fetched word.");
        if (RequiresArithmeticResult(in instruction))
            throw new InvalidOperationException("The captured CPU command is still waiting for its arithmetic result.");
        _preparationTransition = true;
        try
        {
            if (_terminalPreparation)
            {
                if (_preparationFailure is { } failure)
                    return HandleSupervisorFailure(_processor.Supervisor!, failure,
                        _preparationStart, _preparationRight);
                _processor.LastStepCompleted = _terminalCompleted;
                return _terminalStopped;
            }
            if (_operandFailure is null &&
                (_state.K != (_arithmeticCaptured ? _arithmeticPosition : _prepared.Address) ||
                 _state.IsRightHalf != (_arithmeticCaptured ? _arithmeticRightHalf : _prepared.RightHalf)))
            {
                _processor.CancelInstructionTrace();
                throw new InvalidOperationException("The CPU instruction position changed while preparation was pending.");
            }
            _processor.LastStepCompleted = true;
            try
            {
                bool stopped = _arithmeticCaptured ? CompleteCapturedArithmetic() : ExecuteFetchedInstruction(in _prepared);
                _processor.Supervisor?.CompletedInstruction();
                return stopped;
            }
            catch (Exception failure) when (_processor.Supervisor is not null && IsSupervisorGuestFailure(failure))
            {
                if (_operandSupervisorFailure is { } fault && ReferenceEquals(failure, _operandFailure?.SourceException))
                    return InterruptFailedStep(_processor.Supervisor!, fault.Signal, fault.ReturnWord, fault.Flags);
                return HandleSupervisorFailure(_processor.Supervisor!, failure,
                    _preparationStart, _preparationRight);
            }
            catch (Processor.DebugWatchAbortException)
            {
                return false;
            }
        }
        finally
        {
            _preparationActive = false;
            _preparationTransition = false;
            _preparationFailure = null;
            ClearArithmeticPreparation();
        }
    }

    internal void CancelInstruction(in PreparedInstruction instruction)
    {
        ValidatePreparation(in instruction);
        _processor.CancelInstructionTrace();
        _processor.LastStepCompleted = false;
        InvalidatePreparedInstruction();
    }

    // Reset cancels the lease; generations are retained so old handles cannot
    // become valid after reset/restart. Fetch/cache effects are not rolled back.
    internal void InvalidatePreparedInstruction()
    {
        if (_preparationTransition)
            throw new InvalidOperationException("CPU reset during instruction preparation or completion is not allowed.");
        _preparationActive = _fetchPending = false;
        _prepared = default;
        _terminalPreparation = false;
        _preparationFailure = null;
        ClearArithmeticPreparation();
    }
}
