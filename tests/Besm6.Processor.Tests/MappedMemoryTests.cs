namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory")]
public sealed class MappedMemoryTests
{
    private static MappedMemoryBackend Mathematical()
    {
        var memory = new MappedMemoryBackend { Supervisor = false, AssignmentBlocked = false, ProtectionBlocked = false };
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.Assignment.SetPhysicalPage(2, 8); // Two mathematical aliases.
        return memory;
    }

    [TestMethod]
    public void AllMathematicalOperandAddressesTranslateAndPhysicalRequestsBypassRp()
    {
        var memory = Mathematical();
        for (uint page = 0; page < 32; page++) memory.Assignment.SetPhysicalPage(page, 31 - page);
        for (uint address = 0; address < 32768; address++) memory.PhysicalMemory.Store(address, new(address + 99), true, true);
        for (uint address = 1; address < 32768; address++)
            Assert.AreEqual((31 - address / 1024) * 1024UL + address % 1024 + 99, memory.Read(address).Value);
        memory.AssignmentBlocked = true;
        for (uint address = 8; address < 32768; address++) Assert.AreEqual(address + 99UL, memory.Read(address).Value);
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void AssignmentAndProtectionBlocksAreIndependent(bool assignmentBlocked, bool protectionBlocked)
    {
        var memory = Mathematical();
        memory.AssignmentBlocked = assignmentBlocked;
        memory.ProtectionBlocked = protectionBlocked;
        memory.Assignment.OperandProtectionMask = 2;
        if (protectionBlocked)
        {
            memory.Write(1029, new(17));
            memory.FlushOperands();
            Assert.AreEqual(17UL, memory.PhysicalMemory.ReadRaw(assignmentBlocked ? 1029u : 8197u).Data.Value);
        }
        else Assert.ThrowsExactly<MemoryProtectionException>(() => memory.Write(1029, new(17)));
    }

    [TestMethod]
    public void FourInstructionRegistersEvictByTagAndDoNotMergePhysicalAliases()
    {
        var memory = Mathematical();
        for (uint offset = 8; offset < 12; offset++) memory.HostMemory.Write(8192 + offset, new(offset));
        memory.FetchInstruction(1032, false);
        memory.FetchInstruction(2056, false); // Separate tag for the same physical word.
        memory.FetchInstruction(1033, false);
        memory.FetchInstruction(1034, false);
        memory.FetchInstruction(1032, true); // First tag becomes recent.
        memory.FetchInstruction(1035, false);
        Assert.AreEqual(4, memory.GetInstructionSnapshot().Length);
        Assert.IsFalse(memory.GetInstructionSnapshot().Any(e => e.Request.Address == 2056));
        Assert.IsTrue(memory.GetInstructionSnapshot().Any(e => e.Request.Address == 1032));
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void AliasesDoNotForwardUntilMramPublication()
    {
        var memory = Mathematical();
        memory.PhysicalMemory.Store(8197, new(7), true, true);
        memory.Write(1029, new(11));
        Assert.AreEqual(11UL, memory.Read(1029).Value);
        Assert.AreEqual(7UL, memory.Read(2053).Value);
        Assert.AreEqual(1, memory.PendingWriteCount); // Read misses do not allocate.
        memory.Write(2053, new(13));
        Assert.AreEqual(11UL, memory.Read(1029).Value);
        Assert.AreEqual(13UL, memory.Read(2053).Value);
        memory.FlushOperands();
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        Assert.AreEqual(13UL, memory.Read(1029).Value);
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void PhysicalMarkerSeparatesIdenticalFifteenBitTags()
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        memory.AssignmentBlocked = true;
        memory.Write(1029, new(13));
        Assert.AreEqual(13UL, memory.Read(1029).Value);
        memory.AssignmentBlocked = false;
        Assert.AreEqual(11UL, memory.Read(1029).Value);
        Assert.AreEqual(2, memory.PendingWriteCount);
        memory.FlushOperands();
        Assert.AreEqual(11UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(1029).Data.Value);
    }

    [TestMethod]
    public void TransferUsesCurrentMappingButCoordinatedRemapPublishesOldMapping()
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        memory.Assignment.SetPhysicalPage(1, 9); // Raw RP interface deliberately does not serialize.
        memory.FlushOperands();
        Assert.AreEqual(11UL, memory.PhysicalMemory.ReadRaw(9221).Data.Value);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        memory.Write(1029, new(13));
        memory.SetPhysicalPage(1, 10);
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(9221).Data.Value);
        Assert.AreEqual(0UL, memory.Read(1029).Value); // No clean BRZ survives remap.
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidRemapLeavesPendingWritesAndAssignmentUntouched(bool group)
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        var before = memory.GetOperandSnapshot();
        if (group) Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.ImportAssignmentGroup(0, new(1UL << 28)));
        else Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.SetPhysicalPage(1, 32));
        CollectionAssert.AreEqual(before, memory.GetOperandSnapshot());
        Assert.AreEqual(8u, memory.Assignment.GetPhysicalPage(1));
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
    }

    [TestMethod]
    public void AssignmentGroupPublishesAndProtectionGroupDoesNotFlush()
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        memory.Assignment.ImportProtectionGroup(0, new(2UL << 20));
        Assert.AreEqual(1, memory.PendingWriteCount);
        memory.ImportAssignmentGroup(0, new(9UL << 5));
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(11UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        Assert.AreEqual(9u, memory.Assignment.GetPhysicalPage(1));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProtectionPrecedesWarmBrzHitAndControl(bool write)
    {
        var memory = Mathematical();
        memory.InvertRightStoreControl = false; // Deliberately mixed control.
        memory.Write(1029, new(11));
        memory.Assignment.OperandProtectionMask = 2;
        var before = memory.GetOperandSnapshot();
        var exception = Assert.ThrowsExactly<MemoryProtectionException>(() =>
        {
            if (write) memory.Write(1029, new(13)); else memory.Read(1029);
        });
        Assert.AreEqual(1u, exception.MathematicalPage);
        Assert.AreEqual(MemoryFaultSource.Protection, memory.LastFault!.Value.Source);
        Assert.IsNull(memory.LastFault.Value.PhysicalAddress);
        CollectionAssert.AreEqual(before, memory.GetOperandSnapshot());
        memory.ProtectionBlocked = true;
        var control = Assert.ThrowsExactly<MemoryControlException>(() => memory.Read(1029));
        Assert.AreEqual(8197u, control.PhysicalAddress);
        Assert.AreEqual(MemoryFaultSource.OperandBuffer, memory.LastFault.Value.Source);
        memory.ClearFault();
        Assert.IsNull(memory.LastFault);
    }

    [TestMethod]
    public void ClosedCommandPageIsCheckedEvenOnWarmBrsHit()
    {
        var memory = Mathematical();
        memory.HostMemory.Write(8197, new(11));
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, false).Value);
        var before = memory.GetInstructionSnapshot();
        memory.SetPhysicalPage(1, 0);
        Assert.ThrowsExactly<MemoryProtectionException>(() => memory.FetchInstruction(1029, true));
        CollectionAssert.AreEqual(before, memory.GetInstructionSnapshot());
        Assert.AreEqual(MemoryAccessKind.InstructionRight, memory.LastFault!.Value.Access);
        memory.Supervisor = true;
        memory.HostMemory.Write(1029, new(13));
        Assert.AreEqual(13UL, memory.FetchInstruction(1029, false).Value);
        Assert.AreEqual(2, memory.GetInstructionSnapshot().Length);
    }

    [TestMethod]
    public void WarmBrsRetainsOldWordAfterRemapUntilExplicitInvalidation()
    {
        var memory = Mathematical();
        memory.HostMemory.Write(8197, new(11));
        memory.HostMemory.Write(9221, new(13));
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, false).Value);
        memory.SetPhysicalPage(1, 9);
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, true).Value);
        Assert.AreEqual(8197u, memory.GetInstructionSnapshot()[0].FetchAddress);
        memory.ClearInstructionBuffer();
        Assert.AreEqual(13UL, memory.FetchInstruction(1029, true).Value);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SelectedHalfControlAndFaultAddressSurviveBrsHits(bool left, bool right)
    {
        var memory = Mathematical();
        memory.PhysicalMemory.Store(8197, new(11), left, right);
        if (left) Assert.ThrowsExactly<MemoryControlException>(() => memory.FetchInstruction(1029, false));
        else Assert.AreEqual(11UL, memory.FetchInstruction(1029, false).Value);
        if (right)
        {
            var exception = Assert.ThrowsExactly<MemoryControlException>(() => memory.FetchInstruction(1029, true));
            Assert.AreEqual(8197u, exception.PhysicalAddress);
            Assert.AreEqual(MemoryFaultSource.InstructionBuffer, memory.LastFault!.Value.Source);
        }
        else Assert.AreEqual(11UL, memory.FetchInstruction(1029, true).Value);
        Assert.AreEqual(1, memory.GetInstructionSnapshot().Length);
    }

    [TestMethod]
    public void StoreDoesNotFeedInstructionBufferAndPublicationDoesNotInvalidateIt()
    {
        var memory = Mathematical();
        memory.HostMemory.Write(8197, new(11));
        memory.FetchInstruction(1029, false);
        memory.InvertLeftStoreControl = memory.InvertRightStoreControl = false;
        memory.Write(1029, new(13));
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, true).Value);
        memory.FlushOperands();
        Assert.AreEqual(13UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        Assert.AreEqual(11UL, memory.FetchInstruction(1029, false).Value);
        memory.ClearInstructionBuffer();
        Assert.AreEqual(13UL, memory.FetchInstruction(1029, false).Value);
    }

    [TestMethod]
    public void EightStoresAndNinthEvictionArePublishedOldestFirst()
    {
        var memory = Mathematical();
        for (uint i = 0; i < 8; i++) memory.Write(1032 + i, new(100 + i));
        memory.Read(1032); // Make the first entry recent.
        memory.Write(1040, new(108));
        Assert.AreEqual(101UL, memory.PhysicalMemory.ReadRaw(8201).Data.Value);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(8200).Data.Value);
        Assert.AreEqual(8, memory.PendingWriteCount);
        memory.FlushOperands();
        for (uint i = 0; i < 9; i++) Assert.AreEqual(100UL + i, memory.PhysicalMemory.ReadRaw(8200 + i).Data.Value);
    }

    [TestMethod]
    public void ZeroAndPanelWritesRespectDocumentedPublicationBarrier()
    {
        var memory = new MappedMemoryBackend { ProtectionBlocked = false };
        memory.Assignment.OperandProtectionMask = uint.MaxValue;
        memory.SetPanel(1, MemoryWord50.Form(new(17), false, false));
        Assert.AreEqual(0UL, memory.Read(0).Value);
        memory.Write(0, new(99));
        Assert.AreEqual(17UL, memory.Read(1).Value); // Supervisor panel exemption, TO-8 §4.26.
        memory.ProtectionBlocked = true;
        for (uint i = 0; i < 8; i++) memory.Write(16 + i, new(100 + i));
        for (int i = 0; i < 9; i++) memory.Write(1, new(999));
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(17UL, memory.Read(1).Value);
        for (uint i = 0; i < 8; i++) Assert.AreEqual(100UL + i, memory.PhysicalMemory.ReadRaw(16 + i).Data.Value);
    }

    [TestMethod]
    public void HostedCtxUsesOperandMappingAndPublishesAfterOlderAliases()
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        memory.Write(2053, new(13));
        memory.HostMemory.Write(8198, new(19));
        memory.FetchInstruction(1030, false);
        memory.StoreCommand(1030, new(17));
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(17UL, memory.FetchInstruction(1030, false).Value);
        Assert.IsTrue(memory.PhysicalMemory.ReadRaw(8198).HasValidInstructionControl(true));
        Assert.AreEqual(13UL, memory.Read(1029).Value);
        Assert.IsTrue(memory.InvertLeftStoreControl);
        Assert.IsTrue(memory.InvertRightStoreControl);
    }

    [TestMethod]
    public void HostObservationAndReplacementDoNotEmulateGuestBusAccess()
    {
        var memory = Mathematical();
        memory.Write(1029, new(11));
        memory.Write(2053, new(13));
        var before = memory.GetOperandSnapshot();
        Assert.AreEqual(13UL, memory.HostMemory.Read(8197).Value);
        CollectionAssert.AreEqual(before, memory.GetOperandSnapshot());
        memory.HostMemory.Write(8197, new(17));
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(17UL, memory.Read(1029).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.HostMemory.Read(32768));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.Write(32768, new(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.SetPanel(0, default));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.GetPanel(8));
    }
}
