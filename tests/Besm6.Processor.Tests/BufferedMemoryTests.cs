namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory")]
public sealed class BufferedMemoryTests
{
    private static PhysicalMemory MemoryWithWords(uint start, uint count, bool commands = true)
    {
        var memory = new PhysicalMemory();
        for (uint address = start; address < start + count; address++)
            memory.Store(address, new(address), !commands, !commands);
        return memory;
    }

    private static uint[] Addresses(MemoryBufferEntry[] entries) =>
        entries.Select(e => e.PhysicalAddress).ToArray();

    [TestMethod]
    public void StoreForwardsOperandButInstructionStillSeesMozu()
    {
        var memory = MemoryWithWords(100, 1);
        var buffers = new BufferedMemory(memory);
        buffers.Store(100, new(200), true, true);
        Assert.AreEqual(200UL, buffers.LoadOperand(100).Value);
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, false).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, true).Value);
        Assert.AreEqual(1, buffers.GetOperandSnapshot().Length);
        Assert.AreEqual(1, buffers.GetInstructionSnapshot().Length);
        Assert.AreEqual(1, buffers.PendingWriteCount);
    }

    [TestMethod]
    public void EightStoresFillRegistersAndNinthPublishesOldest()
    {
        var memory = MemoryWithWords(100, 9);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 108; address++)
            buffers.Store(address, new(address + 1000), true, true);
        for (uint address = 100; address < 108; address++)
            Assert.AreEqual((ulong)address, memory.ReadRaw(address).Data.Value);
        Assert.AreEqual(8, buffers.PendingWriteCount);
        buffers.Store(108, new(1108), true, true);
        Assert.AreEqual(1100UL, memory.LoadOperand(100).Value);
        for (uint address = 101; address <= 108; address++)
            Assert.AreEqual((ulong)address, memory.ReadRaw(address).Data.Value);
        CollectionAssert.AreEqual(new uint[] { 108, 107, 106, 105, 104, 103, 102, 101 },
            Addresses(buffers.GetOperandSnapshot()));
    }

    public static IEnumerable<object[]> OperandVictims() => Enumerable.Range(0, 8).Select(v => new object[] { v });

    [TestMethod]
    [DynamicData(nameof(OperandVictims))]
    public void AccessingAllOtherRegistersMakesUntouchedOneTheVictim(int victim)
    {
        var memory = MemoryWithWords(100, 9);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 108; address++) buffers.Store(address, new(address + 1000), true, true);
        for (uint index = 0; index < 8; index++)
            if (index != victim) Assert.AreEqual(index + 1100UL, buffers.LoadOperand(100 + index).Value);
        buffers.Store(108, new(1108), true, true);
        for (uint index = 0; index < 8; index++)
            Assert.AreEqual(index + (index == victim ? 1100UL : 100UL), memory.ReadRaw(100 + index).Data.Value);
        Assert.IsFalse(buffers.GetOperandSnapshot().Any(e => e.PhysicalAddress == 100 + victim));
    }

    [TestMethod]
    public void RepeatedStoreReplacesControlAndRefreshesRecencyWithoutWriteback()
    {
        var memory = MemoryWithWords(100, 9);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 108; address++) buffers.Store(address, new(0), true, true);
        buffers.StoreRaw(100, new(0x1000000000000)); // zero command, replaces a zero number
        Assert.AreEqual(8, buffers.GetOperandSnapshot().Length);
        Assert.AreEqual(100u, buffers.GetOperandSnapshot()[0].PhysicalAddress);
        Assert.AreEqual(0x1000000000000UL, buffers.GetOperandSnapshot()[0].Word.RawValue);
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
        buffers.Store(108, new(0), true, true);
        Assert.AreEqual(0UL, memory.ReadRaw(101).Data.Value);
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
    }

    [TestMethod]
    public void OperandReadMissDoesNotPopulateOrTouchWriteRegisters()
    {
        var memory = MemoryWithWords(100, 20);
        var buffers = new BufferedMemory(memory);
        buffers.Store(100, new(1000), true, true);
        buffers.Store(101, new(1001), true, true);
        var before = buffers.GetOperandSnapshot();
        for (uint address = 102; address < 120; address++) Assert.AreEqual((ulong)address, buffers.LoadOperand(address).Value);
        CollectionAssert.AreEqual(before, buffers.GetOperandSnapshot());
        Assert.AreEqual(0, buffers.GetInstructionSnapshot().Length);
    }

    [TestMethod]
    public void InstructionBufferRetainsOldWordAfterPendingWritesArePublished()
    {
        var memory = MemoryWithWords(100, 5);
        var buffers = new BufferedMemory(memory);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, false).Value);
        buffers.Store(100, new(200), false, false);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(200UL, memory.FetchInstruction(100, false).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, false).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, true).Value);
        for (uint address = 101; address <= 104; address++) buffers.FetchInstruction(address, false);
        Assert.AreEqual(200UL, buffers.FetchInstruction(100, false).Value);
    }

    [TestMethod]
    public void PublishedNumberCausesCommandControlFaultAfterOldCommandIsEvicted()
    {
        var memory = MemoryWithWords(100, 5);
        var buffers = new BufferedMemory(memory);
        buffers.FetchInstruction(100, false);
        buffers.Store(100, new(200), true, true);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(200UL, buffers.LoadOperand(100).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(100, false).Value);
        for (uint address = 101; address <= 104; address++) buffers.FetchInstruction(address, false);
        var fault = Assert.ThrowsExactly<MemoryControlException>(() => buffers.FetchInstruction(100, false));
        Assert.AreEqual(100u, fault.PhysicalAddress);
        Assert.AreEqual(MemoryAccessKind.InstructionLeft, fault.AccessKind);
    }

    public static IEnumerable<object[]> InstructionVictims() => Enumerable.Range(0, 4).Select(v => new object[] { v });

    [TestMethod]
    [DynamicData(nameof(InstructionVictims))]
    public void CommandHitsSelectLeastRecentWordForReplacement(int victim)
    {
        var memory = MemoryWithWords(100, 5);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 104; address++) buffers.FetchInstruction(address, false);
        for (uint index = 0; index < 4; index++)
            if (index != victim) buffers.FetchInstruction(100 + index, true);
        buffers.FetchInstruction(104, false);
        Assert.AreEqual(4, buffers.GetInstructionSnapshot().Length);
        Assert.IsFalse(buffers.GetInstructionSnapshot().Any(e => e.PhysicalAddress == 100 + victim));
        Assert.AreEqual(104u, buffers.GetInstructionSnapshot()[0].PhysicalAddress);
        Assert.AreEqual(0, buffers.PendingWriteCount);
    }

    [TestMethod]
    public void BothHalvesShareOneWordRegisterAndAreCheckedIndependently()
    {
        var memory = new PhysicalMemory();
        memory.WriteRaw(100, new(0)); // valid left half, invalid right half
        var buffers = new BufferedMemory(memory);
        Assert.AreEqual(0UL, buffers.FetchInstruction(100, false).Value);
        memory.WriteRaw(100, new(0x1000000000000)); // does not change existing BRS
        var fault = Assert.ThrowsExactly<MemoryControlException>(() => buffers.FetchInstruction(100, true));
        Assert.AreEqual(MemoryAccessKind.InstructionRight, fault.AccessKind);
        Assert.AreEqual(1, buffers.GetInstructionSnapshot().Length);
        Assert.AreEqual(0UL, buffers.GetInstructionSnapshot()[0].Word.RawValue);
        buffers.ClearInstructionBuffer();
        Assert.AreEqual(0UL, buffers.FetchInstruction(100, true).Value);
    }

    [TestMethod]
    public void OperandControlComesFromWriteRegisterAndBadControlIsPreserved()
    {
        var memory = MemoryWithWords(100, 1);
        var buffers = new BufferedMemory(memory);
        buffers.StoreRaw(100, new(0));
        var fault = Assert.ThrowsExactly<MemoryControlException>(() => buffers.LoadOperand(100));
        Assert.AreEqual(100u, fault.PhysicalAddress);
        Assert.AreEqual(MemoryAccessKind.OperandRead, fault.AccessKind);
        Assert.AreEqual(0UL, buffers.GetOperandSnapshot()[0].Word.RawValue);
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(0UL, memory.ReadRaw(100).RawValue);
        Assert.ThrowsExactly<MemoryControlException>(() => memory.LoadOperand(100));
    }

    [TestMethod]
    public void EveryRawControlCombinationSurvivesEvictionAndExplicitWriteback()
    {
        var memory = new PhysicalMemory();
        var buffers = new BufferedMemory(memory);
        for (uint index = 0; index < 12; index++)
            buffers.StoreRaw(index + 100, new(0xABCDEF123456UL | ((ulong)(index % 4) << 48)));
        buffers.WriteBackPendingOperands();
        for (uint index = 0; index < 12; index++)
            Assert.AreEqual(0xABCDEF123456UL | ((ulong)(index % 4) << 48), memory.ReadRaw(index + 100).RawValue);
    }

    [TestMethod]
    public void InstructionReplacementAndClearingNeverPublishOperands()
    {
        var memory = MemoryWithWords(100, 20);
        var buffers = new BufferedMemory(memory);
        buffers.Store(100, new(1000), true, true);
        var before = buffers.GetOperandSnapshot();
        for (uint address = 100; address < 120; address++) buffers.FetchInstruction(address, false);
        buffers.ClearInstructionBuffer();
        Assert.AreEqual(0, buffers.GetInstructionSnapshot().Length);
        CollectionAssert.AreEqual(before, buffers.GetOperandSnapshot());
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
    }

    [TestMethod]
    public void ExplicitWritebackRetainsRecencyAndWarmOperandRegisters()
    {
        var memory = MemoryWithWords(100, 8);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 108; address++) buffers.Store(address, new(address + 1000), true, true);
        buffers.LoadOperand(100);
        uint[] order = Addresses(buffers.GetOperandSnapshot());
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(0, buffers.PendingWriteCount);
        Assert.IsTrue(buffers.GetOperandSnapshot().All(e => !e.PendingWrite));
        CollectionAssert.AreEqual(order, Addresses(buffers.GetOperandSnapshot()));
        for (uint address = 100; address < 108; address++) Assert.AreEqual(address + 1000UL, memory.LoadOperand(address).Value);
        memory.Store(100, new(2000), true, true); // explicit MOZU-only host update
        Assert.AreEqual(1100UL, buffers.LoadOperand(100).Value);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(2000UL, memory.LoadOperand(100).Value); // clean registers are not written again
        buffers.Store(100, new(3000), true, true);
        Assert.AreEqual(1, buffers.PendingWriteCount);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(3000UL, memory.LoadOperand(100).Value);
    }

    [TestMethod]
    public void EvictingCleanOperandRegisterDoesNotOverwriteDirectMozuUpdate()
    {
        var memory = MemoryWithWords(100, 9);
        var buffers = new BufferedMemory(memory);
        for (uint address = 100; address < 108; address++) buffers.Store(address, new(address + 1000), true, true);
        buffers.WriteBackPendingOperands();
        memory.Store(100, new(2000), true, true);
        buffers.Store(108, new(1108), true, true);
        Assert.AreEqual(2000UL, memory.LoadOperand(100).Value);
        Assert.AreEqual(1, buffers.PendingWriteCount);
    }

    [TestMethod]
    public void SnapshotsAreIndependentAndDoNotCountAsAccesses()
    {
        var memory = MemoryWithWords(100, 2);
        var buffers = new BufferedMemory(memory);
        buffers.Store(100, new(1000), true, true);
        buffers.Store(101, new(1001), true, true);
        buffers.FetchInstruction(100, false);
        buffers.FetchInstruction(101, false);
        var operands = buffers.GetOperandSnapshot();
        var instructions = buffers.GetInstructionSnapshot();
        operands[0] = default;
        instructions[0] = default;
        CollectionAssert.AreEqual(new uint[] { 101, 100 }, Addresses(buffers.GetOperandSnapshot()));
        CollectionAssert.AreEqual(new uint[] { 101, 100 }, Addresses(buffers.GetInstructionSnapshot()));
        Assert.AreEqual(2, buffers.PendingWriteCount);
    }

    [TestMethod]
    public void AliasesSharePhysicalBuffersAndRemapDoesNotRetargetPendingPhysicalWrite()
    {
        var memory = new PhysicalMemory();
        memory.Store(7 * 1024 + 10, new(100), false, false);
        memory.Store(8 * 1024 + 10, new(200), false, false);
        var buffers = new BufferedMemory(memory);
        var map = new PageAssignment();
        map.SetPhysicalPage(2, 7);
        map.SetPhysicalPage(3, 7);
        uint first = map.ResolveOperand(2 * 1024 + 10, true, false, false, false).Address;
        buffers.Store(first, new(300), false, false);
        uint alias = map.ResolveOperand(3 * 1024 + 10, false, false, false, false).Address;
        Assert.AreEqual(300UL, buffers.LoadOperand(alias).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(alias, false).Value);
        map.SetPhysicalPage(3, 8);
        uint remapped = map.ResolveOperand(3 * 1024 + 10, false, false, false, false).Address;
        Assert.AreEqual(200UL, buffers.LoadOperand(remapped).Value);
        buffers.WriteBackPendingOperands();
        Assert.AreEqual(300UL, memory.LoadOperand(first).Value);
        Assert.AreEqual(200UL, memory.LoadOperand(remapped).Value);
        // This asserts this physical-address API, not an unverified hardware remap sequence.
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(32767u)]
    public void PhysicalBoundaryAddressesAreValidBufferTags(uint address)
    {
        var memory = new PhysicalMemory();
        memory.Store(address, new(0), false, false);
        var buffers = new BufferedMemory(memory);
        buffers.Store(address, new(1), false, false);
        Assert.AreEqual(1UL, buffers.LoadOperand(address).Value);
        Assert.AreEqual(0UL, buffers.FetchInstruction(address, false).Value);
        buffers.WriteBackPendingOperands();
        buffers.ClearInstructionBuffer();
        Assert.AreEqual(1UL, buffers.FetchInstruction(address, true).Value);
    }

    [TestMethod]
    [DataRow(32768u)]
    [DataRow(uint.MaxValue)]
    public void InvalidAddressCannotChangeOrEvictEitherBuffer(uint address)
    {
        var memory = MemoryWithWords(100, 8);
        var buffers = new BufferedMemory(memory);
        for (uint a = 100; a < 108; a++) buffers.Store(a, new(a + 1000), true, true);
        for (uint a = 100; a < 104; a++) buffers.FetchInstruction(a, false);
        var operands = buffers.GetOperandSnapshot();
        var instructions = buffers.GetInstructionSnapshot();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffers.Store(address, new(0), false, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffers.StoreRaw(address, new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffers.LoadOperand(address));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffers.FetchInstruction(address, false));
        CollectionAssert.AreEqual(operands, buffers.GetOperandSnapshot());
        CollectionAssert.AreEqual(instructions, buffers.GetInstructionSnapshot());
        Assert.AreEqual(100UL, memory.ReadRaw(100).Data.Value);
    }
}
