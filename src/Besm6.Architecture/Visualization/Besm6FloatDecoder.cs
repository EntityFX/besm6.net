using System.Globalization;

namespace Besm6.Architecture.Visualization;

public sealed record Besm6FloatDecodeResult(
    Word48 Word,
    double Value,
    int RawExponent,
    int Exponent,
    long Mantissa,
    IReadOnlyList<BitField> Fields,
    CalculationReport Report);

/// <summary>Объясняет нативный 48-битный формат числа БЭСМ-6.</summary>
public static class Besm6FloatDecoder
{
    public static Besm6FloatDecodeResult Decode(BitPattern bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Width != 48)
            throw new ArgumentException("Нативное число БЭСМ-6 должно содержать ровно 48 бит.", nameof(bits));

        var word = new Word48((ulong)bits.UnsignedValue);
        int rawExponent = (int)(word.Value >> 41);
        int exponent = rawExponent - 64;
        long mantissa = (long)(word.Value & ((1UL << 41) - 1));
        if ((mantissa & (1L << 40)) != 0)
            mantissa |= ~((1L << 41) - 1);

        double value = word.ToDouble();
        var fields = new BitField[]
        {
            new("Порядок", 47, 41, BitFieldKind.Exponent),
            new("Знак мантиссы", 40, 40, BitFieldKind.Sign),
            new("Мантисса", 39, 0, BitFieldKind.Fraction),
        };
        var steps = new List<string>
        {
            $"Семь старших битов 47–41 дают смещённый порядок: {rawExponent}.",
            $"Истинный порядок: {rawExponent} - 64 = {exponent}.",
            $"Биты 40–0 читаются как знаковое 41-битное число в дополнительном коде: мантисса = {mantissa}.",
            $"Подстановка: {mantissa} / 2^40 × 2^({rawExponent} - 64).",
        };
        if (word.Value == 0)
            steps.Add("Все биты равны нулю, поэтому мантисса и итоговое значение равны нулю.");
        else
            steps.Add($"После масштабирования получаем {value.ToString("R", CultureInfo.InvariantCulture)}.");

        var report = new CalculationReport(
            "БЭСМ-6 — нативное 48-битное число",
            $"{bits.ToBinaryString()}₂ ({word.ToOctal()}₈)",
            value.ToString("R", CultureInfo.InvariantCulture),
            steps);

        return new Besm6FloatDecodeResult(word, value, rawExponent, exponent, mantissa, fields, report);
    }
}
