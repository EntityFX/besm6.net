using System.Numerics;
using Besm6.Runtime.Encoding;

namespace Besm6.Runtime.Devices;

public enum PunchCardEncoding { Ibm, Upp }

/// <summary>Text to hole matrix; no job/deck control cards are added.</summary>
public static class PunchCardCodec
{
    public static PunchCardImage Encode(string text, PunchCardEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        int capacity = encoding switch
        {
            PunchCardEncoding.Ibm => 80,
            PunchCardEncoding.Upp => 120,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        if (text.Length > capacity)
            throw new ArgumentException($"{encoding} supports at most {capacity} characters per card (got {text.Length}).", nameof(text));

        var bytes = new byte[PunchCardImage.MemoryImageSize];
        for (int i = 0; i < text.Length; i++)
        {
            char ch = char.ToUpperInvariant(text[i]);
            // These Cyrillic and Latin glyphs share the historical character codes.
            ch = ch switch
            {
                'А' => 'A', 'В' => 'B', 'С' => 'C', 'Е' => 'E', 'Н' => 'H',
                'К' => 'K', 'М' => 'M', 'О' => 'O', 'Р' => 'P', 'Т' => 'T',
                'Х' => 'X', 'У' => 'Y', _ => ch
            };
            if (encoding == PunchCardEncoding.Ibm)
            {
                if (!IbmPunchCardTable.Codes.TryGetValue(ch, out ushort holes))
                    throw Unsupported(text[i], i);
                for (int row = 0; row < 12; row++)
                    if ((holes & (1 << row)) != 0) Punch(row, i);
            }
            else
            {
                ch = ch switch
                {
                    '≤' => '⩽', '≥' => '⩾',
                    '⑩' => '⏨', '␣' => ' ', '◊' => '◇',
                    _ => ch
                };
                int code = -1;
                for (int c = 0; c < 0x60; c++)
                    if (EncodingTables.GostToUnicodeLat[c] == ch) { code = c; break; }
                if (code < 0) throw Unsupported(text[i], i);
                if ((BitOperations.PopCount((uint)code) & 1) == 0) code |= 0x80;
                for (int bit = 0; bit < 8; bit++)
                    if ((code & (0x80 >> bit)) != 0) Punch(i / 10, (i % 10) * 8 + bit);
            }
        }
        return new PunchCardImage(bytes);

        void Punch(int row, int column) =>
            bytes[PunchCardImage.ByteOffset(row, column)] |= (byte)(0x80 >> (column % 8));
    }

    private static ArgumentException Unsupported(char ch, int index) =>
        new($"Unsupported character U+{(int)ch:X4} at position {index + 1}.", "text");
}
