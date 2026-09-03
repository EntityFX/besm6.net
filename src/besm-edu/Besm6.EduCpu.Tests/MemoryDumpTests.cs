namespace Besm6.EduCpu.Tests;

[TestClass]
public class MemoryDumpTests
{
    [TestMethod]
    public void Format_HeaderAndLines()
    {
        Memory mem = new();
        mem.Write(100, Word48.FromOctal("5"));
        mem.Write(101, Word48.FromOctal("7"));
        string[] lines = MemoryDump.Format(mem, 100, 101).Split('\n');
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("ДАМП ПАМЯТИ", lines[0]);
        Assert.AreEqual("000144  0000000000000005", lines[1]);
        Assert.AreEqual("000145  0000000000000007", lines[2]);
    }

    [TestMethod]
    public void Format_SingleAddress()
    {
        Memory mem = new();
        mem.Write(0, Word48.FromOctal("1"));
        Assert.AreEqual("ДАМП ПАМЯТИ\n000000  0000000000000001", MemoryDump.Format(mem, 0, 0));
    }

    [TestMethod]
    public void Format_ReversedRange_Throws()
        => Assert.Throws<ArgumentException>(() => MemoryDump.Format(new Memory(), 10, 5));

    [TestMethod]
    public void Oct_TryParse_ValidAndInvalid()
    {
        Assert.IsTrue(Oct.TryParse("077777", out ushort v));
        Assert.AreEqual((ushort)32767, v);
        Assert.IsFalse(Oct.TryParse("200000", out _));
        Assert.IsFalse(Oct.TryParse("8", out _));
        Assert.IsFalse(Oct.TryParse("", out _));
    }
}