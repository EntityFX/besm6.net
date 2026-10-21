using System.Globalization;
using System.Text;

namespace Besm6.Runtime.Devices;

/// <summary>Text-only views of a card. All output uses LF line endings.</summary>
public static class PunchCardRenderer
{
    private static readonly int[] IbmRows = { 12, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    private static readonly byte[] BrailleMap = { 0x01, 0x08, 0x02, 0x10, 0x04, 0x20, 0x40, 0x80 };

//                   ____________
//                  /
//           /  12 / O
//   Zone rows  11|   O
//           \/  0|    O
//           /   1|     O
//          /    2|      O
//         /     3|       O
//   Numerc      4|        O
//   rows        5|         O
//         \     6|          O
//          \    7|           O
//           \   8|            O
//            \  9|             O
//                |______________

    public static string RenderGrid(PunchCardImage card, PunchCardEncoding encoding, bool ascii = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        var result = new StringBuilder();

        result.Append("    ");
        Columns(c => (char)('0' + c / 10), ender: ' ');
        result.Append("    ");
        Columns(c => (char)('0' + c % 10), ender: ' '); //▯' : '▮

        result.Append("    ");
        result.Append(new string(ascii ? '-' : '─', 81));
        result.Append(ascii ? '+' : '┐');
        result.AppendLine();

        result.Append("   /");
        result.Append(new string(' ', 81));
        result.Append(ascii ? "|" : "│");
        result.AppendLine();
        for (int row = 0; row < 12; row++)
        {
            int label = encoding == PunchCardEncoding.Ibm ? IbmRows[row] : row + 1;
            result.Append(label.ToString("00", CultureInfo.InvariantCulture)).Append(row == 0 ? "/ " : (ascii ? "| " : "│ "));
            Columns(c => {
                return card.IsPunched(row, c - 1) ? (ascii ? 'O' : '□') : '.';
            }, false, ascii ? '|' : '│');
        }
        result.Append("  ");
        result.Append(ascii ? '+' : '└');
        result.Append(new string(ascii ? '-' : '─', 82));
        result.Append(ascii ? '+' : '┘');
        result.AppendLine();
        result.Append(ascii ? "O = hole; . = unpunched\n" : "□ = hole\n");
        return result.ToString();

        void Columns(Func<int, char> character, bool spacer = false, char ender = '|')
        {
            for (int c = 1; c <= 80; c++)
            {
                if (spacer && (c > 1 && (c - 1) % 8 == 0)) result.Append(' ');
                result.Append(character(c));
            }
            result.Append(' ');
            result.Append(ender);
            result.AppendLine();
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
