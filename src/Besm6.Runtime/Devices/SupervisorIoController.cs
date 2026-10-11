using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

/// <summary>
/// Selected DISPAK configuration: two drums, two KMD channels, sixteen disks and two byte
/// consoles. Register/port layouts follow pinned SIMH BESM6 (9e318d16), not its
/// host-time scheduling. CompletionDelayTicks is a functional delay, not hardware time.
/// DMA bypasses RP/RZ and both CPU buffers, as an external physical memory request.
/// </summary>
public sealed partial class SupervisorIoController : IHardwareSupervisorIo
{
    public SupervisorIoController HardwareController => this;
    public const int ZoneWords = 1032;
    public const int FormattedDiskZones = 1015; // DISK_TOTBLK 01767 in pinned SIMH.
    public const ulong Channel3Free = 1UL << 28;
    public const ulong Channel4Free = 1UL << 27;
    public const ulong TimerInterrupt = 1UL << 39;
    public const ulong SlowClockInterrupt = 1UL << 9;
    public const ulong PeripheralInterrupt = 1UL << 36;
    public const ulong Drum1Free = 1UL << 45;
    public const ulong Drum2Free = 1UL << 44;
    public const ulong SerialInterrupt = 1UL << 18;
    public const ulong SerialInputStart = 1UL << 30;
    private const ulong WiredInterrupts = (3UL << 44) | (15UL << 32) | (31UL << 24);
    private const uint WiredPeripheral = (3U << 20) | (15U << 14);
    private const uint PageMode = 1U << 18;
    private const uint ReadOperation = 1U << 17;
    private const uint SystemDataOnly = 1U << 20;
    private const uint GoodStatus = 0x300100;
    private readonly MappedMemoryBackend _memory;
    private readonly IEventScheduler _scheduler;
    private readonly Action _interruptChanged;
    private ulong _interrupts;
    private uint _peripheral;
    private uint _peripheralMask;
    private uint _ready = 3U << 18; // Negative printer readiness: one means disconnected.

    public ulong ExternalInterrupts => _interrupts;
    public ulong ExternalInterruptMask { get; private set; }
    public ulong PendingExternalInterrupts => _interrupts & ExternalInterruptMask;
    public uint PeripheralInterrupts => _peripheral;
    public uint PeripheralInterruptMask => _peripheralMask;
    private ulong _completionDelayTicks = 20;
    public ulong CompletionDelayTicks
    {
        get => _completionDelayTicks;
        set { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); _completionDelayTicks = value; }
    }
    private ulong? _printerPulseTicks, _printerZeroTicks;
    /// <summary>Functional character/zero-event periods, independent of DMA.
    /// Defaults retain CompletionDelayTicks; these are not physical hardware times.</summary>
    public ulong PrinterPulseTicks
    {
        get => _printerPulseTicks ?? CompletionDelayTicks;
        set { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); _printerPulseTicks = value; }
    }
    public ulong PrinterZeroTicks
    {
        get => _printerZeroTicks ?? CompletionDelayTicks;
        set { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); _printerZeroTicks = value; }
    }
    public int CompletedTransfers { get; private set; }
    public int CompletedHeaderReads { get; private set; }
    public int CompletedDrumTransfers { get; private set; }
    public event Action<int, byte>? ConsoleOutput;
    public event Action<int, byte>? SerialOutput;
    /// <summary>Host observation of framing/queued input; does not change guest interrupts.</summary>
    public bool IsSerialIdle
    {
        get
        {
            for (int index = 0; index < _serialLines.Length; index++)
            {
                var line = _serialLines[index];
                if (line.Connected && (line.OutputStage != 0 || line.InputStage != 0 ||
                    line.Input.Count != 0 || (_serialOut & (1U << (23 - index))) != 0)) return false;
            }
            return true;
        }
    }
    public event Action<int>? SerialFramingError;
    /// <summary>One printed line, with up to ten overprints; bytes are GOST codes, 255 denotes blank.</summary>
    public event Action<int, byte[][]>? PrinterOutput;

    public SupervisorIoController(MappedMemoryBackend memory, IEventScheduler scheduler,
        Action externalInterruptChanged)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _interruptChanged = externalInterruptChanged ?? throw new ArgumentNullException(nameof(externalInterruptChanged));
    }

    public Word48 ReadRegister(uint register) => register is 31 or 159
        ? new(_interrupts) : throw Unsupported("register read", register);

    public void WriteRegister(uint register, Word48 value)
    {
        switch (register)
        {
            case 30: ExternalInterruptMask = value.Value; break; // REG 036
            case 31: _interrupts &= value.Value | WiredInterrupts; break; // REG 037
            default: throw Unsupported("register write", register);
        }
        NotifyInterrupts();
    }

    public void RaiseInterrupts(ulong bits)
    {
        _interrupts |= bits & Word48.Mask48;
        NotifyInterrupts();
    }

    public Word48 ReadDevice(uint address)
    {
        uint port = address & 0x87F; // Aex & 04177
        return port switch
        {
            2051 or 2052 => new(_channels[port - 2051].Status), // 04003/04004
            2072 => new(_peripheral & 0xFFF000), // 04030
            2060 or 2061 => new(_paperReaders[port - 2060].Register), // 04014/04015 FS byte
            2073 => new(_ready), // 04031: mixed positive FS/negative printer readiness
            2076 => new((_peripheral & 0xFFF) | 0xFF), // 04034: wired unused low bits
            2077 => new(_diskErrors | _drumErrors), // 04035
            2112 => new(_serialIn), // 04100: multiplexed serial input bits
            2114 => new(0x880000), // 04102: disconnected card readers, SIMH reset READY2
            >= 2115 and <= 2118 => new(0xFFFFFF), // 04103..04106: tape drives not ready
            // Pinned SIMH cmd033 returns zero for this explicitly unknown poll.
            // This compatibility response belongs only to the experimental adapter;
            // it is not a specification of a historical hardware register.
            2125 when _memory.PhysicalMemory.Configuration == MemoryConfiguration.Simh512K => Word48.Zero,
            2172 or 2173 => new(_consoleInput[port - 2172]), // 04174/04175
            2175 => Word48.Zero, // 04177: disconnected display panel
            _ => throw Unsupported("device read", address)
        };
    }

    public void WriteDevice(uint address, Word48 value)
    {
        uint port = address & 0x87F;
        uint command = (uint)value.Value;
        switch (port)
        {
            case 0: break; // Momentary printer solenoid release: completed strikes are retained, as in pinned SIMH.
            case 1: case 2: TransferDrum((int)port - 1, command); break;
            case 3: case 4: SetupDisk((int)port - 3, command); break;
            case 8: case 9: ControlPaperReader((int)port - 8, command & 7); break;
            case 12: case 13: ControlPrinter((int)port - 12, command & 15); break;
            case 19: case 20: ControlDisk((int)port - 19, command); break; // 023/024
            case 24: _peripheral &= (command & 0xFFFFFF) | WiredPeripheral; NotifyInterrupts(); break; // 030
            case 25: RaiseInterrupts((ulong)(command & 0xFFFFFF) << 24); break; // 031
            case 28: _peripheralMask = command & 0xFFFFFF; NotifyInterrupts(); break; // 034
            case 96: _serialOut = command & 0xFFFFFF; break; // 0140 serial output bits
            case 103: break; // 0147 power supply control has no simulated observable effect
            case 107:
                // Selected SIMH adapter behavior, cmd_033 case 0153, revision 9e318d16:
                // https://github.com/simh/simh/blob/9e318d16a203a3efae3420ea3f5700a8d7e3bec3/BESM6/besm6_cpu.c
                // The named interface signal extinguishing has no observable effect
                // there. Historical reset signals/timing are unresolved: do not
                // invent a UART reset or discard active framing/input/clock events.
                break;
            case 124: case 125: PrintConsole((int)port - 124, (byte)command); break;
            case 127: break; // 0177 disconnected display
            default:
                if (port is >= 32 and <= 47) { StrikePrinter(port >= 40 ? 1 : 0, (int)(port & 7), command & 65535); break; }
                if (port is >= 64 and <= 95) break; // Disconnected tape motor controls, as SIMH
                throw Unsupported("device write", address);
        }
    }

    /// <summary>Host console injection; guest acknowledgement is through PRP, not a read.</summary>
    private void NotifyInterrupts()
    {
        if ((_peripheral & _peripheralMask) != 0) _interrupts |= PeripheralInterrupt;
        else _interrupts &= ~PeripheralInterrupt;
        _interruptChanged();
    }
    private static ulong ReadyMask(int index) => index == 0 ? Channel3Free : Channel4Free;
    private static uint FailureMask(int index) => index == 0 ? 16U : 8U;
    private static void ValidateDrum(int controller)
    {
        if (controller is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(controller));
    }

    private static NotSupportedException Unsupported(string operation, uint address)
        => new($"Unsupported supervisor {operation}: octal {Convert.ToString(address, 8)}.");
}
