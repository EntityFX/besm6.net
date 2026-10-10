namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private readonly record struct FetchedInstruction(uint Address, bool RightHalf,
        ulong RawWord, uint RawInstruction, DecodedInstruction Instruction, bool ShouldExecute);

    /// <summary>
    /// A single-use lease on a fetched command. The model driver may advance its
    /// calendar before completion; operands and instruction effects are still
    /// evaluated by the shared handlers at completion, not by a second CPU.
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

    internal PreparedInstruction PrepareInstruction()
    {
        EnsurePreparationAllowed();
        EnsureNoPreparedInstruction();
        ulong generation = checked(_preparationGeneration + 1);
        _preparationGeneration = generation;
        _preparationActive = true;
        _preparationTransition = true;
        _terminalPreparation = false;
        _preparationFailure = null;
        _processor.LastStepCompleted = true;
        try
        {
            var supervisor = _processor.Supervisor;
            // Interrupts are accepted once, at the same boundary as serial Step.
            // An interrupt arriving while this lease waits belongs to the next one.
            if (supervisor?.Io is { PendingExternalInterrupts: not 0 } &&
                (supervisor.Status.Flags & ControlUnitFlags.ExternalInterruptsBlocked) == 0)
                supervisor.EnterExternal();
            _preparationStart = _state.K;
            _preparationRight = _state.IsRightHalf;
            try
            {
                _prepared = FetchInstruction();
            }
            catch (Exception failure) when (supervisor is not null && IsSupervisorGuestFailure(failure))
            {
                // A model fetch records the guest cause. Delivering its interrupt
                // belongs to the completion boundary, not the host fetch call.
                _preparationFailure = failure;
                _terminalPreparation = true;
            }
            catch (Processor.DebugWatchAbortException)
            {
                _terminalStopped = false;
                _terminalCompleted = _processor.LastStepCompleted;
                _terminalPreparation = true;
            }
            if (!_preparationActive || generation != _preparationGeneration)
                throw new InvalidOperationException("CPU preparation was invalidated during fetch.");
            _processor.LastStepCompleted = false;
            return new(this, generation, ArchitectureConstants.NormalizeAddress(_preparationStart), _preparationRight,
                _terminalPreparation ? null : _prepared.Instruction);
        }
        catch
        {
            if (generation == _preparationGeneration) _preparationActive = false;
            throw;
        }
        finally
        {
            _preparationTransition = false;
        }
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
            if (_state.K != _prepared.Address || _state.IsRightHalf != _prepared.RightHalf)
            {
                _processor.CancelInstructionTrace();
                throw new InvalidOperationException("The CPU instruction position changed while preparation was pending.");
            }
            _processor.LastStepCompleted = true;
            try
            {
                bool stopped = ExecuteFetchedInstruction(in _prepared);
                _processor.Supervisor?.CompletedInstruction();
                return stopped;
            }
            catch (Exception failure) when (_processor.Supervisor is not null && IsSupervisorGuestFailure(failure))
            {
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
        _preparationActive = false;
        _prepared = default;
        _terminalPreparation = false;
        _preparationFailure = null;
    }
}
