namespace Besm6.Core;

public enum MemoryAddressKind { PhysicalMemory, ZeroOperand, PanelRegister }

public readonly record struct ResolvedMemoryAddress(MemoryAddressKind Kind, uint Address);

/// <summary>
/// Page assignment/protection for 32K: TO-8 (1967), §4.19, §4.21–4.28.
/// Host setters specify page numbers, not an assumed encoding of privileged registers.
/// Accumulator import follows the printed RP/RZ tables; CPU reset defaults, buffers,
/// register instruction execution and device protection are not inferred.
/// </summary>
public sealed class PageAssignment
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

    /// <summary>
    /// Imports the accumulator fields for RP0..RP7 (TO-8, sheet 107).
    /// group is a host index, not a privileged instruction address.
    /// Sixth page bits describe the 64K extension; this 32K component rejects them
    /// before changing any assignment. This is configuration validation, not a guest fault.
    /// Required BRZ transfers and CPU serialization are the caller's responsibility.
    /// </summary>
    public void ImportAssignmentGroup(uint group, Word48 accumulator)
    {
        if (group >= 8) throw new ArgumentOutOfRangeException(nameof(group));
        Span<uint> pages = stackalloc uint[4];
        for (int field = 0; field < 4; field++)
        {
            uint page = (uint)((accumulator.Value >> (field * 5)) & 31);
            page |= (uint)((accumulator.Value >> (28 + field)) & 1) << 5;
            ValidatePage(page);
            pages[field] = page;
        }
        for (int field = 0; field < 4; field++)
            _physicalPages[group * 4 + (uint)field] = pages[field];
    }

    /// <summary>Imports RZ0..RZ3 from accumulator bits 21..28 (TO-8, sheets 108–109).</summary>
    public void ImportProtectionGroup(uint group, Word48 accumulator)
    {
        if (group >= 4) throw new ArgumentOutOfRangeException(nameof(group));
        int shift = (int)group * 8;
        uint bits = (uint)((accumulator.Value >> 20) & 255);
        OperandProtectionMask = (OperandProtectionMask & ~(255u << shift)) | (bits << shift);
    }

    public ResolvedMemoryAddress ResolveInstruction(uint address, bool supervisor, bool rightHalf)
    {
        ValidateAddress(address);
        if (supervisor)
            return ResolveDirect(address);
        uint physicalPage = _physicalPages[address >> 10];
        // A zero assignment closes the page for commands, not for operands (§4.28).
        if (physicalPage == 0)
            throw new MemoryProtectionException(address, rightHalf
                ? MemoryAccessKind.InstructionRight : MemoryAccessKind.InstructionLeft);
        return Physical(physicalPage * PageSize + address % PageSize);
    }

    public ResolvedMemoryAddress ResolveOperand(uint address, bool write,
        bool assignmentBlocked, bool protectionBlocked, bool supervisor)
    {
        ValidateAddress(address);
        if (address == 0)
            return new(MemoryAddressKind.ZeroOperand, 0);
        bool panelExemption = supervisor && address <= 7;
        if (!protectionBlocked && !panelExemption &&
            (OperandProtectionMask & (1u << (int)(address >> 10))) != 0)
            throw new MemoryProtectionException(address, write
                ? MemoryAccessKind.OperandWrite : MemoryAccessKind.OperandRead);
        return assignmentBlocked ? ResolveDirect(address)
            : Physical(_physicalPages[address >> 10] * PageSize + address % PageSize);
    }

    private static ResolvedMemoryAddress Physical(uint address) =>
        new(MemoryAddressKind.PhysicalMemory, address);

    private static ResolvedMemoryAddress ResolveDirect(uint address) => address is >= 1 and <= 7
        ? new(MemoryAddressKind.PanelRegister, address) : Physical(address);

    private static void ValidatePage(uint page)
    {
        if (page >= PageCount) throw new ArgumentOutOfRangeException(nameof(page));
    }

    private static void ValidateAddress(uint address)
    {
        if (address >= PhysicalMemory.WordCount)
            throw new ArgumentOutOfRangeException(nameof(address));
    }
}
