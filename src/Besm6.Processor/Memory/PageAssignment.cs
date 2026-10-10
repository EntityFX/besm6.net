namespace Besm6.Core;

public enum MemoryAddressKind { PhysicalMemory, ZeroOperand, PanelRegister }

public readonly record struct ResolvedMemoryAddress(MemoryAddressKind Kind, uint Address);

/// <summary>
/// Default 32K page assignment/protection: TO-8, §4.19, §4.21–4.28.
/// Explicit Simh512K uses the selected simulator's expanded RP fields only.
/// Host setters specify page numbers, not an assumed encoding of privileged registers.
/// Accumulator import follows the printed RP/RZ tables; CPU reset defaults, buffers,
/// register instruction execution and device protection are not inferred.
/// </summary>
public sealed class PageAssignment
{
    public const uint PageSize = 1024;
    public const uint PageCount = 32;
    private readonly uint[] _physicalPages = new uint[PageCount];
    public MemoryConfiguration Configuration { get; }
    public uint PhysicalPageCount { get; }

    public PageAssignment() : this(MemoryConfiguration.Classical32K) { }

    public PageAssignment(MemoryConfiguration configuration)
    {
        PhysicalPageCount = configuration switch
        {
            MemoryConfiguration.Classical32K => PageCount,
            MemoryConfiguration.Simh512K => 512,
            _ => throw new ArgumentOutOfRangeException(nameof(configuration))
        };
        Configuration = configuration;
    }

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
        if (physicalPage >= PhysicalPageCount)
            throw new ArgumentOutOfRangeException(nameof(physicalPage));
        _physicalPages[mathematicalPage] = physicalPage;
    }

    /// <summary>
    /// Imports the accumulator fields for RP0..RP7 (TO-8, sheet 107).
    /// group is a host index, not a privileged instruction address.
    /// Sixth page bits describe the 64K extension; the default 32K configuration rejects them
    /// before changing any assignment. This is configuration validation, not a guest fault.
    /// Required BRZ transfers and CPU serialization are the caller's responsibility.
    /// </summary>
    public void ImportAssignmentGroup(uint group, Word48 accumulator)
    {
        ValidateImportAssignmentGroup(group, accumulator);
        Span<uint> pages = stackalloc uint[4];
        for (int field = 0; field < 4; field++)
        {
            uint page = (uint)((accumulator.Value >> (field * 5)) & 31);
            if (Configuration == MemoryConfiguration.Simh512K)
            {
                // https://github.com/simh/simh/blob/9e318d16a203a3efae3420ea3f5700a8d7e3bec3/BESM6/besm6_mmu.c#L691
                // Selected SIMH mmu_setrp: low five bits in1..20,
                // successive page bits interleaved in29..48; MEMSIZE mask511.
                // The tenth encoded bit is discarded by that selected mask.
                for (int bit = 5; bit < 10; bit++)
                    page |= (uint)((accumulator.Value >> (28 + (bit - 5) * 4 + field)) & 1) << bit;
                page &= PhysicalPageCount - 1;
            }
            pages[field] = page;
        }
        for (int field = 0; field < 4; field++)
            _physicalPages[group * 4 + (uint)field] = pages[field];
    }

    internal static void ValidateAssignmentGroup(uint group, Word48 accumulator)
    {
        if (group >= 8) throw new ArgumentOutOfRangeException(nameof(group));
        if ((accumulator.Value & 0xF0000000UL) != 0)
            throw new ArgumentOutOfRangeException(nameof(accumulator), "64K assignment is unsupported by 32K memory.");
    }

    internal void ValidateImportAssignmentGroup(uint group, Word48 accumulator)
    {
        if (Configuration == MemoryConfiguration.Classical32K)
            ValidateAssignmentGroup(group, accumulator);
        else if (group >= 8)
            throw new ArgumentOutOfRangeException(nameof(group));
    }

    /// <summary>
    /// Translates at the request boundary using the current assignment (TO-2, sheet 66).
    /// Admission checks, zero operands and panel accesses must already be handled.
    /// This method neither checks protection nor accesses memory or buffers.
    /// </summary>
    public uint TranslateRequest(MemoryRequestAddress request) => request.IsPhysical
        ? request.Address
        : _physicalPages[request.Address >> 10] * PageSize + request.Address % PageSize;

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
