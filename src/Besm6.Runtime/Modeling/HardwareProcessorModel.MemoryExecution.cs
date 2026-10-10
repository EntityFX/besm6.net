using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private bool _timedFetchWaiting, _timedOperandRequested;
    private HardwareBufferedRead? _timedFetchReply, _timedOperandTransfer;
    private HardwareBufferedMemoryToken _timedFetchToken, _timedOperandToken;
    internal bool FetchWaiting => _timedFetchWaiting && HasPendingInstruction;
    internal HardwareInstructionHandle? CurrentInstruction =>
        HasPendingInstruction && !_timedFetchWaiting ? _pendingHandle : null;

    /// <summary>Reserve the shared CPU lease before its asynchronous BRS/MRAM fetch.</summary>
    internal void StartTimedInstructionFetch(MramPortConfiguration configuration, HardwareDuration hitLatency)
    {
        EnsureDriverAllowed();
        var buffers = CreateBufferedMemory(configuration, hitLatency);
        BeginDriving();
        try
        {
            RetireInvalidatedPreparation();
            if (_pendingInstruction.HasValue || _processor.HasPreparedInstruction)
                throw new InvalidOperationException("A CPU instruction is already pending.");
            Timeline.AdvanceTo(Timeline.Now);
            if (_processor.Supervisor is not null && PendingArithmeticInterruption != 0)
                throw new InvalidOperationException("The UU must accept its pending arithmetic cause before fetching.");
            ulong sequence = checked(_sequence + 1);
            var lease = _processor.BeginInstructionFetch();
            _pendingInstruction = lease; _pendingHandle = new(this, sequence, in lease, Timeline.Now);
            _sequence = sequence; _timedFetchWaiting = true;
            try
            {
                _timedFetchToken = buffers.FetchInstruction(lease.Address, lease.RightHalf, reply => _timedFetchReply = reply);
            }
            catch (Exception failure) when (failure is MemoryProtectionException or HardwareWatchException)
            {
                // Preserve admission faults for the shared CPU completion boundary.
                _timedFetchFailure = failure;
                _timedFetchReply = new(default, lease.Address, default, default, MemoryFaultSource.Protection, Timeline.Now, null);
            }
            catch { _processor.CancelInstruction(in lease); ClearPendingInstruction(); throw; }
            IssuedInstructions++;
        }
        finally { EndDriving(); }
    }

    internal bool TryCancelTimedFetch() => _timedFetchWaiting && TryRequestCancellation(in _pendingHandle);

    private Exception? _timedFetchFailure, _timedOperandFailure;
    private void AcceptTimedInstructionFetch(HardwareBufferedRead reply)
    {
        var lease = _pendingInstruction!.Value;
        try
        {
            var fetched = _processor.AcceptInstructionFetch(in lease, reply.Word, _timedFetchFailure ?? reply.Failure);
            _pendingInstruction = fetched;
            _pendingHandle = new(this, _pendingHandle.Sequence, in fetched, Timeline.Now);
            _timedFetchWaiting = false; _timedFetchReply = null; _timedFetchFailure = null;
            if (fetched.Instruction is null) _completionReady = true;
        }
        catch (Exception failure)
        {
            var outcome = new HardwareInstructionOutcome(_pendingHandle, Timeline.Now,
                failure is ProcessorException ? HardwareInstructionStatus.GuestFault : HardwareInstructionStatus.HostFailure, false);
            ClearPendingInstruction(); NotifyCompleted(outcome, failure); throw;
        }
    }

    internal void RequestBoundArithmeticOperand(HardwareInstructionHandle instruction, ArithmeticCommandHandle command)
    {
        EnsureDriverAllowed();
        if (!IsCurrent(in instruction) || _boundArithmetic is null || command != _boundArithmeticCommand ||
            _boundOperandInitiallyReady || _timedOperandRequested || _bufferedMemory is null ||
            _boundCommandWord?.Source != ArithmeticCommandSource.BufferRead)
            throw new InvalidOperationException("No waiting bound memory operand can issue this request.");
        uint address = _processor.GetPreparedMemoryOperandAddress(_pendingInstruction!.Value);
        _timedOperandRequested = true;
        try
        {
            _timedOperandToken = _bufferedMemory.ReadOperand(address, reply =>
            {
                if (!IsCurrent(in instruction) || _cancellationRequested) return;
                _timedOperandTransfer = reply;
                if (!_boundArithmetic.TryReleaseBufferWait(command))
                    throw new InvalidOperationException("The AU cannot receive this timed memory transfer.");
            });
        }
        catch (Exception failure) when (failure is MemoryProtectionException or HardwareWatchException)
        {
            // Admission failed before MRAM. PVR still owns address/stack effects
            // and the shared completion handler owns the guest trap.
            _timedOperandFailure = failure;
            _timedOperandTransfer = new(default, address, default, default, MemoryFaultSource.Protection, Timeline.Now, null);
            if (!_boundArithmetic.TryReleaseBufferWait(command))
                throw new InvalidOperationException("The AU cannot accept its failed operand admission.");
        }
        catch { _timedOperandRequested = false; throw; }
    }

    private PreparedArithmeticOperation? CaptureTransferredOperand(HardwareInstructionHandle instruction, HardwareBufferedRead transfer)
    {
        bool prohibited = Timeline.AdvancementProhibited;
        _capturingOperand = true; Timeline.AdvancementProhibited = true;
        try { return _processor.CaptureTransferredArithmeticOperand(_pendingInstruction!.Value,
            transfer.Address, transfer.Word, _timedOperandFailure ?? transfer.Failure); }
        finally { _capturingOperand = false; Timeline.AdvancementProhibited = prohibited; }
    }

    private void TimedMemoryCancelled(HardwareBufferedMemoryToken token)
    {
        if ((token == _timedFetchToken || token == _timedOperandToken) && HasPendingInstruction)
            _cancellationRequested = true;
    }

    private void ReleaseTimedMemory()
    {
        var fetch = _timedFetchToken; var operand = _timedOperandToken;
        _timedFetchToken = _timedOperandToken = default;
        _timedFetchWaiting = _timedOperandRequested = false;
        _timedFetchReply = _timedOperandTransfer = null; _timedFetchFailure = _timedOperandFailure = null;
        _bufferedMemory?.Cancel(fetch); _bufferedMemory?.Cancel(operand);
    }
}
