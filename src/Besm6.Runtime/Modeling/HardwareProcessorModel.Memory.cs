using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private readonly PhysicalMemory? _physicalMemory;
    private MramMemoryPort? _memoryPort;

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
