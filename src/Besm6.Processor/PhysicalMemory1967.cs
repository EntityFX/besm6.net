namespace Besm6.Core;

/// <summary>
/// Untimed 32K physical storage for the 1967 configuration (TO-8 §4.1, §4.5–4.8).
/// Deliberately not IMemory: command fetch and operand access have different control.
/// The CPU, buffers, panel registers and interrupt delivery are not connected yet.
/// </summary>
public sealed class PhysicalMemory1967
{
    public const uint WordCount = 32768;
    public const uint BankCount = 8;
    private readonly MemoryWord50[] _words = new MemoryWord50[WordCount];

    // Initial storage is raw zero, not a claim about hardware power-on/reset state.
    public MemoryWord50 ReadRaw(uint physicalAddress)
    {
        ValidateAddress(physicalAddress);
        return _words[physicalAddress];
    }

    public void WriteRaw(uint physicalAddress, MemoryWord50 word)
    {
        ValidateAddress(physicalAddress);
        _words[physicalAddress] = word;
    }

    public Word48 FetchInstruction(uint physicalAddress, bool rightHalf)
    {
        MemoryWord50 word = ReadRaw(physicalAddress);
        if (!word.HasValidInstructionControl(rightHalf))
            throw new MemoryControlException(physicalAddress, rightHalf
                ? MemoryAccessKind1967.InstructionRight : MemoryAccessKind1967.InstructionLeft);
        return word.Data;
    }

    public Word48 LoadOperand(uint physicalAddress)
    {
        MemoryWord50 word = ReadRaw(physicalAddress);
        if (!word.HasValidOperandControl)
            throw new MemoryControlException(physicalAddress, MemoryAccessKind1967.OperandRead);
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
}
