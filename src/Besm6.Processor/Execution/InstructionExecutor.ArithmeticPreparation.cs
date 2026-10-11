using System.Runtime.ExceptionServices;

namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private bool _arithmeticCaptured;
    private ExecutionFrame _arithmeticFrame;
    private uint _arithmeticPosition;
    private bool _arithmeticRightHalf, _arithmeticAdditive, _arithmeticLogical;
    private ExceptionDispatchInfo? _operandFailure, _arithmeticFailure;
    private NormalizedArithmeticResult? _arithmeticResult;
    private SupervisorFailureDescription? _operandSupervisorFailure;

    private void ValidateArithmeticAccess(in PreparedInstruction instruction)
    {
        // PVR may run inside the hardware calendar. This narrow input/result API
        // cannot dispatch another command; Step/Complete retain callback guards.
        if (_preparationTransition || !_preparationActive || !instruction.BelongsTo(this, _preparationGeneration))
            throw new InvalidOperationException("No idle prepared CPU command owns this arithmetic transition.");
    }

    internal bool RequiresArithmeticResult(in PreparedInstruction instruction) =>
        IsPreparedInstructionActive(in instruction) && _arithmeticCaptured &&
        _operandFailure is null && _arithmeticFailure is null && !_arithmeticResult.HasValue;

    internal bool CanCaptureArithmeticOperand(in PreparedInstruction instruction) =>
        IsPreparedInstructionActive(in instruction) && !_arithmeticCaptured && !_dataCaptured && !_terminalPreparation &&
        _prepared.ShouldExecute && MemoryInstructionExecutor.CanCaptureArithmetic(_prepared.Instruction.Opcode);

    /// <summary>Read-only UU operand formation; uses the same C/index resolver as execution.</summary>
    internal byte GetPreparedImmediateOperand(in PreparedInstruction instruction)
    {
        ValidateArithmeticAccess(in instruction);
        if (!CanCaptureArithmeticOperand(in instruction) ||
            _prepared.Instruction.Opcode is not (Opcode.EPlusN or Opcode.EMinusN) ||
            _state.K != _prepared.Address || _state.IsRightHalf != _prepared.RightHalf)
            throw new InvalidOperationException("No uncaptured immediate arithmetic command can form its operand.");
        return (byte)(_memoryInstructions.ResolveImmediateAddress(
            ModifiedInstructionAddress(_prepared.Instruction.Address), _prepared.Instruction.Register) & 0x7F);
    }

    internal uint GetPreparedMemoryOperandAddress(in PreparedInstruction instruction)
    {
        ValidateArithmeticAccess(in instruction);
        if (!CanCaptureArithmeticOperand(in instruction) ||
            _prepared.Instruction.Opcode is Opcode.EPlusN or Opcode.EMinusN)
            throw new InvalidOperationException("No waiting memory arithmetic operand.");
        return _memoryInstructions.ResolveMemoryOperandAddress(
            ModifiedInstructionAddress(_prepared.Instruction.Address), _prepared.Instruction.Register);
    }

    /// <summary>
    /// PVR input: the same opcode/address/stack handler captures a single operand
    /// and mode. A/Y and completion diagnostics remain unpublished. A failed read
    /// is latched for the completion boundary, never thrown from the calendar.
    /// Accepted address/stack effects are not rolled back by lease cancellation.
    /// </summary>
    internal PreparedArithmeticOperation? CaptureArithmeticOperand(in PreparedInstruction instruction,
        uint? transferredAddress = null, Word48 transferredWord = default, Exception? transferredFailure = null)
    {
        ValidateArithmeticAccess(in instruction);
        if (_arithmeticCaptured || _dataCaptured || _terminalPreparation || !_prepared.ShouldExecute ||
            !MemoryInstructionExecutor.CanCaptureArithmetic(_prepared.Instruction.Opcode))
            throw new InvalidOperationException("This CPU command cannot accept an arithmetic operand.");
        if (_state.K != _prepared.Address || _state.IsRightHalf != _prepared.RightHalf)
            throw new InvalidOperationException("The prepared instruction position changed before PVR.");
        _preparationTransition = true;
        try
        {
            _arithmeticFrame = BeginFetchedInstruction(in _prepared);
            _arithmeticPosition = _state.K;
            _arithmeticRightHalf = _state.IsRightHalf;
            _arithmeticCaptured = true;
            try
            {
                var captured = transferredAddress is { } address
                    ? _memoryInstructions.CaptureTransferredArithmetic(ref _arithmeticFrame, address, transferredWord, transferredFailure)
                    : _memoryInstructions.CaptureArithmetic(ref _arithmeticFrame);
                _arithmeticAdditive = captured.Additive;
                _arithmeticLogical = captured.IsLogical;
                return captured.Operation;
            }
            catch (Exception failure)
            {
                _operandFailure = ExceptionDispatchInfo.Capture(failure);
                if (_processor.Supervisor is not null && IsSupervisorGuestFailure(failure))
                    _operandSupervisorFailure = DescribeSupervisorFailure(failure, _preparationStart, _preparationRight);
                return null;
            }
        }
        finally { _preparationTransition = false; }
    }

    internal void SupplyArithmeticResult(in PreparedInstruction instruction,
        NormalizedArithmeticResult? result, ProcessorException? failure)
    {
        ValidateArithmeticAccess(in instruction);
        if (!_arithmeticCaptured || _operandFailure is not null ||
            _arithmeticResult.HasValue || _arithmeticFailure is not null)
            throw new InvalidOperationException("This CPU command is not waiting for an arithmetic result.");
        if (result.HasValue == (failure is not null))
            throw new ArgumentException("Supply either an arithmetic result or a calculation failure.");
        _arithmeticResult = result;
        _arithmeticFailure = failure is null ? null : ExceptionDispatchInfo.Capture(failure);
    }

    private bool CompleteCapturedArithmetic()
    {
        try
        {
            _operandFailure?.Throw();
            _arithmeticFailure?.Throw();
            _memoryInstructions.PublishArithmetic(ref _arithmeticFrame, _arithmeticResult!.Value, _arithmeticAdditive, _arithmeticLogical);
        }
        catch (ProcessorException failure) when (string.IsNullOrEmpty(failure.Message))
        {
            FinalizeInstruction(ref _arithmeticFrame, updateRegistersAndModification: false);
            throw;
        }
        FinalizeInstruction(ref _arithmeticFrame, updateRegistersAndModification: true);
        return false;
    }

    private void ClearArithmeticPreparation()
    {
        _arithmeticCaptured = false;
        _arithmeticLogical = false;
        _arithmeticFrame = default;
        _operandFailure = _arithmeticFailure = null;
        _arithmeticResult = null;
        _operandSupervisorFailure = null;
    }
}
