using System.Runtime.ExceptionServices;

namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private bool _dataCaptured, _dataReady;
    private int _dataPhase;
    private ExecutionFrame _dataFrame;
    private uint _dataPosition;
    private bool _dataRightHalf;
    private ExceptionDispatchInfo? _dataFailure;
    private SupervisorFailureDescription? _dataSupervisorFailure;

    internal bool RequiresMemoryTransfer(in PreparedInstruction instruction) =>
        IsPreparedInstructionActive(in instruction) && _dataCaptured && !_dataReady;

    internal CpuMemoryTransfer? BeginMemoryInstruction(in PreparedInstruction instruction)
    {
        ValidatePreparation(in instruction);
        if (_fetchPending || _dataCaptured || _arithmeticCaptured || _terminalPreparation || !_prepared.ShouldExecute ||
            !MemoryInstructionExecutor.CanExecuteData(_prepared.Instruction.Opcode))
            throw new InvalidOperationException("No prepared data instruction can start.");
        if (_state.K != _prepared.Address || _state.IsRightHalf != _prepared.RightHalf)
            throw new InvalidOperationException("The CPU position changed before data acceptance.");
        _preparationTransition = true;
        try
        {
            _dataFrame = BeginFetchedInstruction(in _prepared);
            _dataCaptured = true; _dataPhase = 0;
            _dataPosition = _state.K; _dataRightHalf = _state.IsRightHalf;
            return BeginDataTransfer();
        }
        finally { _preparationTransition = false; }
    }

    internal CpuMemoryTransfer? AcceptMemoryTransfer(in PreparedInstruction instruction, Word48 word, Exception? failure)
    {
        ValidatePreparation(in instruction);
        if (!_dataCaptured || _dataReady) throw new InvalidOperationException("No CPU data transfer is waiting.");
        _preparationTransition = true;
        try
        {
            if (_state.K != _dataPosition || _state.IsRightHalf != _dataRightHalf)
                throw new InvalidOperationException("The CPU position changed during data transfer.");
            try
            {
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                if (_memoryInstructions.AcceptData(ref _dataFrame, _dataPhase++, word.Value))
                { _dataReady = true; return null; }
                return BeginDataTransfer();
            }
            catch (Exception cause) { LatchDataFailure(cause); return null; }
        }
        finally { _preparationTransition = false; }
    }

    private CpuMemoryTransfer? BeginDataTransfer()
    {
        try
        {
            var request = _memoryInstructions.BeginData(ref _dataFrame, _dataPhase);
            _memory.CheckTransferredAccess(request.Address, request.Write);
            return request;
        }
        catch (Exception failure) { LatchDataFailure(failure); return null; }
    }

    private void LatchDataFailure(Exception failure)
    {
        _dataFailure = ExceptionDispatchInfo.Capture(failure); _dataReady = true;
        if (_processor.Supervisor is not null && IsSupervisorGuestFailure(failure))
            _dataSupervisorFailure = DescribeSupervisorFailure(failure, _preparationStart, _preparationRight);
    }

    private bool CompleteCapturedData()
    {
        _dataFailure?.Throw();
        FinalizeInstruction(ref _dataFrame, updateRegistersAndModification: true,
            preserveZeroModification: _processor.Supervisor is not null && _dataFrame.Instruction.Opcode == Opcode.Wtc);
        return false;
    }

    private void ClearDataPreparation()
    {
        _dataCaptured = _dataReady = false; _dataPhase = 0; _dataFrame = default;
        _dataFailure = null; _dataSupervisorFailure = null;
    }
}
