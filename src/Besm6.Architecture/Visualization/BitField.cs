namespace Besm6.Architecture.Visualization;

/// <summary>Семантическая категория поля, не зависящая от UI-палитры.</summary>
public enum BitFieldKind
{
    Value,
    Sign,
    Exponent,
    Fraction,
    Register,
    Format,
    Opcode,
    Address,
}

/// <summary>Именованный непрерывный диапазон битов с включёнными границами.</summary>
public sealed record BitField
{
    public BitField(string name, int mostSignificantBit, int leastSignificantBit, BitFieldKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Имя поля не может быть пустым.", nameof(name));
        if (leastSignificantBit < 0)
            throw new ArgumentOutOfRangeException(nameof(leastSignificantBit));
        if (mostSignificantBit < leastSignificantBit)
            throw new ArgumentOutOfRangeException(nameof(mostSignificantBit));

        Name = name;
        MostSignificantBit = mostSignificantBit;
        LeastSignificantBit = leastSignificantBit;
        Kind = kind;
    }

    public string Name { get; }

    public int MostSignificantBit { get; }

    public int LeastSignificantBit { get; }

    public BitFieldKind Kind { get; }

    public bool Contains(int bitIndex) =>
        bitIndex >= LeastSignificantBit && bitIndex <= MostSignificantBit;
}
