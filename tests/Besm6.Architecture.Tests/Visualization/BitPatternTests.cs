using System.Numerics;
using Besm6.Architecture.Visualization;

namespace Besm6.Architecture.Tests.Visualization;

[TestClass]
public sealed class BitPatternTests
{
    [TestMethod]
    public void FromMsbFirst_PreservesVisualOrder()
    {
        BitPattern bits = BitPattern.FromMsbFirst([true, false, true, true]);

        Assert.AreEqual(4, bits.Width);
        Assert.AreEqual(new BigInteger(11), bits.UnsignedValue);
        Assert.AreEqual("1011", bits.ToBinaryString());
        Assert.IsTrue(bits.GetBit(3));
        Assert.IsTrue(bits.GetBit(0));
    }

    [TestMethod]
    public void Constructor_RejectsValueOutsideWidth()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BitPattern(8, 256));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BitPattern(8, -1));
    }

    [TestMethod]
    public void Formatting_PadsToDeclaredWidth()
    {
        var bits = new BitPattern(12, 0x2A);

        Assert.AreEqual("000000101010", bits.ToBinaryString());
        Assert.AreEqual("0052", bits.ToOctalString());
        Assert.AreEqual("02A", bits.ToHexString());
    }

    [TestMethod]
    public void GetBit_RejectsIndexOutsidePattern()
    {
        var bits = new BitPattern(8, 1);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => bits.GetBit(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => bits.GetBit(8));
    }
}
