using Besm6.Runtime.Devices;

namespace Besm6.AspNetCore.Web.Hosting;

/// <summary>Derive browser codebooks from the same codec as the CLI and puncher.</summary>
internal static class PunchCardCodebook
{
    internal static readonly object Data = new { ibm = Build(PunchCardEncoding.Ibm), upp = Build(PunchCardEncoding.Upp) };

    private static Dictionary<string, int> Build(PunchCardEncoding encoding)
    {
        const string candidates = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ⑩◊¬␣⏨↑×÷≠≤≥⩽⩾∨∧⊃≡%|—_!\"°′‘’'+-/,.:;[]()*<>=‾";
        var result = new Dictionary<string, int>();
        foreach (char character in candidates.Distinct())
        {
            PunchCardImage card;
            try { card = PunchCardCodec.Encode(character.ToString(), encoding); }
            catch (ArgumentException) { continue; }
            int mask = 0;
            int bits = encoding == PunchCardEncoding.Ibm ? 12 : 8;
            for (int bit = 0; bit < bits; bit++)
                if (encoding == PunchCardEncoding.Ibm ? card.IsPunched(bit, 0) : card.IsPunched(0, bit))
                    mask |= 1 << (encoding == PunchCardEncoding.Ibm ? bit : 7 - bit);
            result[character.ToString()] = mask;
        }
        return result;
    }
}
