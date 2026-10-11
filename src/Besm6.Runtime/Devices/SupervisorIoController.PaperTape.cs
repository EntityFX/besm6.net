using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private readonly PaperReader[] _paperReaders = [new(), new()];

    public void AttachPaperTape(int reader, ReadOnlySpan<byte> holes)
    {
        ValidateDrum(reader);
        var state = _paperReaders[reader];
        if (state.Pending is IoEventToken token) CancelIo(token);
        state.Pending = null;
        state.Data = holes.ToArray();
        state.Position = state.Mode = 0;
        state.Register = 0;
        _ready |= 1U << (15 - reader);
    }

    /// <summary>Host FS1500 prepared-text encoder: GOST+odd parity, GS virtual cards, 120 columns and ENDA3.</summary>
    public void AttachPaperTapeText(int reader, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var holes = new List<byte>();
        int column = -1;
        foreach (char character in text)
        {
            if (character == '\r') continue;
            if (character == '\n')
            {
                if (column >= 0) { while (column++ < 120) holes.Add(0); column = 0; }
                continue;
            }
            if (character == 29)
            {
                if (column < 0) column = 0;
                else
                {
                    for (int i = 0; i < 120; i++) holes.Add(i % 5 == 0 ? (byte)128 : (byte)0);
                    column = -1;
                }
                continue;
            }
            byte code = ToGost(character);
            holes.Add((byte)(code | ((BitOperations.PopCount((uint)code) & 1) == 0 ? 128 : 0)));
            if (column >= 0 && ++column == 120) column = 0;
        }
        AttachPaperTape(reader, holes.ToArray());
    }

    private static byte ToGost(char character)
    {
        char upper = char.ToUpperInvariant(character);
        if (upper is >= 'А' and <= 'Я')
        {
            int offset = upper - 'А';
            return (byte)(offset < 26 ? 32 + offset : offset == 26 ? 93 : 31 + offset);
        }
        if (upper == 'Ё') return 37;
        if (character == 30) return 124;
        if (character == '\t') return 15;
        byte? punctuation = character switch
        {
            ' ' => 15, '!' => 91, '"' => 92, '#' => 28, '$' => 87, '%' => 86,
            '&' => 81, '\'' => 27, '(' => 18, ')' => 19, '*' => 25, '+' => 10,
            ',' => 13, '-' => 11, '.' => 14, '/' => 12, ':' => 31, ';' => 22,
            '<' => 29, '=' => 21, '>' => 30, '?' => 94, '@' => 17, '[' => 23,
            '\\' => 15, ']' => 24, '^' => 77, '_' => 90, '`' => 26, '{' => 15,
            '|' => 88, '}' => 15, '~' => 83, _ => null
        };
        if (punctuation is byte code) return code;
        var table = Encoding.EncodingTables.GostToUnicodeLat;
        for (byte i = 0; i < 128; i++) if (table[i] == upper) return i;
        throw new ArgumentException($"Character U+{(int)character:X4} is not supported in prepared GOST text.");
    }

    private void ControlPaperReader(int reader, uint command)
    {
        var state = _paperReaders[reader];
        if ((_ready & (1U << (15 - reader))) == 0) return; // Disconnected motor cannot advance tape.
        switch (command)
        {
            case 0:
                if (state.Pending is IoEventToken token) CancelIo(token);
                state.Pending = null; state.Mode = 0; break;
            case 4:
                if (state.Pending is IoEventToken starting) CancelIo(starting);
                state.Pending = null; state.Mode = 1; break;
            case 5:
                // Pinned fs_control/sim_activate: no feed without a running motor,
                // and a second activation of an active unit retains its deadline.
                if (state.Mode == 0 || state.Pending is not null) return;
                if (state.Mode == 3) { _ready &= ~(1U << (15 - reader)); state.Mode = 0; break; }
                state.Pending = ScheduleIo(IoEventKind.PaperReader, CompletionDelayTicks, () =>
                {
                    state.Pending = null;
                    if (state.Mode == 0) state.Register = 0;
                    else if (state.Mode == 1) { state.Register = 0; state.Mode = 2; }
                    else if (state.Position < state.Data.Length) state.Register = state.Data[state.Position++];
                    else { state.Register = 0; state.Mode = 3; }
                    RaiseInterrupts(1UL << (41 - reader));
                });
                break;
            default: throw Unsupported("paper reader control", command);
        }
    }

    private sealed class PaperReader
    {
        public byte[] Data = [];
        public int Position;
        public int Mode;
        public byte Register;
        public IoEventToken? Pending;
    }
}
