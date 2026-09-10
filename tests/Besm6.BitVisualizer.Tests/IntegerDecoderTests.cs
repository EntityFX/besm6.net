using System.Numerics;
using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.Tests;

[TestClass]
public sealed class IntegerDecoderTests
{
    [TestMethod]
    [DataRow(8, "11111111", "255")]
    [DataRow(16, "1000000000000000", "32768")]
    [DataRow(32, "10000000000000000000000000000000", "2147483648")]
    [DataRow(64, "1111111111111111111111111111111111111111111111111111111111111111", "18446744073709551615")]
    public void Decode_UnsignedUsesAllPositiveWeights(int width, string binary, string expected)
    {
        BitPattern bits = Bits(binary);
        Assert.AreEqual(width, bits.Width);

        IntegerDecodeResult result = IntegerDecoder.Decode(bits, IntegerInterpretation.Unsigned);

        Assert.AreEqual(BigInteger.Parse(expected), result.Value);
        Assert.AreEqual(IntegerInterpretation.Unsigned, result.Interpretation);
        Assert.IsFalse(result.Fields.Any(field => field.Kind == BitFieldKind.Sign));
    }

    [TestMethod]
    [DataRow("10000000", "-128")]
    [DataRow("11111111", "-1")]
    [DataRow("01111111", "127")]
    [DataRow("1000000000000000", "-32768")]
    public void Decode_SignedUsesTwosComplement(string binary, string expected)
    {
        IntegerDecodeResult result = IntegerDecoder.Decode(Bits(binary), IntegerInterpretation.Signed);

        Assert.AreEqual(BigInteger.Parse(expected), result.Value);
        Assert.AreEqual(BitFieldKind.Sign, result.Fields[0].Kind);
        StringAssert.Contains(result.Report.Render(), "дополнительном коде");
    }

    [TestMethod]
    public void Decode_ExplainsSetBitWeights()
    {
        IntegerDecodeResult result = IntegerDecoder.Decode(Bits("00000101"), IntegerInterpretation.Unsigned);

        StringAssert.Contains(result.Report.Render(), "2^2 = 4");
        StringAssert.Contains(result.Report.Render(), "2^0 = 1");
        StringAssert.Contains(result.Report.Render(), "4 + 1 = 5");
    }

    [TestMethod]
    public void Decode_RejectsUnsupportedWidth()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegerDecoder.Decode(new BitPattern(7, 0), IntegerInterpretation.Unsigned));
    }

    private static BitPattern Bits(string binary) =>
        BitPattern.FromMsbFirst(binary.Select(character => character == '1'));
}
