using Besm6.Architecture.Visualization;

namespace Besm6.Architecture.Tests.Visualization;

[TestClass]
public sealed class Besm6FloatDecoderTests
{
    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    [DataRow(-3.5)]
    [DataRow(12345.25)]
    public void Decode_UsesCanonicalWord48Conversion(double source)
    {
        Word48 word = Word48.FromDouble(source);

        Besm6FloatDecodeResult result = Besm6FloatDecoder.Decode(new BitPattern(48, word.Value));

        Assert.AreEqual(word, result.Word);
        Assert.AreEqual(word.ToDouble(), result.Value);
        Assert.AreEqual(47, result.Fields.Max(field => field.MostSignificantBit));
        StringAssert.Contains(result.Report.Render(), "2^40");
    }

    [TestMethod]
    public void Decode_NegativeNumberSignExtendsMantissa()
    {
        Word48 word = Word48.FromDouble(-1.0);

        Besm6FloatDecodeResult result = Besm6FloatDecoder.Decode(new BitPattern(48, word.Value));

        Assert.IsLessThan(0L, result.Mantissa);
        Assert.AreEqual(BitFieldKind.Sign, result.Fields.Single(field => field.MostSignificantBit == 40 && field.LeastSignificantBit == 40).Kind);
    }

    [TestMethod]
    public void Decode_RejectsNonWordWidth()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Besm6FloatDecoder.Decode(new BitPattern(47, 0)));
    }
}
