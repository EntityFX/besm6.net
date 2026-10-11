using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareBufferedMemory
{
    private sealed class Store
    {
        internal required Pending Pending;
        internal required MappedStoreAdmission Admission;
        internal required Action Completed;
        internal bool Accepted, WaitingPublication, PanelPublished;
        internal HardwareBufferedMemoryToken PublicationToken;
    }
    private Store? _store;

    /// <summary>BRZ (БРЗ) accepts first; MRAM publication uses the common FIFO port.</summary>
    internal HardwareBufferedMemoryToken StoreOperand(uint address, Word48 word, Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (_store is not null) throw new InvalidOperationException("A buffered store is already waiting.");
        ulong next = checked(_nextId + 1);
        var admission = _backend.AdmitHardwareStore(address, word);
        var pending = new Pending { Token = new(this, _nextId), Store = true };
        var store = new Store { Pending = pending, Admission = admission, Completed = completed };
        pending.LocalEvent = _timeline.Schedule(BufferHitLatency, () => ContinueStore(store));
        _pending.Add(_nextId, pending); _nextId = next; _store = store;
        return pending.Token;
    }

    private void ContinueStore(Store store)
    {
        if (!ReferenceEquals(_store, store) || store.WaitingPublication) return;
        store.Pending.LocalEvent = default;
        if (!store.Accepted)
        {
            var acceptance = _backend.AcceptHardwareStore(store.Admission, store.PanelPublished);
            store.Accepted = acceptance != BufferedStoreAcceptance.WaitingForSlot;
            if (acceptance == BufferedStoreAcceptance.Accepted) { CompleteStore(store); return; }
        }
        // An accepted value and any already-started MRAM cycle survive CPU cancellation.
        if (TryPublishOldest(_ =>
            {
                if (!ReferenceEquals(_store, store)) return;
                store.WaitingPublication = false; store.PanelPublished = true;
                if (store.Accepted) CompleteStore(store); else ContinueStore(store);
            }, out var token)) { store.WaitingPublication = true; store.PublicationToken = token; }
    }

    private void CompleteStore(Store store)
    {
        _pending.Remove(store.Pending.Token.Id); _store = null;
        store.Completed();
    }

    private void ResumeWaitingStore()
    {
        if (_store is { WaitingPublication: false } store && store.Pending.LocalEvent == default)
            store.Pending.LocalEvent = _timeline.Schedule(new HardwareDuration(0), () => ContinueStore(store));
    }

    private void CancelWaitingStore(HardwareBufferedMemoryToken token)
    {
        if (_store is { } store && store.Pending.Token == token) _store = null;
        else if (_store is { } waiting)
        {
            if (waiting.PublicationToken == token) waiting.WaitingPublication = false;
            ResumeWaitingStore();
        }
    }
}
