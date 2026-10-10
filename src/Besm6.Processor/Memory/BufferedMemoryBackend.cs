namespace Besm6.Core;

/// <summary>
/// Opt-in functional memory backend: direct 15-bit addressing, control, BRZ and BRS.
/// CPU Read/Write are operand accesses; HostMemory is a separate coherent loader view.
/// This is not a supervisor/MMU profile or a hardware timing model.
/// </summary>
public sealed class BufferedMemoryBackend : IInstructionMemory
{
    public PhysicalMemory PhysicalMemory { get; } = new();
    public BufferedMemory Buffers { get; }
    public IMemory HostMemory { get; }
    public int Size => (int)PhysicalMemory.WordCount;

    public BufferedMemoryBackend()
    {
        // Software initialization for this backend, not historical cold-reset semantics.
        for (uint address = 0; address < PhysicalMemory.WordCount; address++)
            PhysicalMemory.Store(address, Word48.Zero, true, true);
        Buffers = new BufferedMemory(PhysicalMemory);
        HostMemory = new HostView(this);
    }

    public Word48 Read(uint address) => Buffers.LoadOperand(address);
    public void Write(uint address, Word48 word) => Buffers.Store(address, word, true, true);
    public Word48 FetchInstruction(uint address, bool rightHalf) => Buffers.FetchInstruction(address, rightHalf);

    private sealed class HostView(BufferedMemoryBackend backend) : IMemory
    {
        public int Size => backend.Size;
        public Word48 Read(uint address) => backend.Buffers.PeekOperand(address).Data;
        public void Write(uint address, Word48 word) =>
            backend.Buffers.ReplaceFromHost(address, MemoryWord50.Form(word, false, false));
    }
}
