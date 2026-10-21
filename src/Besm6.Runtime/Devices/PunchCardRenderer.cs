using System.Globalization;
using System.Text;

namespace Besm6.Runtime.Devices;

/// <summary>Text-only views of a card. All output uses LF line endings.</summary>
public static class PunchCardRenderer
{
    private static readonly int[] IbmRows = { 12, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    private static readonly byte[] BrailleMap = { 0x01, 0x08, 0x02, 0x10, 0x04, 0x20, 0x40, 0x80 };

    public static string RenderGrid(PunchCardImage card, PunchCardEncoding encoding, bool ascii = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        var result = new StringBuilder();
        result.Append("Tens     ");
        Columns(c => (char)('0' + c / 10));
        result.Append("Units    ");
        Columns(c => (char)('0' + c % 10));
        for (int row = 0; row < 12; row++)
        {
            int label = encoding == PunchCardEncoding.Ibm ? IbmRows[row] : row + 1;
            result.Append("Row ").Append(label.ToString("00", CultureInfo.InvariantCulture)).Append("   ");
            Columns(c => card.IsPunched(row, c - 1) ? (ascii ? 'O' : '▯') : (ascii ? '.' : '▮'));
        }
        result.Append(ascii ? "O = hole; . = unpunched\n" : "▯ = hole; ▮ = unpunched\n");
        return result.ToString();

        void Columns(Func<int, char> character)
        {
            for (int c = 1; c <= 80; c++)
            {
                if (c > 1 && (c - 1) % 8 == 0) result.Append(' ');
                result.Append(character(c));
            }
            result.Append('\n');
        }
    }

    /// <summary>Three rows of 40 eight-dot Braille cells, compatible with punch.out.</summary>
    public static string RenderBraille(PunchCardImage card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var result = new StringBuilder();
        for (int block = 0; block < 3; block++)
        {
            for (int cell = 0; cell < 40; cell++)
            {
                int bits = 0;
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 2; c++)
                        if (card.IsPunched(block * 4 + r, cell * 2 + c))
                            bits |= BrailleMap[r * 2 + c];
                result.Append((char)(0x2800 + bits));
            }
            result.Append('\n');
        }
        return result.Append('\n').ToString();
    }
}
