namespace Besm6.Runtime.Devices;

/// <summary>An immutable 80 × 12 hole matrix in the puncher's 24-word memory layout.</summary>
public sealed class PunchCardImage
{
    public const int Rows = 12;
    public const int Columns = 80;
    public const int MemoryImageSize = 144;
    private readonly byte[] _bytes;

    internal PunchCardImage(byte[] bytes) => _bytes = bytes;

    /// <summary>Copies a 144-byte image: two big-endian words per row, 40 useful bits per word.</summary>
    public static PunchCardImage FromMemoryImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != MemoryImageSize)
            throw new ArgumentException("A card memory image must contain exactly 144 bytes.", nameof(bytes));
        return new PunchCardImage(bytes.ToArray());
    }

    public byte[] ToMemoryImage() => (byte[])_bytes.Clone();

    /// <summary>Tests a hole using zero-based physical row and column indices.</summary>
    public bool IsPunched(int row, int column)
    {
        if ((uint)row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
        return (_bytes[ByteOffset(row, column)] & (0x80 >> (column % 8))) != 0;
    }

    // Each six-byte word starts with an unused padding byte.
    internal static int ByteOffset(int row, int column) =>
        12 * row + 1 + column / 8 + (column >= 40 ? 1 : 0);
}
