using System.Globalization;
using System.Text.RegularExpressions;

namespace Besm6.Android.Services;

public sealed record PlotterSegment(float X0, float Y0, float X1, float Y1, int Color);

/// <summary>Port of the web plotter command decoders for Watanabe, Calcomp and Tektronix.</summary>
public static class PlotterParser
{
    private static readonly Regex Pen = new(@"^J\s*([1-6])\s*$", RegexOptions.Compiled);
    private static readonly Regex Command = new(@"^([A-Z])\s*(-?\d+)\s*,\s*(-?\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<PlotterSegment> Parse(string? data, string? kind)
    {
        var lines = new List<PlotterSegment>();
        if (string.IsNullOrEmpty(data)) return lines;

        float x = 0, y = 0;
        int color = 0;
        bool positioned = string.Equals(kind, "calcomp", StringComparison.OrdinalIgnoreCase);
        bool penUp = false;

        void Point(int nextX, int nextY, bool draw)
        {
            if (draw && positioned && (x != nextX || y != nextY))
            {
                float dx = nextX - x, dy = nextY - y;
                if (lines.Count > 0)
                {
                    var last = lines[^1];
                    float ldx = last.X1 - last.X0, ldy = last.Y1 - last.Y0;
                    if (last.Color == color && last.X1 == x && last.Y1 == y
                        && ldx * dy == ldy * dx && ldx * dx + ldy * dy > 0)
                    {
                        lines[^1] = last with { X1 = nextX, Y1 = nextY };
                        x = nextX;
                        y = nextY;
                        positioned = true;
                        return;
                    }
                }
                lines.Add(new PlotterSegment(x, y, nextX, nextY, color));
            }
            x = nextX;
            y = nextY;
            positioned = true;
        }

        switch ((kind ?? string.Empty).ToLowerInvariant())
        {
            case "watanabe":
                foreach (string line in data.Split('\n'))
                {
                    string trimmed = line.TrimEnd('\r');
                    Match pen = Pen.Match(trimmed);
                    if (pen.Success)
                    {
                        color = int.Parse(pen.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                        continue;
                    }
                    Match command = Command.Match(trimmed);
                    if (command.Success)
                    {
                        int cx = int.Parse(command.Groups[2].Value, CultureInfo.InvariantCulture);
                        int cy = int.Parse(command.Groups[3].Value, CultureInfo.InvariantCulture);
                        Point(cx, cy, command.Groups[1].Value == "D");
                    }
                }
                break;

            case "tektronix":
                for (int i = 0; i < data.Length;)
                {
                    bool move = false;
                    if (data[i] == 29)
                    {
                        move = true;
                        i++;
                    }
                    if (i + 4 > data.Length) break;
                    int nextY = ((data[i] & 31) << 5) | (data[i + 1] & 31);
                    int nextX = ((data[i + 2] & 31) << 5) | (data[i + 3] & 31);
                    Point(nextX, nextY, !move);
                    i += 4;
                }
                break;

            case "calcomp":
                foreach (char character in data)
                {
                    int b = character;
                    if ((b & 4) != 0) penUp = true;
                    if ((b & 8) != 0) penUp = false;
                    int dx = (b & 2) != 0 ? 1 : (b & 1) != 0 ? -1 : 0;
                    int dy = (b & 32) != 0 ? -1 : (b & 16) != 0 ? 1 : 0;
                    if (dx != 0 || dy != 0) Point((int)x + dx, (int)y + dy, !penUp);
                }
                break;
        }

        return lines;
    }
}
