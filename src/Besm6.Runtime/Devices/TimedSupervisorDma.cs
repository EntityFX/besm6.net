using Besm6.Architecture.Memory;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Calendar;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Runtime.Devices;

internal readonly record struct SupervisorDmaWord(uint PhysicalAddress, int MediaIndex);

/// <summary>
/// Raw external exchange: no RP/RZ (приписка/защита), CPU buffer flush or parity
/// conversion. Device requests share the CPU's MRAM queue. An explicit issue
/// interval spaces words, or zero issues a burst; this is logical bus arbitration.
/// </summary>
internal sealed class TimedSupervisorDma
{
    private readonly MramMemoryPort _port;
    private readonly HardwareDuration _issueInterval;
    internal int PendingTransfers { get; private set; }
    internal bool UsesPort(MramMemoryPort port) => ReferenceEquals(_port, port);
    internal bool HasPendingMemoryCycles => _port.PendingRequests != 0;
    internal TimedSupervisorDma(MramMemoryPort port, HardwareDuration issueInterval)
    { _port = port; _issueInterval = issueInterval; }

    internal void Transfer(bool toMemory, MemoryWord50[] media, SupervisorDmaWord[] words, Action completed, Action aborted)
    {
        ArgumentNullException.ThrowIfNull(media); ArgumentNullException.ThrowIfNull(words); ArgumentNullException.ThrowIfNull(completed); ArgumentNullException.ThrowIfNull(aborted);
        foreach (var word in words)
            if (word.PhysicalAddress >= _port.Storage.CapacityWords || word.MediaIndex < 0 || word.MediaIndex >= media.Length)
                throw new ArgumentOutOfRangeException(nameof(words));
        if (words.Length == 0) { completed(); return; }
        // Validate the issue horizon before any word enters MRAM.
        _ = _port.Timeline.Now + new HardwareDuration(checked((ulong)(words.Length - 1) * _issueInterval.Nanoseconds));
        int issued = 0, remaining = words.Length;
        bool active = true;
        var tokens = new List<MramRequestToken>(words.Length);
        HardwareEventToken nextIssue = default;
        PendingTransfers++;
        void Abort()
        {
            if (!active) return;
            active = false; PendingTransfers--;
            _port.Timeline.Cancel(nextIssue);
            foreach (var token in tokens) _port.CancelQueuedOrDetachAcceptedWrite(token);
            aborted();
        }
        void Returned(int index, MramTransfer transfer)
        {
            if (!active) return;
            if (!toMemory) media[words[index].MediaIndex] = transfer.Word;
            if (--remaining != 0) return;
            active = false;
            PendingTransfers--; // Terminal even if the host observer throws.
            completed();
        }
        void Issue()
        {
            nextIssue = default;
            if (!active) return;
            try
            {
                do
                {
                    int index = issued; var word = words[index];
                    tokens.Add(toMemory ? _port.Write(word.PhysicalAddress, media[word.MediaIndex], reply => Returned(index, reply)) :
                        _port.Read(word.PhysicalAddress, reply => Returned(index, reply)));
                    issued++;
                } while (_issueInterval.Nanoseconds == 0 && issued < words.Length);
                if (issued < words.Length) nextIssue = _port.Timeline.Schedule(_issueInterval, Issue);
            }
            catch { Abort(); throw; }
        }
        Issue();
    }
}
