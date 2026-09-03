namespace Besm6.EduCpu.Tests;

[TestClass]
public class MemoryTests
{
    [TestMethod]
    public void ReadWrite_RoundTrip()
    {
        Memory mem = new();
        Word48 w = Word48.FromOctal("1234567777777776");
        mem.Write(100, w);
        Assert.AreEqual(w, mem.Read(100));
    }

    [TestMethod]
    public void Boundary_AddressesWork()
    {
        Memory mem = new();
        mem.Write(0, new Word48(1));
        mem.Write(Memory.MaxAddress, new Word48(2));
        Assert.AreEqual((ulong)1, mem.Read(0).Raw);
        Assert.AreEqual((ulong)2, mem.Read(Memory.MaxAddress).Raw);
    }

    [TestMethod]
    public void OutOfRange_Read_ThrowsWithOctalAddress()
    {
        Memory mem = new();
        Exception ex = Assert.Throws<OutOfRangeAddressException>(() => mem.Read(32768));
        StringAssert.Contains(ex.Message, "0100000"); // адрес 32768 в восьмеричной записи
    }

    [TestMethod]
    public void OutOfRange_Write_Throws()
        => Assert.Throws<OutOfRangeAddressException>(() => new Memory().Write(40000, new Word48(0)));
}
