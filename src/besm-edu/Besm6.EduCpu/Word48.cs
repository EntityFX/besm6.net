namespace Besm6.EduCpu;

/// <summary>
/// Машинное слово БЭСМ-6: 48 информационных бит.
/// Аппаратный аналог — разрядное слово оперативной памяти.
/// </summary>
public readonly struct Word48 : IEquatable<Word48>
{
    public const ulong Mask = 0xFFFF_FFFF_FFFF; // младшие 48 бит

    public ulong Raw { get; }

    /// <summary>Конструктор всегда оставляет только младшие 48 бит.</summary>
    public Word48(ulong value)
    {
        Raw = value & Mask;
    }

    /// <summary>Восьмеричная запись ровно 16 цифр (48 бит).</summary>
    public string ToOctal() => Oct.Pad(Raw, 16);

    /// <summary>Создание слова из восьмеричной строки.</summary>
    public static Word48 FromOctal(string octal)
    {
        if (string.IsNullOrWhiteSpace(octal))
        {
            throw new ArgumentException("Пустая восьмеричная строка.", nameof(octal));
        }

        ulong value = 0;
        foreach (char c in octal.Trim())
        {
            if (c < '0' || c > '7')
            {
                throw new FormatException($"Не восьмеричная цифра: '{c}'.");
            }

            value = (value << 3) | ((ulong)c - (ulong)'0');
        }
        return new Word48(value);
    }

    /// <summary>Упаковка двух 24-разрядных команд в одно 48-разрядное слово.</summary>
    public static Word48 Pack(uint left24, uint right24)
    {
        if (left24 > 0xFF_FFFFu)
        {
            throw new ArgumentOutOfRangeException(nameof(left24), "Левая половина шире 24 бит.");
        }

        if (right24 > 0xFF_FFFFu)
        {
            throw new ArgumentOutOfRangeException(nameof(right24), "Правая половина шире 24 бит.");
        }

        return new Word48(((ulong)left24 << 24) | right24);
    }

    /// <summary>Левая (старшая) 24-разрядная половина.</summary>
    public uint LeftHalf => (uint)(Raw >> 24);

    /// <summary>Правая (младшая) 24-разрядная половина.</summary>
    public uint RightHalf => (uint)(Raw & 0xFF_FFFFu);

    // Побитовые операции (И/ИЛИ/НТЖ) и арифметика (СЛЦ/ВЫЧ/УМН) — всегда возвращают новое слово.
    public Word48 And(Word48 other) => new(Raw & other.Raw);
    public Word48 Or(Word48 other) => new(Raw | other.Raw);
    public Word48 Xor(Word48 other) => new(Raw ^ other.Raw);

    /// <summary>Побитовое отрицание в пределах 48 разрядов.</summary>
    public Word48 Not() => new((~Raw) & Mask);

    /// <summary>Циклическое сложение: перенос из 49-го разряда прибавляется к младшему.</summary>
    public Word48 CyclicAdd(Word48 other)
    {
        ulong sum = Raw + other.Raw;
        return new Word48(((sum & Mask) + ((sum >> 48) & 1)) & Mask);
    }

    /// <summary>Вычитание 48-разрядных слов по модулю 2^48 (перенос отбрасывается).</summary>
    public Word48 Subtract(Word48 other) => new(Raw - other.Raw);

    /// <summary>Учебное умножение: младшие 24 бита обоих операндов, младшие 48 бит произведения.</summary>
    public Word48 Multiply(Word48 other)
    {
        ulong a = Raw & 0xFF_FFFF;
        ulong b = other.Raw & 0xFF_FFFF;
        return new(a * b);
    }

    public static Word48 operator & (Word48 a, Word48 b) => new(a.Raw & b.Raw);
    public static Word48 operator | (Word48 a, Word48 b) => new(a.Raw | b.Raw);
    public static Word48 operator ^ (Word48 a, Word48 b) => new(a.Raw ^ b.Raw);

    public bool Equals(Word48 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Word48 other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public static bool operator ==(Word48 a, Word48 b) => a.Raw == b.Raw;
    public static bool operator !=(Word48 a, Word48 b) => a.Raw != b.Raw;
    public override string ToString() => ToOctal();
}