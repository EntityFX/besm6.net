using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Besm6.BitVisualizer;

public enum Ieee754Class
{
    Normal,
    Subnormal,
    PositiveZero,
    NegativeZero,
    PositiveInfinity,
    NegativeInfinity,
    NaN,
}

public sealed record Ieee754DecodeResult(
    double Value,
    Ieee754Class Classification,
    int RawExponent,
    BigInteger Fraction,
    IReadOnlyList<BitField> Fields,
    CalculationReport Report);

/// <summary>Разбирает поля IEEE-754 и формирует учебное объяснение.</summary>
public static class Ieee754Decoder
{
    public static Ieee754DecodeResult Decode(BitPattern bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        FormatInfo format = bits.Width switch
        {
            16 => new FormatInfo("binary16 (Half)", 5, 10, 15),
            32 => new FormatInfo("binary32 (float)", 8, 23, 127),
            64 => new FormatInfo("binary64 (double)", 11, 52, 1023),
            _ => throw new ArgumentException("IEEE-754 значение должно содержать 16, 32 или 64 бита.", nameof(bits)),
        };

        bool negative = bits.GetBit(bits.Width - 1);
        BigInteger fractionMask = (BigInteger.One << format.FractionBits) - 1;
        BigInteger fraction = bits.UnsignedValue & fractionMask;
        int rawExponent = (int)((bits.UnsignedValue >> format.FractionBits) & ((BigInteger.One << format.ExponentBits) - 1));
        int maximumExponent = (1 << format.ExponentBits) - 1;
        Ieee754Class classification = Classify(negative, rawExponent, maximumExponent, fraction);
        double value = ReadRuntimeValue(bits);

        var fields = new BitField[]
        {
            new("Знак", bits.Width - 1, bits.Width - 1, BitFieldKind.Sign),
            new("Порядок", bits.Width - 2, format.FractionBits, BitFieldKind.Exponent),
            new("Дробная часть", format.FractionBits - 1, 0, BitFieldKind.Fraction),
        };

        var steps = new List<string>
        {
            $"Формат {format.Name}: 1 бит знака, {format.ExponentBits} бит порядка и {format.FractionBits} бит дробной части.",
            $"Знак s = {(negative ? 1 : 0)}, поэтому множитель (-1)^{(negative ? 1 : 0)} = {(negative ? -1 : 1)}.",
            $"Сырой порядок = {rawExponent}, дробная часть = {fraction}.",
        };

        AppendExplanation(steps, classification, rawExponent, fraction, format, negative);

        string resultText = FormatValue(value, classification);
        var report = new CalculationReport(
            $"IEEE-754 — {format.Name}",
            $"{bits.ToBinaryString()}₂ (0x{bits.ToHexString()})",
            resultText,
            steps);

        return new Ieee754DecodeResult(value, classification, rawExponent, fraction, fields, report);
    }

    private static Ieee754Class Classify(bool negative, int exponent, int maximumExponent, BigInteger fraction)
    {
        if (exponent == 0 && fraction.IsZero)
            return negative ? Ieee754Class.NegativeZero : Ieee754Class.PositiveZero;
        if (exponent == 0)
            return Ieee754Class.Subnormal;
        if (exponent == maximumExponent && fraction.IsZero)
            return negative ? Ieee754Class.NegativeInfinity : Ieee754Class.PositiveInfinity;
        if (exponent == maximumExponent)
            return Ieee754Class.NaN;
        return Ieee754Class.Normal;
    }

    private static double ReadRuntimeValue(BitPattern bits)
    {
        ulong raw = (ulong)bits.UnsignedValue;
        return bits.Width switch
        {
            16 => (double)BitConverter.UInt16BitsToHalf((ushort)raw),
            32 => BitConverter.Int32BitsToSingle(unchecked((int)(uint)raw)),
            64 => BitConverter.Int64BitsToDouble(unchecked((long)raw)),
            _ => throw new UnreachableException(),
        };
    }

    private static void AppendExplanation(
        ICollection<string> steps,
        Ieee754Class classification,
        int rawExponent,
        BigInteger fraction,
        FormatInfo format,
        bool negative)
    {
        switch (classification)
        {
            case Ieee754Class.PositiveZero:
            case Ieee754Class.NegativeZero:
                steps.Add("Порядок и дробная часть равны нулю: это знаковый ноль.");
                break;
            case Ieee754Class.PositiveInfinity:
            case Ieee754Class.NegativeInfinity:
                steps.Add("Все биты порядка равны 1, а дробная часть равна 0: это бесконечность.");
                break;
            case Ieee754Class.NaN:
                steps.Add("Все биты порядка равны 1, а дробная часть ненулевая: это NaN.");
                break;
            case Ieee754Class.Subnormal:
            {
                int exponent = 1 - format.Bias;
                double significand = (double)fraction / Math.Pow(2, format.FractionBits);
                steps.Add($"Порядок равен 0, но дробь ненулевая: это субнормальное число, скрытой единицы нет.");
                steps.Add($"Истинный порядок = 1 - {format.Bias} = {exponent}; мантисса = {fraction} / 2^{format.FractionBits} = {significand.ToString("R", CultureInfo.InvariantCulture)}.");
                steps.Add($"Формула: (-1)^{(negative ? 1 : 0)} × ({fraction} / 2^{format.FractionBits}) × 2^{exponent}.");
                break;
            }
            case Ieee754Class.Normal:
            {
                int exponent = rawExponent - format.Bias;
                double fractionValue = (double)fraction / Math.Pow(2, format.FractionBits);
                double significand = 1 + fractionValue;
                steps.Add($"Порядок не крайний: число нормальное, перед дробью используется скрытая единица.");
                steps.Add($"Истинный порядок = {rawExponent} - {format.Bias} = {exponent}; мантисса = 1 + {fraction} / 2^{format.FractionBits} = {significand.ToString("R", CultureInfo.InvariantCulture)}.");
                steps.Add($"Формула: (-1)^{(negative ? 1 : 0)} × (1 + {fraction} / 2^{format.FractionBits}) × 2^({rawExponent} - {format.Bias}).");
                break;
            }
            default:
                throw new UnreachableException();
        }
    }

    private static string FormatValue(double value, Ieee754Class classification) => classification switch
    {
        Ieee754Class.PositiveInfinity => "+∞",
        Ieee754Class.NegativeInfinity => "-∞",
        Ieee754Class.NaN => "NaN",
        Ieee754Class.NegativeZero => "-0",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };

    private readonly record struct FormatInfo(string Name, int ExponentBits, int FractionBits, int Bias);
}
