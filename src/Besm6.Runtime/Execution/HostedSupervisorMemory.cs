using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Core;

namespace Besm6.Runtime.Execution;

/// <summary>Runtime-only mathematical view for hosted monitor/loader/services.
/// Native physical host memory, panel addresses and guest MMU checks are unchanged.</summary>
internal sealed class HostedSupervisorMemory(MappedMemoryBackend memory) : IHostMemory
{
    public int Size => (int)ArchitectureConstants.WordCount;
    private uint Physical(uint address)
    {
        if (address >= ArchitectureConstants.WordCount) throw new ArgumentOutOfRangeException(nameof(address));
        return memory.Assignment.TranslateRequest(new(address, false));
    }
    public Word48 Read(uint address) => memory.HostMemory.Read(Physical(address));
    public void Write(uint address, Word48 word) => memory.HostMemory.Write(Physical(address), word);
}
