using System.Numerics;
using System.Text;

namespace Besm6.BitVisualizer;

/// <summary>Неизменяемая битовая последовательность фиксированной ширины.</summary>
public sealed class BitPattern : IEquatable<BitPattern>
{
    public BitPattern(int width, BigInteger unsignedValue)
    {
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Ширина должна быть положительной.");
        if (unsignedValue < BigInteger.Zero || unsignedValue >= (BigInteger.One << width))
            throw new ArgumentOutOfRangeException(nameof(unsignedValue), "Значение не помещается в указанную ширину.");

        Width = width;
        UnsignedValue = unsignedValue;
    }

    public int Width { get; }

    public BigInteger UnsignedValue { get; }

    public static BitPattern FromMsbFirst(IEnumerable<bool> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        bool[] values = bits.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("Последовательность должна содержать хотя бы один бит.", nameof(bits));

        BigInteger value = BigInteger.Zero;
        foreach (bool bit in values)
        {
            value <<= 1;
            if (bit)
                value += BigInteger.One;
        }

        return new BitPattern(values.Length, value);
    }

    public bool GetBit(int bitIndex)
    {
        if ((uint)bitIndex >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(bitIndex), "Номер бита выходит за границы последовательности.");

        return ((UnsignedValue >> bitIndex) & BigInteger.One) != BigInteger.Zero;
    }

    public string ToBinaryString() => Format(2, Width);

    public string ToOctalString() => Format(8, (Width + 2) / 3);

    public string ToHexString() => Format(16, (Width + 3) / 4);

    public bool Equals(BitPattern? other) =>
        other is not null && Width == other.Width && UnsignedValue == other.UnsignedValue;

    public override bool Equals(object? obj) => Equals(obj as BitPattern);

    public override int GetHashCode() => HashCode.Combine(Width, UnsignedValue);

    public override string ToString() => ToBinaryString();

    private string Format(int radix, int minimumDigits)
    {
        const string alphabet = "0123456789ABCDEF";
        if (UnsignedValue.IsZero)
            return new string('0', minimumDigits);

        var result = new StringBuilder();
        BigInteger remaining = UnsignedValue;
        while (remaining > BigInteger.Zero)
        {
            remaining = BigInteger.DivRem(remaining, radix, out BigInteger remainder);
            result.Insert(0, alphabet[(int)remainder]);
        }

        return result.ToString().PadLeft(minimumDigits, '0');
    }
}
