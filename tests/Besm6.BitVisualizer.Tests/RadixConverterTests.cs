using System.Numerics;
using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.Tests;

[TestClass]
public sealed class RadixConverterTests
{
    [TestMethod]
    [DataRow("11111111", 2, 10, "255")]
    [DataRow("777", 8, 16, "1FF")]
    [DataRow("-32768", 10, 16, "-8000")]
    [DataRow("Z", 36, 2, "100011")]
    public void Convert_ReturnsExpectedDigits(string input, int sourceBase, int targetBase, string expected)
    {
        RadixConversionResult result = RadixConverter.Convert(input, sourceBase, targetBase);

        Assert.AreEqual(expected, result.Output);
        Assert.IsTrue(result.Report.Steps.Count > 0);
    }

    [TestMethod]
    [DataRow("FF", 16, 10, "255")]
    [DataRow("ff", 16, 10, "255")]
    [DataRow(" 11111111 ", 2, 10, "255")]
    public void Convert_IgnoresLetterCaseAndOuterWhitespace(string input, int sourceBase, int targetBase, string expected)
    {
        Assert.AreEqual(expected, RadixConverter.Convert(input, sourceBase, targetBase).Output);
    }

    [TestMethod]
    public void Convert_HandlesNumbersBeyondUInt64()
    {
        RadixConversionResult result = RadixConverter.Convert("FFFFFFFFFFFFFFFF", 16, 10);

        Assert.AreEqual(BigInteger.Parse("18446744073709551615"), result.Value);
        Assert.AreEqual("18446744073709551615", result.Output);
    }

    [TestMethod]
    public void Convert_ZeroIsExplainedExplicitly()
    {
        RadixConversionResult result = RadixConverter.Convert("0", 16, 2);

        Assert.AreEqual(BigInteger.Zero, result.Value);
        Assert.AreEqual("0", result.Output);
        StringAssert.Contains(result.Report.Render(), "ноль");
    }

    [TestMethod]
    public void Convert_ExplainsAccumulationAndDivisionSteps()
    {
        RadixConversionResult result = RadixConverter.Convert("7FFF", 16, 10);

        Assert.AreEqual(BigInteger.Parse("32767"), result.Value);
        StringAssert.Contains(result.Report.Render(), "× 16");
        StringAssert.Contains(result.Report.Render(), "остаток");
    }

    [TestMethod]
    public void Convert_RejectsDigitOutsideSourceBase()
    {
        FormatException error = Assert.ThrowsExactly<FormatException>(() => RadixConverter.Convert("102", 2, 10));
        StringAssert.Contains(error.Message, "2");
    }

    [TestMethod]
    [DataRow(1, 10)]
    [DataRow(37, 10)]
    [DataRow(10, 1)]
    [DataRow(10, 37)]
    public void Convert_RejectsBasesOutsideTwoToThirtySix(int sourceBase, int targetBase)
    {
        Assert.ThrowsExactly<FormatException>(() => RadixConverter.Convert("1", sourceBase, targetBase));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("-")]
    public void Convert_RejectsEmptyMagnitude(string input)
    {
        Assert.ThrowsExactly<FormatException>(() => RadixConverter.Convert(input, 10, 2));
    }

    [TestMethod]
    public void Convert_RejectsUnknownSymbols()
    {
        Assert.ThrowsExactly<FormatException>(() => RadixConverter.Convert("1.5", 10, 2));
    }
}