using System.Text;
using Besm6.Runtime.Devices;

namespace Besm6.Cli;

public sealed class PunchCommand : ICommand
{
    public string Name => "punch";
    public string Description => "Encode text as IBM/UPDK or UPP cards; print numbered grid, ASCII or Braille";
    public string Usage => "besm6 punch [file|- | --text <text>] [--encoding ibm|upp] [--format grid|ascii|braille] [--output <file>]";

    public int Execute(string[] args)
    {
        try
        {
            string? input = null, text = null, output = null;
            var encoding = PunchCardEncoding.Upp;
            string format = "grid";
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                        Console.WriteLine($"Usage: {Usage}");
                        Console.WriteLine("One input line = one card; leading/trailing spaces preserved. Lowercase is uppercased.");
                        Console.WriteLine("Limits: IBM/UPDK 80 characters, UPP 120. Unsupported characters and overflow are errors.");
                        Console.WriteLine("Input/output files use UTF-8. Omit input or use '-' to read stdin. No deck control cards are added.");
                        return 0;
                    case "--text": text = Value(ref i); break;
                    case "--output": output = Value(ref i); break;
                    case "--encoding":
                        encoding = Value(ref i).ToLowerInvariant() switch
                        {
                            "ibm" or "updk" => PunchCardEncoding.Ibm,
                            "upp" => PunchCardEncoding.Upp,
                            _ => throw new ArgumentException("Encoding must be ibm, updk or upp.")
                        };
                        break;
                    case "--format":
                        format = Value(ref i).ToLowerInvariant();
                        if (format is not ("grid" or "ascii" or "braille"))
                            throw new ArgumentException("Format must be grid, ascii or braille.");
                        break;
                    default:
                        if (args[i].StartsWith('-') && args[i] != "-")
                            throw new ArgumentException($"Unknown option: {args[i]}");
                        if (input != null) throw new ArgumentException("Specify only one input file.");
                        input = args[i];
                        break;
                }
            }
            if (text != null && input != null)
                throw new ArgumentException("Use either --text or an input file, not both.");
            if (input is not null and not "-" && output != null &&
                string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Input and output paths must differ.");

            using TextReader reader = text != null ? new StringReader(text) :
                input is null or "-" ? new StringReader(Console.In.ReadToEnd()) :
                new StringReader(File.ReadAllText(input, new UTF8Encoding(false, true)));
            // Encode all lines before opening output: a bad card must not leave a partial deck.
            var cards = new List<PunchCardImage>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                try { cards.Add(PunchCardCodec.Encode(line, encoding)); }
                catch (ArgumentException ex) { throw new ArgumentException($"Line {cards.Count + 1}: {ex.Message}"); }
            }
            // Explicit empty text represents one empty card; an empty file represents an empty deck.
            if (text == "") cards.Add(PunchCardCodec.Encode("", encoding));

            using var fileWriter = output == null ? null : new StreamWriter(output, false, new UTF8Encoding(false));
            TextWriter writer = fileWriter ?? Console.Out;
            for (int i = 0; i < cards.Count; i++)
            {
                if (format == "braille") writer.Write(PunchCardRenderer.RenderBraille(cards[i]));
                else
                {
                    writer.WriteLine($"Card {i + 1} ({encoding})");
                    writer.Write(PunchCardRenderer.RenderGrid(cards[i], encoding, format == "ascii"));
                    writer.WriteLine();
                }
            }
            return 0;

            string Value(ref int index)
            {
                if (++index >= args.Length) throw new ArgumentException($"Missing value for {args[index - 1]}.");
                return args[index];
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
