using Besm6.Architecture.Memory;

namespace Besm6.Processor.Memory;

/// <summary>Snapshot in MRU order. FetchAddress records where a possibly stale BRS word came from.</summary>
public readonly record struct AddressedMemoryBufferEntry(MemoryRequestAddress Request,
    MemoryWord50 Word, uint? FetchAddress = null);
