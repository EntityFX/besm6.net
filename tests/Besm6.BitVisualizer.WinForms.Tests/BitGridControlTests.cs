using System.Numerics;
using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Tests;

[TestClass]
public sealed class BitGridControlTests
{
    [STATestMethod]
    public void Configure_CreatesBitsFromMostToLeastSignificant()
    {
        using var grid = new BitGridControl();
        grid.Configure(24, [new BitField("Значение", 23, 0, BitFieldKind.Value)]);

        Assert.AreEqual(24, grid.BitCount);
        Assert.AreEqual(23, grid.GetDisplayedBitIndex(0));
        Assert.AreEqual(0, grid.GetDisplayedBitIndex(23));
    }

    [STATestMethod]
    public void SetValue_ToBitPattern_RoundTrips()
    {
        using var grid = new BitGridControl();
        grid.Configure(
            48,
            [
                new BitField("Порядок", 47, 41, BitFieldKind.Exponent),
                new BitField("Знак мантиссы", 40, 40, BitFieldKind.Sign),
                new BitField("Мантисса", 39, 0, BitFieldKind.Fraction),
            ]);
        BigInteger value = new(0x400000000000);
        grid.SetValue(value);

        Assert.AreEqual(new BitPattern(48, value), grid.ToBitPattern());
    }

    [STATestMethod]
    public void Clear_UnchecksAllBits()
    {
        using var grid = new BitGridControl();
        grid.Configure(16, [new BitField("Значение", 15, 0, BitFieldKind.Value)]);
        grid.SetValue(new BigInteger(0xFFFF));
        grid.Clear();

        Assert.AreEqual(BigInteger.Zero, grid.ToBitPattern().UnsignedValue);
    }

    [STATestMethod]
    public void CellsAreLabeledWithFieldAndBitNumber()
    {
        using var grid = new BitGridControl();
        grid.Configure(
            16,
            [
                new BitField("Знак", 15, 15, BitFieldKind.Sign),
                new BitField("Значение", 14, 0, BitFieldKind.Value),
            ]);

        StringAssert.Contains(grid.BitBoxes[15].AccessibleName, "Знак");
        StringAssert.Contains(grid.BitBoxes[15].AccessibleName, "15");
        StringAssert.Contains(grid.BitBoxes[0].AccessibleName, "Значение");
    }

    [STATestMethod]
    public void ApplyFields_RecolorsWithoutResettingState()
    {
        using var grid = new BitGridControl();
        grid.Configure(8, [new BitField("Значение", 7, 0, BitFieldKind.Value)]);
        grid.SetValue(new BigInteger(0b10000000));
        grid.ApplyFields([new BitField("Знак", 7, 7, BitFieldKind.Sign), new BitField("Значение", 6, 0, BitFieldKind.Value)]);

        Assert.AreEqual(new BitPattern(8, new BigInteger(0b10000000)), grid.ToBitPattern());
        StringAssert.Contains(grid.BitBoxes[7].AccessibleName, "Знак");
    }
}