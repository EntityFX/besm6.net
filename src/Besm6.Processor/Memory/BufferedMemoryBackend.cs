using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Memory;

/// <summary>
/// Opt-in functional memory backend: direct 15-bit addressing, control, BRZ and BRS.
/// CPU Read/Write are operand accesses; HostMemory is a separate coherent loader view.
/// This is not a supervisor/MMU profile or a hardware timing model.
/// The explicit hostedCompatibility option preserves untyped, coherent Dubna self-modification.
/// Its stores form command-valid control and invalidate matching BRS entries; strict is the default.
/// </summary>
public sealed class BufferedMemoryBackend : IInstructionMemory, ICommandMemory
{
    public PhysicalMemory PhysicalMemory { get; } = new();
    public BufferedMemory Buffers { get; }
    public IHostMemory HostMemory { get; }
    public int Size => (int)ArchitectureConstants.WordCount;
    public bool HostedCompatibility { get; }

    public BufferedMemoryBackend(bool hostedCompatibility = false)
    {
        HostedCompatibility = hostedCompatibility;
        // Software initialization for this backend, not historical cold-reset semantics.
        for (uint address = 0; address < ArchitectureConstants.WordCount; address++)
            PhysicalMemory.Store(address, Word48.Zero, !hostedCompatibility, !hostedCompatibility);
        Buffers = new BufferedMemory(PhysicalMemory);
        HostMemory = new HostView(this);
    }

    public Word48 Read(uint address) => Buffers.LoadOperand(address);
    public void Write(uint address, Word48 word)
    {
        Buffers.Store(address, word, !HostedCompatibility, !HostedCompatibility);
        if (HostedCompatibility) Buffers.InvalidateHostedInstruction(address);
    }
    public Word48 FetchInstruction(uint address, bool rightHalf) => HostedCompatibility
        ? Buffers.FetchHostedInstruction(address, rightHalf) : Buffers.FetchInstruction(address, rightHalf);
    public void StoreCommand(uint address, Word48 word) => HostMemory.Write(address, word);

    private sealed class HostView(BufferedMemoryBackend backend) : IHostMemory
    {
        public int Size => backend.Size;
        public Word48 Read(uint address) => backend.Buffers.PeekOperand(address).Data;
        public void Write(uint address, Word48 word) =>
            backend.Buffers.ReplaceFromHost(address, MemoryWord50.Form(word, false, false));
    }
}
