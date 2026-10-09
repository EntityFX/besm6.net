namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory")]
public sealed class SupervisorExtendedMemoryTests
{
    [TestMethod]
    public void DefaultConstructorsRetainClassicalCapacityAndStrictExtensionValidation()
    {
        var physical = new PhysicalMemory();
        var assignment = new PageAssignment();
        var mapped = new MappedMemoryBackend();
        Assert.AreEqual(32768u, physical.CapacityWords);
        Assert.AreEqual(32u, assignment.PhysicalPageCount);
        Assert.AreEqual(32768, mapped.Size);
        Assert.AreEqual(MemoryConfiguration.Classical32K, physical.Configuration);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => physical.ReadRaw(32768));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.ImportAssignmentGroup(0, new(1UL << 28)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => mapped.SetPhysicalPage(1, 32));
    }

    [TestMethod]
    public void ExplicitExpansionStoresRawControlAtLastNineteenBitAddress()
    {
        var physical = new PhysicalMemory(MemoryConfiguration.Simh512K);
        var word = MemoryWord50.Form(new Word48(0x123456789ABC), false, false);
        physical.WriteRaw(524287, word);
        Assert.AreEqual(524288u, physical.CapacityWords);
        Assert.AreEqual(word, physical.ReadRaw(524287));
        Assert.AreEqual(word.Data, physical.FetchInstruction(524287, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => physical.ReadRaw(524288));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => physical.WriteRaw(524288, word));
        Assert.AreEqual(word, physical.ReadRaw(524287));
        // Existing bank helpers describe classical geometry, not expanded wiring.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PhysicalMemory.GetBank(524287));
    }

    // Independent field positions from pinned SIMH mmu_setrp, lines699..706:
    // https://github.com/simh/simh/blob/9e318d16a203a3efae3420ea3f5700a8d7e3bec3/BESM6/besm6_mmu.c
    [TestMethod]
    [DataRow(28, 0, 32)]
    [DataRow(29, 1, 32)]
    [DataRow(30, 2, 32)]
    [DataRow(31, 3, 32)]
    [DataRow(32, 0, 64)]
    [DataRow(33, 1, 64)]
    [DataRow(34, 2, 64)]
    [DataRow(35, 3, 64)]
    [DataRow(36, 0, 128)]
    [DataRow(37, 1, 128)]
    [DataRow(38, 2, 128)]
    [DataRow(39, 3, 128)]
    [DataRow(40, 0, 256)]
    [DataRow(41, 1, 256)]
    [DataRow(42, 2, 256)]
    [DataRow(43, 3, 256)]
    [DataRow(44, 0, 0)]
    [DataRow(45, 1, 0)]
    [DataRow(46, 2, 0)]
    [DataRow(47, 3, 0)]
    public void PinnedSimhExtendedFieldsAndCapacityMask(int bit, int field, int expectedPage)
    {
        var assignment = new PageAssignment(MemoryConfiguration.Simh512K);
        assignment.ImportAssignmentGroup(7, new(1UL << bit));
        for (uint page = 0; page < 32; page++)
            Assert.AreEqual(page == 28u + (uint)field ? (uint)expectedPage : 0u,
                assignment.GetPhysicalPage(page));
    }

    [TestMethod]
    public void LowFieldsUnusedBitsAndAllOnesHaveIndependentExpectedPages()
    {
        var assignment = new PageAssignment(MemoryConfiguration.Simh512K);
        assignment.ImportAssignmentGroup(0, new(0xF81)); // low fields: 1,28,3,0.
        CollectionAssert.AreEqual(new uint[] { 1, 28, 3, 0 }, Pages(assignment));
        assignment.ImportAssignmentGroup(0, new(0xFF00000)); // bits21..28 unused by RP.
        CollectionAssert.AreEqual(new uint[] { 0, 0, 0, 0 }, Pages(assignment));
        assignment.ImportAssignmentGroup(0, new(Word48.Mask48));
        CollectionAssert.AreEqual(new uint[] { 511, 511, 511, 511 }, Pages(assignment));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.ImportAssignmentGroup(8, Word48.Zero));
        CollectionAssert.AreEqual(new uint[] { 511, 511, 511, 511 }, Pages(assignment));
    }

    [TestMethod]
    public void MathematicalRequestsStayFifteenBitAndTranslateToPhysicalPage511()
    {
        var memory = Expanded();
        memory.SetPhysicalPage(31, 511);
        memory.PhysicalMemory.Store(524287, new(77), true, true);
        Assert.AreEqual(77UL, memory.Read(32767).Value);
        Assert.AreEqual(524287u, memory.Assignment.ResolveOperand(32767, false, false, true, false).Address);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.Read(32768));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.FetchInstruction(32768, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.SetPhysicalPage(32, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.SetPhysicalPage(1, 512));
        memory.AssignmentBlocked = true;
        memory.PhysicalMemory.Store(32767, new(88), true, true);
        Assert.AreEqual(88UL, memory.Read(32767).Value);
    }

    [TestMethod]
    public void HighPhysicalAliasesKeepDistinctBrzTagsAndPublicationOrder()
    {
        var memory = Expanded();
        memory.SetPhysicalPage(1, 511);
        memory.SetPhysicalPage(2, 511);
        memory.PhysicalMemory.Store(523269, new(7), true, true);
        memory.Write(1029, new(11));
        Assert.AreEqual(11UL, memory.Read(1029).Value);
        Assert.AreEqual(7UL, memory.Read(2053).Value);
        memory.Write(2053, new(13));
        Assert.AreEqual(2, memory.PendingWriteCount);
        memory.FlushOperands();
        Assert.AreEqual(13UL, memory.Read(1029).Value);
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(523269).Data.Value);
    }

    [TestMethod]
    public void CoordinatedHighRemapPublishesOldPageAndDirectRemapUsesNewPage()
    {
        var memory = Expanded();
        memory.SetPhysicalPage(1, 511);
        memory.Write(1029, new(11));
        memory.SetPhysicalPage(1, 510);
        Assert.AreEqual(11UL, memory.PhysicalMemory.ReadRaw(523269).Data.Value);
        Assert.AreEqual(0UL, memory.Read(1029).Value);
        memory.Write(1029, new(13));
        memory.Assignment.SetPhysicalPage(1, 509);
        memory.FlushOperands();
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(521221).Data.Value);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(522245).Data.Value);
    }

    [TestMethod]
    public void HighPageBrsRemainsStaleUntilExplicitClearAndHostWriteInvalidates()
    {
        var memory = Expanded();
        memory.SetPhysicalPage(1, 511);
        memory.HostMemory.Write(523269, new(11));
        memory.HostMemory.Write(522245, new(13));
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, false).Value);
        memory.SetPhysicalPage(1, 510);
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, true).Value);
        memory.ClearInstructionBuffer();
        Assert.AreEqual(13UL, memory.FetchInstruction(1029, false).Value);
        memory.HostMemory.Write(522245, new(17));
        Assert.AreEqual(17UL, memory.FetchInstruction(1029, false).Value);
    }

    [TestMethod]
    public void HostPhysicalViewSupportsExpandedBoundaryWithoutChangingGuestZero()
    {
        var memory = Expanded();
        Assert.AreEqual(524288, memory.HostMemory.Size);
        memory.HostMemory.Write(524287, new(99));
        Assert.AreEqual(99UL, memory.HostMemory.Read(524287).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.HostMemory.Read(524288));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.HostMemory.Write(524288, Word48.Zero));
        memory.SetPhysicalPage(0, 511);
        memory.PhysicalMemory.Store(523264, new(42), true, true);
        Assert.AreEqual(0UL, memory.Read(0).Value);
        memory.Write(0, new(43));
        Assert.AreEqual(42UL, memory.PhysicalMemory.ReadRaw(523264).Data.Value);
    }

    [TestMethod]
    public void ExtendedMappingRetainsProtectionByMathematicalPage()
    {
        var memory = Expanded();
        memory.SetPhysicalPage(31, 511);
        memory.Assignment.OperandProtectionMask = 1u << 31;
        memory.ProtectionBlocked = false;
        Assert.ThrowsExactly<MemoryProtectionException>(() => memory.Read(32767));
        Assert.ThrowsExactly<MemoryProtectionException>(() => memory.Write(32767, new(17)));
        Assert.AreEqual(0, memory.PendingWriteCount);
        memory.ProtectionBlocked = true;
        memory.Write(32767, new(17));
        memory.FlushOperands();
        Assert.AreEqual(17UL, memory.PhysicalMemory.ReadRaw(524287).Data.Value);
    }

    [TestMethod]
    public void InvalidConfigurationsAreRejectedBeforeAllocatingStorage()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PhysicalMemory((MemoryConfiguration)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PageAssignment((MemoryConfiguration)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MappedMemoryBackend((MemoryConfiguration)99));
    }

    private static uint[] Pages(PageAssignment assignment) => Enumerable.Range(0, 4)
        .Select(page => assignment.GetPhysicalPage((uint)page)).ToArray();

    private static MappedMemoryBackend Expanded() => new(MemoryConfiguration.Simh512K)
    {
        Supervisor = false, AssignmentBlocked = false, ProtectionBlocked = true
    };
}
