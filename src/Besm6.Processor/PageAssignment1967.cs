namespace Besm6.Core;

public enum MemoryAddressKind1967 { PhysicalMemory, ZeroOperand, PanelRegister }

public readonly record struct ResolvedMemoryAddress1967(MemoryAddressKind1967 Kind, uint Address);

/// <summary>
/// Page assignment/protection for 32K: TO-8 (1967), §4.19, §4.21–4.28.
/// Host setters specify page numbers, not an assumed encoding of privileged registers.
/// No CPU reset defaults, buffers, register instructions or device protection are inferred.
/// </summary>
public sealed class PageAssignment1967
{
    public const uint PageSize = 1024;
    public const uint PageCount = 32;
    private readonly uint[] _physicalPages = new uint[PageCount];

    /// <summary>Bit n protects operand accesses to mathematical page n (§4.26).</summary>
    public uint OperandProtectionMask { get; set; }

    public uint GetPhysicalPage(uint mathematicalPage)
    {
        ValidatePage(mathematicalPage);
        return _physicalPages[mathematicalPage];
    }

    public void SetPhysicalPage(uint mathematicalPage, uint physicalPage)
    {
        ValidatePage(mathematicalPage);
        ValidatePage(physicalPage);
        _physicalPages[mathematicalPage] = physicalPage;
    }

    public ResolvedMemoryAddress1967 ResolveInstruction(uint address, bool supervisor, bool rightHalf)
    {
        ValidateAddress(address);
        if (supervisor)
            return ResolveDirect(address);
        uint physicalPage = _physicalPages[address >> 10];
        // A zero assignment closes the page for commands, not for operands (§4.28).
        if (physicalPage == 0)
            throw new MemoryProtection1967Exception(address, rightHalf
                ? MemoryAccessKind1967.InstructionRight : MemoryAccessKind1967.InstructionLeft);
        return Physical(physicalPage * PageSize + address % PageSize);
    }

    public ResolvedMemoryAddress1967 ResolveOperand(uint address, bool write,
        bool assignmentBlocked, bool protectionBlocked, bool supervisor)
    {
        ValidateAddress(address);
        if (address == 0)
            return new(MemoryAddressKind1967.ZeroOperand, 0);
        bool panelExemption = supervisor && address <= 7;
        if (!protectionBlocked && !panelExemption &&
            (OperandProtectionMask & (1u << (int)(address >> 10))) != 0)
            throw new MemoryProtection1967Exception(address, write
                ? MemoryAccessKind1967.OperandWrite : MemoryAccessKind1967.OperandRead);
        return assignmentBlocked ? ResolveDirect(address)
            : Physical(_physicalPages[address >> 10] * PageSize + address % PageSize);
    }

    private static ResolvedMemoryAddress1967 Physical(uint address) =>
        new(MemoryAddressKind1967.PhysicalMemory, address);

    private static ResolvedMemoryAddress1967 ResolveDirect(uint address) => address is >= 1 and <= 7
        ? new(MemoryAddressKind1967.PanelRegister, address) : Physical(address);

    private static void ValidatePage(uint page)
    {
        if (page >= PageCount) throw new ArgumentOutOfRangeException(nameof(page));
    }

    private static void ValidateAddress(uint address)
    {
        if (address >= PhysicalMemory1967.WordCount)
            throw new ArgumentOutOfRangeException(nameof(address));
    }
}
