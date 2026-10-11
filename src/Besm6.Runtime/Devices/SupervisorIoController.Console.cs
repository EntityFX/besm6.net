using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private readonly byte[] _consoleInput = new byte[2];
    private readonly bool[] _consoleConnected = new bool[2];
    private readonly IoEventToken?[] _consolePending = new IoEventToken?[2];

    public void ReceiveConsole(int channel, byte sevenBitCode)
    {
        if (channel is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(channel));
        if (sevenBitCode > 127) throw new ArgumentOutOfRangeException(nameof(sevenBitCode));
        if (!_consoleConnected[channel]) throw new InvalidOperationException("Parallel console is disconnected.");
        _consoleInput[channel] = (byte)(sevenBitCode | ((BitOperations.PopCount((uint)sevenBitCode) & 1) << 7));
        _peripheral |= 1U << (11 - channel);
        NotifyInterrupts();
    }

    private void PrintConsole(int channel, byte value)
    {
        if (!_consoleConnected[channel]) return; // No connected device can assert a completion signal.
        if (_consolePending[channel] is not null) throw new InvalidOperationException("Parallel console output is already pending.");
        ConsoleOutput?.Invoke(channel, (byte)(value & 127));
        _consolePending[channel] = ScheduleIo(IoEventKind.Console, CompletionDelayTicks, () =>
        {
            _consolePending[channel] = null;
            _peripheral |= 1U << (9 - channel);
            NotifyInterrupts();
        });
    }

    /// <summary>Selected configuration attachment. An attached typewriter initially asserts CAN_PRINT (SIMH tty_reset).</summary>
    public void ConfigureConsole(int channel, bool connected = true)
    {
        ValidateDrum(channel);
        if (_consolePending[channel] is IoEventToken token) CancelIo(token);
        _consolePending[channel] = null;
        _consoleConnected[channel] = connected;
        _consoleInput[channel] = 0;
        _peripheral &= ~((1U << (9 - channel)) | (1U << (11 - channel)));
        if (connected) _peripheral |= 1U << (9 - channel);
        NotifyInterrupts();
    }

}
