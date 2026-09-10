using System.Globalization;
using System.Numerics;
using Besm6.Architecture;

namespace Besm6.BitVisualizer;

public enum IntegerInterpretation
{
    Unsigned,
    Signed,
}

public sealed record IntegerDecodeResult(
    BigInteger Value,
    IntegerInterpretation Interpretation,
    IReadOnlyList<BitField> Fields,
    CalculationReport Report);

/// <summary>Декодирует современные целые числа с объяснением веса разрядов.</summary>
public static class IntegerDecoder
{
    private static readonly int[] SupportedWidths = [8, 16, 32, 64];

    public static IntegerDecodeResult Decode(BitPattern bits, IntegerInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (!SupportedWidths.Contains(bits.Width))
            throw new ArgumentException("Целое число должно содержать 8, 16, 32 или 64 бита.", nameof(bits));
        if (!Enum.IsDefined(interpretation))
            throw new ArgumentOutOfRangeException(nameof(interpretation));

        bool negative = interpretation == IntegerInterpretation.Signed && bits.GetBit(bits.Width - 1);
        BigInteger value = negative
            ? bits.UnsignedValue - (BigInteger.One << bits.Width)
            : bits.UnsignedValue;

        var steps = new List<string>
        {
            interpretation == IntegerInterpretation.Signed
                ? "Выбран знаковый режим: число читается в дополнительном коде."
                : "Выбран беззнаковый режим: каждый установленный бит даёт положительный вклад.",
        };

        var contributions = new List<BigInteger>();
        for (int bit = bits.Width - 1; bit >= 0; bit--)
        {
            if (!bits.GetBit(bit))
                continue;

            BigInteger contribution = negative && bit == bits.Width - 1
                ? -(BigInteger.One << bit)
                : BigInteger.One << bit;
            contributions.Add(contribution);
            steps.Add($"Бит {bit} установлен: {(contribution.Sign < 0 ? "-" : string.Empty)}2^{bit} = {Format(contribution)}.");
        }

        if (contributions.Count == 0)
        {
            steps.Add("Установленных битов нет, поэтому сумма равна 0.");
        }
        else
        {
            string expression = string.Join(" + ", contributions.Select(Format)).Replace("+ -", "- ", StringComparison.Ordinal);
            steps.Add($"Сумма вкладов: {expression} = {Format(value)}.");
        }

        IReadOnlyList<BitField> fields = interpretation == IntegerInterpretation.Signed
            ?
            [
                new BitField("Знак", bits.Width - 1, bits.Width - 1, BitFieldKind.Sign),
                new BitField("Разряды значения", bits.Width - 2, 0, BitFieldKind.Value),
            ]
            : [new BitField("Беззнаковое значение", bits.Width - 1, 0, BitFieldKind.Value)];

        var report = new CalculationReport(
            interpretation == IntegerInterpretation.Signed
                ? $"Знаковое {bits.Width}-битное целое"
                : $"Беззнаковое {bits.Width}-битное целое",
            $"{bits.ToBinaryString()}₂",
            $"{Format(value)}₁₀",
            steps);

        return new IntegerDecodeResult(value, interpretation, fields, report);
    }

    private static string Format(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
}
