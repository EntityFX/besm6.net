namespace Besm6.Core;

/// <summary>Explicit storage/assignment configurations; SIMH expansion is an
/// experimental compatibility contract, not historical bank wiring or timing.</summary>
public enum MemoryConfiguration { Classical32K, Simh512K }

/// <summary>
/// Untimed physical storage. Default 32K follows TO-8 §4.1, §4.5–4.8;
/// the explicit SIMH configuration supplies expanded capacity only.
/// Deliberately not IMemory: command fetch and operand access have different control.
/// CPU adapters provide separate buffer/addressing policies; this storage has no timing.
/// </summary>
public sealed class PhysicalMemory
{
    /// <summary>Legacy mathematical/classical capacity. Use CapacityWords for storage bounds.</summary>
    public const uint WordCount = 32768;
    /// <summary>Classical bank count; no expanded physical bank geometry is specified.</summary>
    public const uint BankCount = 8;
    private readonly MemoryWord50[] _words;
    public MemoryConfiguration Configuration { get; }
    public uint CapacityWords { get; }

    public PhysicalMemory() : this(MemoryConfiguration.Classical32K) { }

    public PhysicalMemory(MemoryConfiguration configuration)
    {
        CapacityWords = configuration switch
        {
            MemoryConfiguration.Classical32K => WordCount,
            MemoryConfiguration.Simh512K => 512 * 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(configuration))
        };
        Configuration = configuration;
        _words = new MemoryWord50[CapacityWords];
    }

    // Initial storage is raw zero, not a claim about hardware power-on/reset state.
    public MemoryWord50 ReadRaw(uint physicalAddress)
    {
        ValidatePhysicalAddress(physicalAddress);
        return _words[physicalAddress];
    }

    public void WriteRaw(uint physicalAddress, MemoryWord50 word)
    {
        ValidatePhysicalAddress(physicalAddress);
        _words[physicalAddress] = word;
    }

    public Word48 FetchInstruction(uint physicalAddress, bool rightHalf)
    {
        MemoryWord50 word = ReadRaw(physicalAddress);
        if (!word.HasValidInstructionControl(rightHalf))
            throw new MemoryControlException(physicalAddress, rightHalf
                ? MemoryAccessKind.InstructionRight : MemoryAccessKind.InstructionLeft);
        return word.Data;
    }

    public Word48 LoadOperand(uint physicalAddress)
    {
        MemoryWord50 word = ReadRaw(physicalAddress);
        if (!word.HasValidOperandControl)
            throw new MemoryControlException(physicalAddress, MemoryAccessKind.OperandRead);
        return word.Data;
    }

    public void Store(uint physicalAddress, Word48 data, bool invertLeft, bool invertRight) =>
        WriteRaw(physicalAddress, MemoryWord50.Form(data, invertLeft, invertRight));

    public static uint GetBank(uint physicalAddress)
    {
        ValidateAddress(physicalAddress);
        return physicalAddress & 7;
    }

    public static uint GetBankOffset(uint physicalAddress)
    {
        ValidateAddress(physicalAddress);
        return physicalAddress >> 3;
    }

    private static void ValidateAddress(uint physicalAddress)
    {
        if (physicalAddress >= WordCount)
            throw new ArgumentOutOfRangeException(nameof(physicalAddress));
    }

    private void ValidatePhysicalAddress(uint physicalAddress)
    {
        if (physicalAddress >= CapacityWords)
            throw new ArgumentOutOfRangeException(nameof(physicalAddress));
    }
}
