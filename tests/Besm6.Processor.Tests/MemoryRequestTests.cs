namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory")]
public sealed class MemoryRequestTests
{
    [TestMethod]
    public void EveryAddressPreservesFifteenBitsAndPhysicalMarker()
    {
        for (uint address = 0; address < 32768; address++)
        {
            var mathematical = new MemoryRequestAddress(address, false);
            var physical = new MemoryRequestAddress(address, true);
            Assert.AreEqual(address, mathematical.EncodedValue);
            Assert.AreEqual(address + 32768, physical.EncodedValue);
            Assert.AreEqual(address, physical.Address);
            Assert.AreNotEqual(mathematical, physical);
        }
        Assert.AreEqual(0u, default(MemoryRequestAddress).EncodedValue);
    }

    [TestMethod]
    [DataRow(32768u)]
    [DataRow(uint.MaxValue)]
    public void InvalidRequestIsRejectedWithoutMasking(uint address)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MemoryRequestAddress(address, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MemoryRequestAddress(address, true));
    }

    [TestMethod]
    public void TranslationOccursAgainstCurrentAssignmentAndPhysicalRequestBypassesIt()
    {
        var assignment = new PageAssignment { OperandProtectionMask = uint.MaxValue };
        var mathematical = new MemoryRequestAddress(2055, false); // page 2, offset 7
        var physical = new MemoryRequestAddress(2055, true);
        assignment.SetPhysicalPage(2, 11);
        Assert.AreEqual(11271u, assignment.TranslateRequest(mathematical));
        Assert.AreEqual(2055u, assignment.TranslateRequest(physical));
        assignment.SetPhysicalPage(2, 19);
        Assert.AreEqual(19463u, assignment.TranslateRequest(mathematical));
        Assert.AreEqual(2055u, assignment.TranslateRequest(physical));
        // Protection is an admission check, not a second check during transfer.
        Assert.AreEqual(uint.MaxValue, assignment.OperandProtectionMask);
    }

    [TestMethod]
    public void EveryPageAndOffsetTranslatesWithoutLosingLowBits()
    {
        var assignment = new PageAssignment();
        for (uint page = 0; page < 32; page++) assignment.SetPhysicalPage(page, 31 - page);
        for (uint address = 0; address < 32768; address++)
        {
            uint expected = (31 - address / 1024) * 1024 + address % 1024;
            Assert.AreEqual(expected, assignment.TranslateRequest(new(address, false)));
            Assert.AreEqual(address, assignment.TranslateRequest(new(address, true)));
        }
    }

    [TestMethod]
    public void RequestTranslationDoesNotApplyOperandZeroOrPanelSemantics()
    {
        var assignment = new PageAssignment();
        assignment.SetPhysicalPage(0, 5);
        Assert.AreEqual(5120u, assignment.TranslateRequest(new(0, false)));
        Assert.AreEqual(0u, assignment.TranslateRequest(new(0, true)));
        Assert.AreEqual(5123u, assignment.TranslateRequest(new(3, false)));
        Assert.AreEqual(3u, assignment.TranslateRequest(new(3, true)));
        Assert.AreEqual(MemoryAddressKind.ZeroOperand, assignment.ResolveOperand(0, false, false, true, false).Kind);
        Assert.AreEqual(MemoryAddressKind.PanelRegister, assignment.ResolveOperand(3, false, true, true, false).Kind);
    }

    [TestMethod]
    public void MappingChangePublishesPendingWritesAtOldPhysicalAddresses()
    {
        var physical = new PhysicalMemory();
        var buffers = new BufferedMemory(physical);
        var assignment = new PageAssignment();
        var controller = new MemoryMappingController(assignment, buffers);
        assignment.SetPhysicalPage(2, 11);
        var request = new MemoryRequestAddress(2055, false);
        uint oldAddress = assignment.TranslateRequest(request);
        uint newAddress = 19463;
        physical.Store(newAddress, new(999), true, true);
        buffers.Store(oldAddress, new(123), true, true);
        Assert.AreEqual(1, buffers.PendingWriteCount);
        controller.SetPhysicalPage(2, 19);
        Assert.AreEqual(0, buffers.PendingWriteCount);
        Assert.AreEqual(123UL, physical.LoadOperand(oldAddress).Value);
        Assert.AreEqual(999UL, physical.LoadOperand(newAddress).Value);
        Assert.AreEqual(newAddress, assignment.TranslateRequest(request));
        Assert.AreEqual(oldAddress, buffers.GetOperandSnapshot().Single().PhysicalAddress);
    }

    [TestMethod]
    public void GroupChangePreservesBRSAndRecencyAndDoesNotRepublishCleanWords()
    {
        var physical = new PhysicalMemory();
        physical.Store(8, new(100), false, false);
        var buffers = new BufferedMemory(physical);
        var assignment = new PageAssignment();
        var controller = new MemoryMappingController(assignment, buffers);
        buffers.FetchInstruction(8, false);
        buffers.Store(8, new(200), true, true);
        buffers.Store(9, new(300), true, true);
        var commands = buffers.GetInstructionSnapshot();
        var operands = buffers.GetOperandSnapshot();
        controller.ImportAssignmentGroup(3, new(0x20C41));
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 },
            Enumerable.Range(12, 4).Select(p => assignment.GetPhysicalPage((uint)p)).ToArray());
        CollectionAssert.AreEqual(commands, buffers.GetInstructionSnapshot());
        CollectionAssert.AreEqual(operands.Select(e => e with { PendingWrite = false }).ToArray(), buffers.GetOperandSnapshot());
        Assert.AreEqual(100UL, buffers.FetchInstruction(8, false).Value);
        Assert.AreEqual(200UL, physical.LoadOperand(8).Value);
        physical.Store(8, new(400), false, false);
        controller.SetPhysicalPage(31, 7);
        Assert.AreEqual(400UL, physical.LoadOperand(8).Value);
        Assert.AreEqual(100UL, buffers.FetchInstruction(8, false).Value);
    }

    [TestMethod]
    public void InvalidChangeHasNoWritebackOrPartialAssignment()
    {
        var physical = new PhysicalMemory();
        physical.Store(8, new(111), true, true);
        var buffers = new BufferedMemory(physical);
        var assignment = new PageAssignment();
        var controller = new MemoryMappingController(assignment, buffers);
        assignment.SetPhysicalPage(0, 7);
        buffers.Store(8, new(222), true, true);
        var before = buffers.GetOperandSnapshot();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.SetPhysicalPage(32, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.SetPhysicalPage(0, 32));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.ImportAssignmentGroup(8, Word48.Zero));
        for (int field = 0; field < 4; field++)
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.ImportAssignmentGroup(0, new(1UL << (28 + field))));
        CollectionAssert.AreEqual(before, buffers.GetOperandSnapshot());
        Assert.AreEqual(111UL, physical.LoadOperand(8).Value);
        Assert.AreEqual(7u, assignment.GetPhysicalPage(0));
    }

    [TestMethod]
    public void NullComponentsAreRejected()
    {
        var buffers = new BufferedMemory(new PhysicalMemory());
        Assert.ThrowsExactly<ArgumentNullException>(() => new MemoryMappingController(null!, buffers));
        Assert.ThrowsExactly<ArgumentNullException>(() => new MemoryMappingController(new PageAssignment(), null!));
    }
}
