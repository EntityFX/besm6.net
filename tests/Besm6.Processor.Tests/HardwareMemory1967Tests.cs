namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareMemory1967")]
public sealed class HardwareMemory1967Tests
{
    // Literal bits derived by counting the printed 24-bit halves, not by Form().
    [TestMethod]
    [DataRow(0UL, 0x1000000000000UL, 0x2000000000000UL)]
    [DataRow(1UL, 0x0000000000001UL, 0x3000000000001UL)]
    [DataRow(0x1000000UL, 0x3000001000000UL, 0x0000001000000UL)]
    [DataRow(0x1000001UL, 0x2000001000001UL, 0x1000001000001UL)]
    [DataRow(0xFFFFFFFFFFFFUL, 0x1FFFFFFFFFFFFUL, 0x2FFFFFFFFFFFFUL)]
    public void LiteralCommandAndNumberControl(ulong data, ulong command, ulong number)
    {
        Assert.AreEqual(command, MemoryWord50.Form(new(data), false, false).RawValue);
        Assert.AreEqual(number, MemoryWord50.Form(new(data), true, true).RawValue);
        var c = new MemoryWord50(command);
        var n = new MemoryWord50(number);
        Assert.AreEqual(data, c.Data.Value);
        Assert.AreEqual(data, n.Data.Value);
        Assert.IsTrue(c.HasValidInstructionControl(false));
        Assert.IsTrue(c.HasValidInstructionControl(true));
        Assert.IsTrue(c.HasValidOperandControl);
        Assert.IsFalse(n.HasValidInstructionControl(false));
        Assert.IsFalse(n.HasValidInstructionControl(true));
        Assert.IsTrue(n.HasValidOperandControl);
    }

    [TestMethod]
    [DataRow(false, false, true, true, true)]
    [DataRow(false, true, true, false, false)]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, true, false, false, true)]
    public void AllControlFormationModes(bool invertLeft, bool invertRight,
        bool leftValid, bool rightValid, bool operandValid)
    {
        foreach (ulong payload in new[] { 0UL, 1UL, 0xABCDEF123456UL, Word48.Mask48 })
        {
            var word = MemoryWord50.Form(new(payload), invertLeft, invertRight);
            Assert.AreEqual(payload, word.Data.Value);
            Assert.AreEqual(leftValid, word.HasValidInstructionControl(false));
            Assert.AreEqual(rightValid, word.HasValidInstructionControl(true));
            Assert.AreEqual(operandValid, word.HasValidOperandControl);
        }
    }

    public static IEnumerable<object[]> Bits() => Enumerable.Range(0, 50).Select(b => new object[] { b });

    [TestMethod]
    [DynamicData(nameof(Bits))]
    public void EverySingleBitCorruptionIsDetectedInItsHalf(int bit)
    {
        const ulong original = 0x1123456789ABCUL;
        // A literal zero command has an even left half and odd right half.
        var corrupted = new MemoryWord50(0x1000000000000UL ^ (1UL << bit));
        bool leftChanged = bit is >= 24 and <= 47 or 49;
        Assert.AreEqual(!leftChanged, corrupted.HasValidInstructionControl(false));
        Assert.AreEqual(leftChanged, corrupted.HasValidInstructionControl(true));
        Assert.IsFalse(corrupted.HasValidOperandControl);
        // Payload corruption at either end must be preserved, not masked to 48 bits.
        var raw = new MemoryWord50(original ^ (1UL << bit));
        Assert.AreEqual(original ^ (1UL << bit), raw.RawValue);
    }

    [TestMethod]
    public void RawWordRejectsBitsAboveFifty()
    {
        Assert.AreEqual(MemoryWord50.Mask50, new MemoryWord50(MemoryWord50.Mask50).RawValue);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MemoryWord50(1UL << 50));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MemoryWord50(ulong.MaxValue));
    }

    [TestMethod]
    public void PhysicalAddressesKeepControlAndBankIdentity()
    {
        var memory = new PhysicalMemory1967();
        for (uint address = 0; address < PhysicalMemory1967.WordCount; address++)
        {
            ulong raw = address | ((ulong)(address % 4) << 48);
            memory.WriteRaw(address, new(raw));
        }
        for (uint address = 0; address < PhysicalMemory1967.WordCount; address++)
        {
            Assert.AreEqual(address | ((ulong)(address % 4) << 48), memory.ReadRaw(address).RawValue);
            Assert.AreEqual(address % 8, PhysicalMemory1967.GetBank(address));
            Assert.AreEqual(address / 8, PhysicalMemory1967.GetBankOffset(address));
        }
    }

    [TestMethod]
    [DataRow(32768u)]
    [DataRow(uint.MaxValue)]
    public void PhysicalAddressesNeverWrap(uint address)
    {
        var memory = new PhysicalMemory1967();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.ReadRaw(address));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory.WriteRaw(address, new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PhysicalMemory1967.GetBank(address));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PhysicalMemory1967.GetBankOffset(address));
        Assert.AreEqual(0UL, memory.ReadRaw(0).RawValue);
    }

    [TestMethod]
    public void ControlFaultsHaveAddressAndAccessAndPreserveStorage()
    {
        var memory = new PhysicalMemory1967();
        memory.WriteRaw(1234, new(0x2000000000000)); // zero number
        Assert.AreEqual(0UL, memory.LoadOperand(1234).Value);
        foreach (bool right in new[] { false, true })
        {
            var fault = Assert.ThrowsExactly<MemoryControlException>(() => memory.FetchInstruction(1234, right));
            Assert.AreEqual(1234u, fault.PhysicalAddress);
            Assert.AreEqual(right ? MemoryAccessKind1967.InstructionRight : MemoryAccessKind1967.InstructionLeft,
                fault.AccessKind);
        }
        Assert.AreEqual(0x2000000000000UL, memory.ReadRaw(1234).RawValue);
        memory.WriteRaw(1234, new(0)); // wrong total control, left command still valid
        Assert.AreEqual(0UL, memory.FetchInstruction(1234, false).Value);
        var operandFault = Assert.ThrowsExactly<MemoryControlException>(() => memory.LoadOperand(1234));
        Assert.AreEqual(MemoryAccessKind1967.OperandRead, operandFault.AccessKind);
        Assert.AreEqual(0UL, memory.ReadRaw(1234).RawValue);
    }

    [TestMethod]
    public void PhysicalStoreReplacesPayloadAndControlBits()
    {
        var memory = new PhysicalMemory1967();
        memory.Store(8, new(1), false, false);
        Assert.AreEqual(1UL, memory.FetchInstruction(8, false).Value);
        Assert.AreEqual(1UL, memory.FetchInstruction(8, true).Value);
        memory.Store(8, new(1), true, true);
        Assert.AreEqual(0x3000000000001UL, memory.ReadRaw(8).RawValue);
        Assert.ThrowsExactly<MemoryControlException>(() => memory.FetchInstruction(8, false));
        Assert.AreEqual(1UL, memory.LoadOperand(8).Value);
    }

    [TestMethod]
    public void EveryPageAndOffsetTranslateWithoutLosingLowBits()
    {
        var map = new PageAssignment1967();
        for (uint page = 0; page < 32; page++) map.SetPhysicalPage(page, 31 - page);
        for (uint address = 1; address < 32768; address++)
        {
            var resolved = map.ResolveOperand(address, false, false, false, false);
            Assert.AreEqual(MemoryAddressKind1967.PhysicalMemory, resolved.Kind);
            Assert.AreEqual((31 - address / 1024) * 1024 + address % 1024, resolved.Address);
        }
    }

    [TestMethod]
    public void AliasAndRemapSelectActualPhysicalStorage()
    {
        var map = new PageAssignment1967();
        var memory = new PhysicalMemory1967();
        map.SetPhysicalPage(2, 7);
        map.SetPhysicalPage(3, 7);
        var first = map.ResolveOperand(2 * 1024 + 27, true, false, false, false);
        memory.Store(first.Address, new(0x1234), false, false);
        Assert.AreEqual(0x1234UL,
            memory.FetchInstruction(map.ResolveInstruction(3 * 1024 + 27, false, false).Address, false).Value);
        map.SetPhysicalPage(3, 8);
        memory.Store(8 * 1024 + 27, new(0x5678), false, false);
        Assert.AreEqual(0x5678UL,
            memory.FetchInstruction(map.ResolveInstruction(3 * 1024 + 27, false, false).Address, false).Value);
        Assert.AreEqual(0x1234UL, memory.ReadRaw(first.Address).Data.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OperandProtectionUsesMathematicalPageEvenWhenAssignmentIsBlocked(bool write)
    {
        var map = new PageAssignment1967 { OperandProtectionMask = 1u << 3 };
        map.SetPhysicalPage(3, 5);
        map.SetPhysicalPage(4, 3);
        foreach (bool blocked in new[] { false, true })
        {
            var fault = Assert.ThrowsExactly<MemoryProtection1967Exception>(() =>
                map.ResolveOperand(3 * 1024 + 9, write, blocked, false, false));
            Assert.AreEqual(3u, fault.MathematicalPage);
            Assert.AreEqual(3081u, fault.MathematicalAddress);
            Assert.AreEqual(write ? MemoryAccessKind1967.OperandWrite : MemoryAccessKind1967.OperandRead,
                fault.AccessKind);
        }
        Assert.AreEqual(3081u, map.ResolveOperand(4 * 1024 + 9, write, false, false, false).Address);
        Assert.AreEqual(5129u, map.ResolveOperand(3 * 1024 + 9, write, false, true, false).Address);
        Assert.AreEqual(3081u, map.ResolveOperand(3 * 1024 + 9, write, true, true, false).Address);
    }

    [TestMethod]
    public void InstructionProtectionIsAssignmentZeroAndIndependentOfOperandMask()
    {
        var map = new PageAssignment1967 { OperandProtectionMask = uint.MaxValue };
        foreach (bool right in new[] { false, true })
        {
            var fault = Assert.ThrowsExactly<MemoryProtection1967Exception>(() =>
                map.ResolveInstruction(2 * 1024 + 55, false, right));
            Assert.AreEqual(right ? MemoryAccessKind1967.InstructionRight : MemoryAccessKind1967.InstructionLeft,
                fault.AccessKind);
        }
        // A zero page assignment still permits operands if operand protection is blocked.
        Assert.AreEqual(55u, map.ResolveOperand(2 * 1024 + 55, false, false, true, false).Address);
        map.SetPhysicalPage(2, 6);
        Assert.AreEqual(6199u, map.ResolveInstruction(2 * 1024 + 55, false, false).Address);
        Assert.AreEqual(2103u, map.ResolveInstruction(2 * 1024 + 55, true, false).Address);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZeroOperandAndPanelSelectionAreNotPhysicalMemory(bool write)
    {
        var map = new PageAssignment1967 { OperandProtectionMask = uint.MaxValue };
        map.SetPhysicalPage(0, 9);
        foreach (bool blocked in new[] { false, true })
            Assert.AreEqual(new ResolvedMemoryAddress1967(MemoryAddressKind1967.ZeroOperand, 0),
                map.ResolveOperand(0, write, blocked, false, false));
        for (uint address = 1; address <= 7; address++)
        {
            Assert.AreEqual(new ResolvedMemoryAddress1967(MemoryAddressKind1967.PanelRegister, address),
                map.ResolveOperand(address, write, true, false, true));
            Assert.AreEqual(new ResolvedMemoryAddress1967(MemoryAddressKind1967.PanelRegister, address),
                map.ResolveInstruction(address, true, false));
            Assert.ThrowsExactly<MemoryProtection1967Exception>(() =>
                map.ResolveOperand(address, write, true, false, false));
            Assert.AreEqual(9 * 1024 + address,
                map.ResolveOperand(address, write, false, true, false).Address);
        }
        Assert.ThrowsExactly<MemoryProtection1967Exception>(() => map.ResolveOperand(8, write, true, false, true));
    }

    [TestMethod]
    public void InvalidMappingAndMathematicalAddressCannotWrap()
    {
        var map = new PageAssignment1967();
        map.SetPhysicalPage(31, 17);
        foreach (uint invalid in new[] { 32u, uint.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.SetPhysicalPage(31, invalid));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.SetPhysicalPage(invalid, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.GetPhysicalPage(invalid));
        }
        Assert.AreEqual(17u, map.GetPhysicalPage(31));
        foreach (uint invalid in new[] { 32768u, uint.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.ResolveInstruction(invalid, true, false));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.ResolveOperand(invalid, false, true, true, true));
        }
    }
}
