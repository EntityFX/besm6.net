namespace Besm6.Tests;

[TestClass]
public sealed class CoreMemoryLayoutTests
{
    [TestMethod]
    [DataRow(8u)]
    [DataRow(24u)]
    [DataRow(32768u)]
    public void EveryAddressRetainsItsOwnWordAcrossBanks(uint size)
    {
        var memory = new CoreMemory(size);
        Assert.AreEqual((int)size, memory.Size);
        for (uint address = 0; address < size; address++)
            memory.Write(address, new Word48(0xABCDEF000000UL + address));
        for (uint address = 0; address < size; address++)
            Assert.AreEqual(0xABCDEF000000UL + address, memory.Read(address).Value);
    }

    [TestMethod]
    [DataRow(0u, 0u)]
    [DataRow(16u, 16u)]
    [DataRow(32768u, uint.MaxValue)]
    public void OutOfRangeKeepsTheSameAddressAndBankDiagnostic(uint size, uint address)
    {
        var memory = new CoreMemory(size);
        string expected = $"Memory access violation at address 0x{address:X5} (Bank {address % 8})";
        Assert.AreEqual(expected, Assert.ThrowsExactly<IndexOutOfRangeException>(() => memory.Read(address)).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<IndexOutOfRangeException>(() => memory.Write(address, Word48.Zero)).Message);
    }

    [TestMethod]
    public void SizeStillMustBeDivisibleByEight()
    {
        foreach (uint size in new uint[] { 1, 7, 15, 32769 })
            Assert.AreEqual("Memory size must be divisible by the number of banks (8)",
                Assert.ThrowsExactly<ArgumentException>(() => new CoreMemory(size)).Message);
    }
}
