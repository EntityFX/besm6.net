using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private readonly Printer[] _printers = [new(), new()];

    public void ConfigurePrinter(int printer, bool connected = true)
    {
        ValidateDrum(printer);
        var state = _printers[printer];
        if (state.Pending is IoEventToken token) CancelIo(token);
        state.Pending = null;
        state.Connected = connected;
        state.Running = false;
        state.Character = state.Feed = state.Length = state.Overprints = 0;
        Array.Clear(state.Strikes);
        if (connected) _ready &= ~(1U << (19 - printer));
        else _ready |= 1U << (19 - printer);
    }

    private void ControlPrinter(int printer, uint command)
    {
        var state = _printers[printer];
        if (!state.Connected) return;
        switch (command)
        {
            case 1:
                var line = new byte[state.Overprints][];
                for (int overprint = 0; overprint < line.Length; overprint++)
                {
                    line[overprint] = new byte[state.Length];
                    for (int position = 0; position < state.Length; position++)
                        line[overprint][position] = (byte)(state.Strikes[position, overprint] - 1);
                }
                PrinterOutput?.Invoke(printer, line);
                Array.Clear(state.Strikes);
                state.Length = state.Overprints = 0;
                _ready &= ~(1U << (23 - printer));
                state.Feed = 1;
                break;
            case 4:
                state.Running = true;
                _ready &= ~(1U << (23 - printer));
                state.Feed = 1;
                if (state.Pending is null) state.Pending = ScheduleIo(IoEventKind.PrinterCharacter, PrinterPulseTicks, () => PulsePrinter(printer));
                break;
            case 2: case 8: case 10:
                state.Running = false;
                if (state.Pending is IoEventToken token) CancelIo(token);
                state.Pending = null;
                break;
            default: throw Unsupported("printer control", command);
        }
    }

    private void PulsePrinter(int printer)
    {
        var state = _printers[printer];
        state.Pending = null;
        bool characterPulse = state.Character < 96;
        if (characterPulse)
        {
            state.Character++;
            _interrupts |= 1UL << (47 - printer);
            if (state.Feed != 0 && --state.Feed == 0) _ready |= 1U << (23 - printer);
        }
        else { state.Character = 0; _interrupts |= 1UL << (38 - printer); }
        if (state.Connected && state.Running)
            state.Pending = ScheduleIo(characterPulse ? IoEventKind.PrinterCharacter : IoEventKind.PrinterZero, characterPulse ? PrinterPulseTicks : PrinterZeroTicks, () => PulsePrinter(printer));
        NotifyInterrupts();
    }

    private void StrikePrinter(int printer, int position, uint mask)
    {
        var state = _printers[printer];
        if (!state.Connected) return;
        while (mask != 0)
        {
            if ((mask & 1) != 0)
            {
                int overprint = 0;
                while (overprint < 10 && state.Strikes[position, overprint] != 0) overprint++;
                if (overprint < 10)
                {
                    state.Strikes[position, overprint] = (byte)state.Character;
                    state.Length = Math.Max(state.Length, position + 1);
                    state.Overprints = Math.Max(state.Overprints, overprint + 1);
                }
            }
            mask >>= 1;
            position += 8;
        }
    }
    private sealed class Printer
    {
        public bool Connected;
        public bool Running;
        public int Character;
        public int Feed;
        public int Length;
        public int Overprints;
        public byte[,] Strikes { get; } = new byte[128, 10];
        public IoEventToken? Pending;
    }
}
