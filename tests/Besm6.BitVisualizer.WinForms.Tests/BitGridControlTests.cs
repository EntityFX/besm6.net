using System.Linq;
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

    [STATestMethod]
    public void BitGridControl_ReportsFullContentSize()
    {
        using var grid = new BitGridControl();
        grid.Configure(
            48,
            [
                new BitField("Знак", 47, 47, BitFieldKind.Sign),
                new BitField("Порядок", 46, 45, BitFieldKind.Exponent),
                new BitField("Мантисса", 44, 0, BitFieldKind.Fraction),
            ]);
        grid.PerformLayout();

        // Три ряда по 50 px + легенда + заголовок: контейнер обязан
        // вместить весь контент, а не обжиматься под внутренние скроллы.
        Assert.IsFalse(grid.AutoScroll, "сетка не должна сжиматься с внутренним скроллом");
        Assert.IsTrue(
            grid.PreferredSize.Height >= 3 * 50 + 60,
            $"высота сетки должна покрывать весь контент (факт: {grid.PreferredSize.Height})");
    }

    [STATestMethod]
    public void RowsArePerfectlyAlignedAndGroupedByEight()
    {
        using var grid = new BitGridControl();
        grid.Configure(
            48,
            [
                new BitField("Знак", 47, 47, BitFieldKind.Sign),
                new BitField("Порядок", 46, 45, BitFieldKind.Exponent),
                new BitField("Мантисса", 44, 0, BitFieldKind.Fraction),
            ]);
        grid.PerformLayout();

        // Все ячейки ряда стоят на одной линии, X растёт строго,
        // чекбоксы — в одной и той же точке внутри каждой ячейки.
        for (int row = 0; row < 3; row++)
        {
            int[] bits = Enumerable.Range(0, 16).Select(offset => 47 - (row * 16 + offset)).ToArray();

            Assert.AreEqual(
                1,
                bits.Select(bit => grid.BitCells[bit].Top).Distinct().Count(),
                $"все ячейки ряда {row} должны иметь одинаковый верхний край");

            int[] xs = bits.Select(bit => grid.BitCells[bit].Left).ToArray();
            for (int i = 1; i < xs.Length; i++)
            {
                Assert.IsTrue(xs[i] > xs[i - 1], $"клетки ряда {row} должны строго расти по X");
            }

            Assert.AreEqual(
                1,
                bits.Select(bit => grid.BitBoxes[bit].Location).Distinct().Count(),
                "чекбокс должен стоять в одной точке в каждой ячейке");
        }

        // Между блоками по восемь бит — визуальный зазор (34 + 10 px плюс рамки ячеек).
        int firstBitOfSecondGroup = 47 - 8;
        int lastBitOfFirstGroup = 47 - 7;
        int step = grid.BitCells[firstBitOfSecondGroup].Left - grid.BitCells[lastBitOfFirstGroup].Left;
        Assert.IsTrue(step is > 44 and < 52, $"шаг через границу группы должен включать зазор (факт: {step})");
    }
}