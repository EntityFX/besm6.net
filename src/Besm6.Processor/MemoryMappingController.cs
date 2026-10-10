namespace Besm6.Core;

/// <summary>
/// Host-coordinated assignment change for the functional physical-address buffers.
/// Validates the whole change, publishes pending writes under the old assignment,
/// then changes pages. This is not ZpR execution, arithmetic unit serialization or a cold reset.
/// </summary>
public sealed class MemoryMappingController
{
    public PageAssignment Assignment { get; }
    public BufferedMemory Buffers { get; }

    public MemoryMappingController(PageAssignment assignment, BufferedMemory buffers)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(buffers);
        Assignment = assignment;
        Buffers = buffers;
    }

    public void SetPhysicalPage(uint mathematicalPage, uint physicalPage)
    {
        if (mathematicalPage >= PageAssignment.PageCount)
            throw new ArgumentOutOfRangeException(nameof(mathematicalPage));
        if (physicalPage >= PageAssignment.PageCount)
            throw new ArgumentOutOfRangeException(nameof(physicalPage));
        Buffers.WriteBackPendingOperands();
        Assignment.SetPhysicalPage(mathematicalPage, physicalPage);
    }

    public void ImportAssignmentGroup(uint group, Word48 accumulator)
    {
        PageAssignment.ValidateAssignmentGroup(group, accumulator);
        Buffers.WriteBackPendingOperands();
        Assignment.ImportAssignmentGroup(group, accumulator);
    }
}
