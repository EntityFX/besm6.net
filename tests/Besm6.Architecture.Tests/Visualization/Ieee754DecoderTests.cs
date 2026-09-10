using System.Numerics;
using Besm6.Architecture.Visualization;

namespace Besm6.Architecture.Tests.Visualization;

[TestClass]
public sealed class Ieee754DecoderTests
{
    [TestMethod]
    [DataRow("0011110000000000", 1.0, Ieee754Class.Normal)]
    [DataRow("11000000001000000000000000000000", -2.5, Ieee754Class.Normal)]
    [DataRow("1011111111110000000000000000000000000000000000000000000000000000", -1.0, Ieee754Class.Normal)]
    public void Decode_KnownPatterns(string binary, double expected, Ieee754Class expectedClass)
    {
        Ieee754DecodeResult result = Ieee754Decoder.Decode(Bits(binary));

        Assert.AreEqual(expected, result.Value);
        Assert.AreEqual(expectedClass, result.Classification);
        CollectionAssert.AreEqual(
            new[] { BitFieldKind.Sign, BitFieldKind.Exponent, BitFieldKind.Fraction },
            result.Fields.Select(field => field.Kind).ToArray());
    }

    [TestMethod]
    [DataRow("0000000000000000", Ieee754Class.PositiveZero)]
    [DataRow("1000000000000000", Ieee754Class.NegativeZero)]
    [DataRow("0111110000000000", Ieee754Class.PositiveInfinity)]
    [DataRow("1111110000000000", Ieee754Class.NegativeInfinity)]
    [DataRow("0111111000000000", Ieee754Class.NaN)]
    [DataRow("0000000000000001", Ieee754Class.Subnormal)]
    public void Decode_ClassifiesBinary16Specials(string binary, Ieee754Class expected)
    {
        Ieee754DecodeResult result = Ieee754Decoder.Decode(Bits(binary));

        Assert.AreEqual(expected, result.Classification);
    }

    [TestMethod]
    public void Decode_NormalReportShowsFieldFormula()
    {
        Ieee754DecodeResult result = Ieee754Decoder.Decode(Bits("0011110000000000"));

        StringAssert.Contains(result.Report.Render(), "(-1)^0");
        StringAssert.Contains(result.Report.Render(), "2^(15 - 15)");
        StringAssert.Contains(result.Report.Render(), "скрытая единица");
    }

    [TestMethod]
    public void Decode_SubnormalReportExplainsMissingHiddenOne()
    {
        Ieee754DecodeResult result = Ieee754Decoder.Decode(Bits("0000000000000001"));

        StringAssert.Contains(result.Report.Render(), "субнормальное");
        StringAssert.Contains(result.Report.Render(), "скрытой единицы нет");
    }

    [TestMethod]
    public void Decode_RejectsUnsupportedWidth()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Ieee754Decoder.Decode(new BitPattern(8, BigInteger.Zero)));
    }

    private static BitPattern Bits(string binary) =>
        BitPattern.FromMsbFirst(binary.Select(character => character == '1'));
}
