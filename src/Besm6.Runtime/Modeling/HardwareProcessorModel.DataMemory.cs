using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private bool _dataMemoryActive, _dataMemoryReplyReady;
    private Word48 _dataMemoryReply;
    private Exception? _dataMemoryFailure;
    private HardwareBufferedMemoryToken _dataMemoryToken;

    internal void StartDataMemoryInstruction(HardwareInstructionHandle instruction)
    {
        BeginDriving();
        try
        {
            if (!IsCurrent(in instruction) || _timedFetchWaiting || _boundArithmetic is not null ||
                _completionReady || _cancellationRequested || _dataMemoryActive || _bufferedMemory is null)
                throw new InvalidOperationException("No waiting data instruction has a configured memory port.");
            CpuMemoryTransfer? request;
            bool prohibited = Timeline.AdvancementProhibited;
            Timeline.AdvancementProhibited = true;
            try { request = _processor.BeginMemoryInstruction(_pendingInstruction!.Value); }
            finally { Timeline.AdvancementProhibited = prohibited; }
            _dataMemoryActive = true;
            IssueDataTransfer(request);
        }
        finally { EndDriving(); }
    }

    private void IssueDataTransfer(CpuMemoryTransfer? request)
    {
        _dataMemoryReplyReady = false; _dataMemoryFailure = null; _dataMemoryToken = default;
        if (request is null) { _completionReady = true; return; }
        try
        {
            if (request.Value.Write)
                _dataMemoryToken = _bufferedMemory!.StoreOperand(request.Value.Address, request.Value.Word,
                    () => { _dataMemoryReply = default; _dataMemoryReplyReady = true; });
            else
                _dataMemoryToken = _bufferedMemory!.ReadOperand(request.Value.Address, reply =>
                {
                    _dataMemoryReply = reply.Word; _dataMemoryFailure = reply.Failure; _dataMemoryReplyReady = true;
                });
        }
        catch (Exception failure)
        { _dataMemoryFailure = failure; _dataMemoryReply = default; _dataMemoryReplyReady = true; }
    }

    private void AcceptDataTransfer()
    {
        CpuMemoryTransfer? request;
        bool prohibited = Timeline.AdvancementProhibited;
        Timeline.AdvancementProhibited = true;
        try { request = _processor.AcceptMemoryTransfer(_pendingInstruction!.Value, _dataMemoryReply, _dataMemoryFailure); }
        finally { Timeline.AdvancementProhibited = prohibited; }
        IssueDataTransfer(request);
    }

    private void ClearDataMemory()
    {
        var token = _dataMemoryToken; _dataMemoryToken = default;
        _dataMemoryActive = _dataMemoryReplyReady = false; _dataMemoryReply = default; _dataMemoryFailure = null;
        _bufferedMemory?.Cancel(token);
    }
}
