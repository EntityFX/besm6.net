using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private readonly PhysicalMemory? _physicalMemory;
    private readonly MappedMemoryBackend? _mappedMemory;
    private HardwareBufferedMemory? _bufferedMemory;
    private MramMemoryPort? _memoryPort;

    internal HardwareBufferedMemory CreateBufferedMemory(MramPortConfiguration configuration, HardwareDuration hitLatency)
    {
        EnsureDriverAllowed();
        if (_mappedMemory is null || !ReferenceEquals(_mappedMemory.PhysicalMemory, _physicalMemory))
            throw new InvalidOperationException("The buffered model requires the machine's shared mapped memory.");
        if (_bufferedMemory is { } existing)
        {
            if (existing.BufferHitLatency != hitLatency)
                throw new InvalidOperationException("This machine's buffer timing is already selected.");
            CreateMemoryPort(configuration);
            return existing;
        }
        return _bufferedMemory = new(_mappedMemory, Timeline, CreateMemoryPort(configuration), hitLatency);
    }

    internal MramMemoryPort CreateMemoryPort(MramPortConfiguration configuration)
    {
        EnsureDriverAllowed();
        if (_memoryPort is { } existing)
        {
            if (existing.Configuration != configuration)
                throw new InvalidOperationException("This machine's MRAM port configuration is already selected.");
            return existing;
        }
        if (_physicalMemory is null)
            throw new InvalidOperationException("A hardware MRAM port requires shared physical machine memory.");
        return _memoryPort = new(_physicalMemory, Timeline, configuration);
    }
}
