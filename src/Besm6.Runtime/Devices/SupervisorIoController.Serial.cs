using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private IoEventToken? _serialClock;
    private ulong _serialPeriod;
    private readonly SerialLine[] _serialLines = Enumerable.Range(0, 24).Select(_ => new SerialLine()).ToArray();
    private uint _serialOut;
    private uint _serialIn;

    public void ConfigureSerialTerminal(int line, bool connected = true)
    {
        ValidateSerialLine(line);
        var state = _serialLines[line - 1];
        state.Connected = connected;
        state.OutputStage = state.InputStage = 0;
        state.Output = 0;
        state.Input.Clear();
        _serialIn &= ~(1U << (24 - line));
    }

    public void ReceiveSerial(int line, ReadOnlySpan<byte> sevenBitCodes)
    {
        ValidateSerialLine(line);
        var state = _serialLines[line - 1];
        if (!state.Connected) throw new InvalidOperationException("Serial terminal is disconnected.");
        foreach (byte value in sevenBitCodes)
            if (value > 127) throw new ArgumentOutOfRangeException(nameof(sevenBitCodes));
        foreach (byte value in sevenBitCodes) state.Input.Enqueue(value);
    }

    public void StartSerialClock(ulong periodTicks)
    {
        RequireFunctionalTiming();
        StartSerialClockCore(periodTicks);
    }

    private void StartSerialClockCore(ulong periodTicks)
    {
        if (periodTicks == 0) throw new ArgumentOutOfRangeException(nameof(periodTicks));
        StopSerialClock();
        _serialPeriod = periodTicks;
        _serialClock = ScheduleIo(IoEventKind.Serial, periodTicks, SerialClockElapsed);
    }

    public void StopSerialClock()
    {
        if (_serialClock is IoEventToken token) CancelIo(token);
        _serialClock = null;
        _serialPeriod = 0;
    }

    private void SerialClockElapsed()
    {
        _serialClock = null;
        if (_serialPeriod != 0) _serialClock = ScheduleIo(IoEventKind.Serial, _serialPeriod, SerialClockElapsed);
        PulseSerialClock();
    }

    /// <summary>One serial bit clock. Inverted seven-bit data, parity and stop framing match SIMH vt_print/vt_receive.</summary>
    public void PulseSerialClock()
    {
        _serialIn = 0;
        for (int index = 0; index < _serialLines.Length; index++)
        {
            var line = _serialLines[index];
            if (!line.Connected) continue;
            uint mask = 1U << (23 - index);
            bool bit = (_serialOut & mask) != 0;
            if (line.OutputStage == 0)
            {
                if (bit) line.OutputStage = 1;
            }
            else if (line.OutputStage == 9)
            {
                if (bit) SerialFramingError?.Invoke(index + 1);
                else
                {
                    SerialOutput?.Invoke(index + 1, (byte)(~line.Output & 127));
                    line.OutputStage = 0;
                    line.Output = 0;
                }
            }
            else
            {
                if (bit) line.Output |= (byte)(1 << (line.OutputStage - 1));
                line.OutputStage++;
            }
            if (line.InputStage == 0)
            {
                if (line.Input.TryDequeue(out byte value))
                {
                    line.InputByte = value;
                    line.InputStage = 1;
                    _serialIn |= mask;
                    _interrupts |= SerialInputStart;
                }
            }
            else if (line.InputStage <= 7)
            {
                if ((line.InputByte & (1 << (line.InputStage - 1))) == 0) _serialIn |= mask;
                line.InputStage++;
            }
            else if (line.InputStage == 8)
            {
                if ((BitOperations.PopCount((uint)line.InputByte) & 1) == 0) _serialIn |= mask;
                line.InputStage++;
            }
            else if (line.InputStage == 12) line.InputStage = 0;
            else line.InputStage++;
        }
        _interrupts |= ExternalInterruptMask & SerialInterrupt;
        NotifyInterrupts();
    }

    private static void ValidateSerialLine(int line)
    {
        if (line is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(line));
    }

    private sealed class SerialLine
    {
        public bool Connected;
        public int OutputStage;
        public byte Output;
        public int InputStage;
        public byte InputByte;
        public Queue<byte> Input { get; } = new();
    }
}
