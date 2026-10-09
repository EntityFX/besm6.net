namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory")]
public sealed class PageAssignmentRegisterTests
{
    public static IEnumerable<object[]> AssignmentBits() =>
        from groupIndex in Enumerable.Range(0, 8)
        from field in Enumerable.Range(0, 4)
        from bit in Enumerable.Range(0, 5)
        select new object[] { groupIndex, field, bit };

    [TestMethod]
    [DynamicData(nameof(AssignmentBits))]
    public void EveryAssignmentBitSelectsItsPrintedPage(int group, int field, int bit)
    {
        var assignment = new PageAssignment();
        assignment.ImportAssignmentGroup((uint)group, new(1UL << (field * 5 + bit)));
        for (uint page = 0; page < 32; page++)
            Assert.AreEqual(page == group * 4 + field ? 1u << bit : 0u,
                assignment.GetPhysicalPage(page));
    }

    [TestMethod]
    public void LiteralAssignmentAndUnusedAccumulatorBits()
    {
        // Sheet 107: first field is bits 1..5, fourth is bits 16..20.
        // 0x20C41 has the literal field values 1, 2, 3, 4.
        foreach (int bit in Enumerable.Range(20, 8).Concat(Enumerable.Range(32, 16)))
        {
            var assignment = new PageAssignment();
            assignment.SetPhysicalPage(0, 7);
            assignment.SetPhysicalPage(31, 9);
            assignment.ImportAssignmentGroup(3, new(0x20C41UL | (1UL << bit)));
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 },
                Enumerable.Range(12, 4).Select(p => assignment.GetPhysicalPage((uint)p)).ToArray());
            Assert.AreEqual(7u, assignment.GetPhysicalPage(0));
            Assert.AreEqual(9u, assignment.GetPhysicalPage(31));
        }
    }

    public static IEnumerable<object[]> ExtensionBits() =>
        from groupIndex in Enumerable.Range(0, 8)
        from field in Enumerable.Range(0, 4)
        select new object[] { groupIndex, field };

    [TestMethod]
    [DynamicData(nameof(ExtensionBits))]
    public void UnsupportedSixthBitRejectsEntireGroup(int group, int field)
    {
        var assignment = new PageAssignment();
        for (uint page = 0; page < 32; page++) assignment.SetPhysicalPage(page, page);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            assignment.ImportAssignmentGroup((uint)group, new(0x20C41UL | (1UL << (28 + field)))));
        for (uint page = 0; page < 32; page++) Assert.AreEqual(page, assignment.GetPhysicalPage(page));
    }

    public static IEnumerable<object[]> ProtectionBits() =>
        from groupIndex in Enumerable.Range(0, 4)
        from bit in Enumerable.Range(0, 8)
        select new object[] { groupIndex, bit };

    [TestMethod]
    [DynamicData(nameof(ProtectionBits))]
    public void EveryProtectionBitSelectsItsMathematicalPage(int group, int bit)
    {
        var assignment = new PageAssignment();
        assignment.ImportProtectionGroup((uint)group, new(1UL << (20 + bit)));
        Assert.AreEqual(1u << (group * 8 + bit), assignment.OperandProtectionMask);
        uint address = (uint)(group * 8 + bit) * 1024 + 8;
        Assert.ThrowsExactly<MemoryProtectionException>(() =>
            assignment.ResolveOperand(address, false, true, false, false));
        Assert.AreEqual(address, assignment.ResolveOperand(address, false, true, true, false).Address);
    }

    [TestMethod]
    public void LiteralProtectionReplacesOnlySelectedGroupAndIgnoresOtherBits()
    {
        var assignment = new PageAssignment { OperandProtectionMask = 0x12345678 };
        // Only bits 21..28 (A5) are meaningful; other bits deliberately all one.
        assignment.ImportProtectionGroup(2, new(0xFFFFFA5FFFFFUL));
        Assert.AreEqual(0x12A55678u, assignment.OperandProtectionMask);
        assignment.ImportProtectionGroup(2, Word48.Zero);
        Assert.AreEqual(0x12005678u, assignment.OperandProtectionMask);
    }

    [TestMethod]
    public void InvalidGroupsDoNotChangeState()
    {
        var assignment = new PageAssignment { OperandProtectionMask = 0x12345678 };
        assignment.SetPhysicalPage(0, 9);
        foreach (uint group in new uint[] { 8, uint.MaxValue })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.ImportAssignmentGroup(group, Word48.Zero));
        foreach (uint group in new uint[] { 4, uint.MaxValue })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.ImportProtectionGroup(group, Word48.Zero));
        Assert.AreEqual(9u, assignment.GetPhysicalPage(0));
        Assert.AreEqual(0x12345678u, assignment.OperandProtectionMask);
    }
}
