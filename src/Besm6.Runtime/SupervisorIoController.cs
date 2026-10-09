using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

/// <summary>
/// Selected DISPAK configuration: two drums, two KMD channels, sixteen disks and two byte
/// consoles. Register/port layouts follow pinned SIMH BESM6 (9e318d16), not its
/// host-time scheduling. CompletionDelayTicks is a functional delay, not hardware time.
/// DMA bypasses RP/RZ and both CPU buffers, as an external physical memory request.
/// </summary>
public sealed class SupervisorIoController : ISupervisorIo
{
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
    private readonly DiskImage?[] _disks = new DiskImage?[16];
    private readonly DiskImage?[] _drums = new DiskImage?[2];
    private readonly EventToken?[] _drumPending = new EventToken?[2];
    private readonly Channel[] _channels = [new() { Unit = 0 }, new() { Unit = 8 }];
    private readonly byte[] _consoleInput = new byte[2];
    private readonly bool[] _consoleConnected = new bool[2];
    private readonly EventToken?[] _consolePending = new EventToken?[2];
    private ulong _interrupts;
    private uint _peripheral;
    private uint _peripheralMask;
    private uint _diskErrors;
    private uint _drumErrors;
    private EventToken? _timer;
    private ulong _timerPeriod;
    private bool _includeSlowClock;
    private uint _timerPulses;
    private EventToken? _serialClock;
    private ulong _serialPeriod;
    private readonly SerialLine[] _serialLines = Enumerable.Range(0, 24).Select(_ => new SerialLine()).ToArray();
    private uint _serialOut;
    private uint _serialIn;
    private uint _ready = 3U << 18; // Negative printer readiness: one means disconnected.
    private readonly PaperReader[] _paperReaders = [new(), new()];
    private readonly Printer[] _printers = [new(), new()];

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

    /// <summary>Attach actual raw 50-bit words. The image is copied, never shared.</summary>
    public void AttachDisk(int unit, ReadOnlySpan<MemoryWord50> words, bool readOnly = true)
    {
        ValidateUnit(unit);
        if (_channels[unit / 8].Pending is not null) throw new InvalidOperationException("Cannot replace a disk during an exchange.");
        if (words.Length == 0 || words.Length % ZoneWords != 0)
            throw new ArgumentException("A disk image consists of zones of eight service words and 1024 data words.", nameof(words));
        _disks[unit] = new(words.ToArray(), readOnly);
    }

    /// <summary>
    /// SIMH images use little-endian 64-bit slots with semantic tags 1 (command)
    /// and 2 (number), not physical parity. Compute real control bits per payload.
    /// Untagged/invalid slots are rejected rather than guessed to be command words.
    /// </summary>
    public void AttachSimhDiskImage(int unit, ReadOnlySpan<byte> bytes, bool readOnly = true)
    {
        if (bytes.Length == 0 || bytes.Length % (ZoneWords * 8) != 0)
            throw new ArgumentException("Invalid SIMH disk image length.", nameof(bytes));
        var words = new MemoryWord50[bytes.Length / 8];
        for (int i = 0; i < words.Length; i++)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i * 8, 8));
            ulong tag = value >> 48;
            if (tag is not (1 or 2)) throw new ArgumentException($"Unsupported SIMH parity class at word {i}.", nameof(bytes));
            words[i] = MemoryWord50.Form(new(value), tag == 2, tag == 2);
        }
        AttachDisk(unit, words, readOnly);
    }

    public MemoryWord50[] SnapshotDisk(int unit)
    {
        ValidateUnit(unit);
        return (_disks[unit] ?? throw new InvalidOperationException("Disk is not attached.")).Words.ToArray();
    }

    /// <summary>
    /// Explicit host formatter matching SIMH besm6_disk.c disk_attach(-n), revision
    /// 9e318d16: 1015 zones; two four-word track headers, volume/key mark and numeric
    /// zero data. Control bits are recomputed from the final payload rather than
    /// copying SIMH's semantic parity class. This does not implement guest FORMAT.
    /// </summary>
    public void CreateScratchDisk(int unit, int volume = 2052)
    {
        ValidateUnit(unit);
        if (volume is < 2048 or > 4095) throw new ArgumentOutOfRangeException(nameof(volume));
        var words = new MemoryWord50[FormattedDiskZones * ZoneWords];
        Array.Fill(words, MemoryWord50.Form(Word48.Zero, true, true));
        var key = MemoryWord50.Form(new((0x5F1C7UL << 24) | ((ulong)(uint)volume << 12)), true, true);
        for (int zone = 0; zone < FormattedDiskZones; zone++)
        {
            int offset = zone * ZoneWords;
            words[offset] = MemoryWord50.Form(new((ulong)(uint)(2 * zone) << 36), true, true);
            words[offset + 4] = MemoryWord50.Form(new((ulong)(uint)(2 * zone + 1) << 36), true, true);
            words[offset + 1] = words[offset + 5] = key;
        }
        AttachDisk(unit, words, readOnly: false);
    }

    public void AttachDrum(int controller, ReadOnlySpan<MemoryWord50> words, bool readOnly = false)
    {
        ValidateDrum(controller);
        if (_drumPending[controller] is not null) throw new InvalidOperationException("Cannot replace a drum during an exchange.");
        if (words.Length != 256 * ZoneWords)
            throw new ArgumentException("The selected drum controller addresses 256 zones.", nameof(words));
        _drums[controller] = new(words.ToArray(), readOnly);
        RaiseInterrupts(controller == 0 ? Drum1Free : Drum2Free);
    }

    /// <summary>Explicit host software initialization of a writable, zero-filled drum.</summary>
    public void CreateScratchDrum(int controller)
    {
        var words = new MemoryWord50[256 * ZoneWords];
        Array.Fill(words, MemoryWord50.Form(Word48.Zero, true, true));
        AttachDrum(controller, words);
    }

    public MemoryWord50[] SnapshotDrum(int controller)
    {
        ValidateDrum(controller);
        return (_drums[controller] ?? throw new InvalidOperationException("Drum is not attached.")).Words.ToArray();
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

    public void StartTimer(ulong periodTicks) => StartTimer(periodTicks, includeSlowClock: false);

    /// <summary>Optional selected-SIMH adapter: fast_clk at pinned revision
    /// 9e318d16a203a3efae3420ea3f5700a8d7e3bec3 raises GRP bit 10 every fourth pulse.
    /// This later undocumented addition is separate from the book configuration;
    /// periodTicks remains a functional scheduling delay, not hardware timing.</summary>
    public void StartTimer(ulong periodTicks, bool includeSlowClock)
    {
        if (periodTicks == 0) throw new ArgumentOutOfRangeException(nameof(periodTicks));
        StopTimer();
        _timerPeriod = periodTicks;
        _includeSlowClock = includeSlowClock;
        _timer = _scheduler.Schedule(periodTicks, TimerElapsed);
    }

    public void StopTimer()
    {
        if (_timer is EventToken token) _scheduler.Cancel(token);
        _timer = null;
        _timerPeriod = 0;
        _includeSlowClock = false;
        _timerPulses = 0;
    }

    private void TimerElapsed()
    {
        _timer = null;
        ulong signals = TimerInterrupt;
        if (_includeSlowClock && (++_timerPulses & 3) == 0) signals |= SlowClockInterrupt;
        RaiseInterrupts(signals);
        if (_timerPeriod != 0) _timer = _scheduler.Schedule(_timerPeriod, TimerElapsed);
    }

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
        if (periodTicks == 0) throw new ArgumentOutOfRangeException(nameof(periodTicks));
        StopSerialClock();
        _serialPeriod = periodTicks;
        _serialClock = _scheduler.Schedule(periodTicks, SerialClockElapsed);
    }

    public void StopSerialClock()
    {
        if (_serialClock is EventToken token) _scheduler.Cancel(token);
        _serialClock = null;
        _serialPeriod = 0;
    }

    private void SerialClockElapsed()
    {
        _serialClock = null;
        PulseSerialClock();
        if (_serialPeriod != 0) _serialClock = _scheduler.Schedule(_serialPeriod, SerialClockElapsed);
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
        _consolePending[channel] = _scheduler.Schedule(CompletionDelayTicks, () =>
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
        if (_consolePending[channel] is EventToken token) _scheduler.Cancel(token);
        _consolePending[channel] = null;
        _consoleConnected[channel] = connected;
        _consoleInput[channel] = 0;
        _peripheral &= ~((1U << (9 - channel)) | (1U << (11 - channel)));
        if (connected) _peripheral |= 1U << (9 - channel);
        NotifyInterrupts();
    }

    private void SetupDisk(int index, uint command)
    {
        var channel = _channels[index];
        if (channel.Pending is not null) throw new InvalidOperationException("Disk channel already has a pending exchange.");
        uint block = (command & 0x7800000) >> 8;
        uint page = (command & ((command & PageMode) != 0 ? 0x1F000U : 0x1F800U)) >> 2;
        uint address = block | page;
        uint length = (command & PageMode) != 0 ? 1024U : 512U;
        if (address + length > _memory.PhysicalMemory.CapacityWords)
            throw new ArgumentOutOfRangeException(nameof(command), "The selected memory configuration cannot address this DMA range.");
        channel.Command = command;
        channel.Memory = address;
        _diskErrors &= ~FailureMask(index);
        _interrupts &= ~ReadyMask(index);
        NotifyInterrupts();
    }

    private void ControlDisk(int index, uint command)
    {
        var channel = _channels[index];
        if (channel.Pending is not null) throw new InvalidOperationException("Disk channel already has a pending exchange.");
        if ((command & 0x800) != 0)
        {
            BeginTransfer(index, (int)((command >> 1) & 1023), (int)(command & 1));
        }
        else if ((command & 0x400) != 0)
        {
            uint mask = command & 255;
            if (mask == 0)
            {
                // Pinned disk_ctl: an empty KMD unit mask sets dev=-1 and returns
                // without changing ready/error signals. Subsequent access has no
                // attached device; preserve this state without C's invalid array index.
                channel.Unit = -1;
                return;
            }
            channel.Unit = index * 8 + BitOperations.Log2(mask);
            if (SelectedDisk(channel) is null) _diskErrors |= FailureMask(index);
            RaiseInterrupts(ReadyMask(index));
        }
        else if ((command & 0x100) != 0) RaiseInterrupts(ReadyMask(index));
        else
        {
            switch (command & 63)
            {
                case 6:
                    // Selected pinned SIMH disk_ctl case006: compare-codes strobe
                    // has no observable effect. This does not implement a physical comparator.
                    break;
                case 0: case 1: case 2: case 3: case 4: case 35: case 36: case 40:
                    break; // Initialization, positioning and read/write direction strobes; actual transfer on address strobe.
                case 8: channel.Status = 0; break;
                case 9: channel.Status = SelectedDisk(channel) is null ? 0 : GoodStatus & 4095; break;
                case 25: channel.Status = SelectedDisk(channel) is null ? 0 : GoodStatus >> 12; break;
                case 7: case 39: ReadTrackHeader(index); break;
                default: throw Unsupported("disk control", command);
            }
        }
    }

    private DiskImage? SelectedDisk(Channel channel) => channel.Unit < 0 ? null : _disks[channel.Unit];

    private void BeginTransfer(int index, int zone, int half)
    {
        var channel = _channels[index];
        var image = SelectedDisk(channel);
        if (image is null || zone >= image.Words.Length / ZoneWords || ((channel.Command & ReadOperation) == 0 && image.ReadOnly))
        {
            _diskErrors |= FailureMask(index);
            NotifyInterrupts();
            return;
        }
        uint command = channel.Command;
        channel.Zone = zone;
        channel.Half = half;
        uint address = channel.Memory;
        bool page = (command & PageMode) != 0;
        bool read = (command & ReadOperation) != 0;
        bool serviceOnly = (command & SystemDataOnly) != 0;
        int count = page ? 1024 : 512;
        int serviceCount = page ? 8 : 4;
        int serviceOffset = zone * ZoneWords + (page ? 0 : half * 4);
        int dataOffset = zone * ZoneWords + 8 + (page ? 0 : half * 512);
        uint serviceAddress = (uint)(index == 0 ? 24 : 32) + (uint)(page ? 0 : half * 4);
        _interrupts &= ~ReadyMask(index);
        // Snapshot the request, but read memory/data when the exchange completes.
        channel.Pending = _scheduler.Schedule(CompletionDelayTicks, () =>
        {
            if (read)
            {
                for (int i = 0; i < serviceCount; i++) _memory.PhysicalMemory.WriteRaw(serviceAddress + (uint)i, image.Words[serviceOffset + i]);
                if (!serviceOnly)
                    for (int i = 0; i < count; i++) _memory.PhysicalMemory.WriteRaw(address + (uint)i, image.Words[dataOffset + i]);
            }
            else
            {
                // Full-zone and half-zone service areas use the same selection as data.
                for (int i = 0; i < serviceCount; i++) image.Words[serviceOffset + i] = _memory.PhysicalMemory.ReadRaw(serviceAddress + (uint)i);
                for (int i = 0; i < count; i++) image.Words[dataOffset + i] = _memory.PhysicalMemory.ReadRaw(address + (uint)i);
            }
            channel.Pending = null;
            CompletedTransfers++;
            RaiseInterrupts(ReadyMask(index));
        });
        NotifyInterrupts();
    }

    // Track-address comb encoding from pinned besm6_disk.c disk_read_header/collect.
    // This is the selected KMD format, not a claim about every BESM disk controller.
    private void ReadTrackHeader(int index)
    {
        var channel = _channels[index];
        if (SelectedDisk(channel) is null)
        {
            _diskErrors |= FailureMask(index);
            NotifyInterrupts();
            return;
        }
        int absoluteHead = channel.Zone * 2 + channel.Half;
        uint addressCode = (uint)((absoluteHead / 10 << 20) | (absoluteHead % 10 << 16));
        if (channel.Zone >= 1000) addressCode |= 1U << 29;
        uint a = addressCode >> 12, b = addressCode >> 24;
        while (b != 0) { uint carry = a & b; a ^= b; b = carry >> 1; }
        addressCode |= ~a & 4095;
        ulong[] header = [0xF04000000000UL | (ulong)addressCode << 8, 0x7E0,
            0x100000003FFFUL | (ulong)addressCode << 14, Word48.Mask48];
        var words = new MemoryWord50[4];
        for (int i = 0; i < 4; i++)
        {
            ulong comb = 0;
            for (int column = 0; column < 5; column++)
                for (int row = 0; row < 9; row++)
                    comb |= ((header[i] >> (column * 9 + row)) & 1) << (column + row * 5);
            words[i] = MemoryWord50.Form(new(comb), true, true);
        }
        uint serviceAddress = (uint)(index == 0 ? 24 : 32) + (uint)(channel.Half * 4);
        _diskErrors &= ~FailureMask(index);
        _interrupts &= ~ReadyMask(index);
        channel.Pending = _scheduler.Schedule(CompletionDelayTicks, () =>
        {
            for (int i = 0; i < 4; i++) _memory.PhysicalMemory.WriteRaw(serviceAddress + (uint)i, words[i]);
            channel.Pending = null;
            CompletedHeaderReads++;
            RaiseInterrupts(ReadyMask(index));
        });
        NotifyInterrupts();
    }

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

    public void AttachPaperTape(int reader, ReadOnlySpan<byte> holes)
    {
        ValidateDrum(reader);
        var state = _paperReaders[reader];
        if (state.Pending is EventToken token) _scheduler.Cancel(token);
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
                if (state.Pending is EventToken token) _scheduler.Cancel(token);
                state.Pending = null; state.Mode = 0; break;
            case 4:
                if (state.Pending is EventToken starting) _scheduler.Cancel(starting);
                state.Pending = null; state.Mode = 1; break;
            case 5:
                // Pinned fs_control/sim_activate: no feed without a running motor,
                // and a second activation of an active unit retains its deadline.
                if (state.Mode == 0 || state.Pending is not null) return;
                if (state.Mode == 3) { _ready &= ~(1U << (15 - reader)); state.Mode = 0; break; }
                state.Pending = _scheduler.Schedule(CompletionDelayTicks, () =>
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

    public void ConfigurePrinter(int printer, bool connected = true)
    {
        ValidateDrum(printer);
        var state = _printers[printer];
        if (state.Pending is EventToken token) _scheduler.Cancel(token);
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
                if (state.Pending is null) state.Pending = _scheduler.Schedule(PrinterPulseTicks, () => PulsePrinter(printer));
                break;
            case 2: case 8: case 10:
                state.Running = false;
                if (state.Pending is EventToken token) _scheduler.Cancel(token);
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
        NotifyInterrupts();
        if (state.Connected && state.Running)
            state.Pending = _scheduler.Schedule(characterPulse ? PrinterPulseTicks : PrinterZeroTicks, () => PulsePrinter(printer));
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
    private static void ValidateSerialLine(int line)
    {
        if (line is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(line));
    }

    private void TransferDrum(int controller, uint command)
    {
        if (_drumPending[controller] is not null) throw new InvalidOperationException("Drum channel already has a pending exchange.");
        if ((command & 0x400000) != 0 || ((command & ReadOperation) == 0 && (command & 0x200000) != 0))
            throw Unsupported("drum overlay or malformed-parity write", command); // Also unimplemented in pinned SIMH.
        bool page = (command & PageMode) != 0;
        bool read = (command & ReadOperation) != 0;
        bool serviceOnly = (command & SystemDataOnly) != 0;
        uint address = (command & (page ? 0x1F000U : 0x1FC00U)) >> 2 | (command & 0x7800000) >> 8;
        uint count = page ? 1024U : 256U;
        if (address + count > _memory.PhysicalMemory.CapacityWords)
            throw new ArgumentOutOfRangeException(nameof(command), "The selected memory configuration cannot address this DMA range.");
        int zone = (int)((command & 0x3FC) >> 2);
        int sector = page ? 0 : (int)(command & 3);
        int serviceCount = page ? 8 : 2;
        int serviceOffset = zone * ZoneWords + sector * 2;
        int dataOffset = zone * ZoneWords + 8 + sector * 256;
        uint serviceAddress = (uint)(controller == 0 ? 8 : 16) + (uint)(sector * 2);
        uint failure = controller == 0 ? 64U : 32U;
        var image = _drums[controller];
        if (image is null || (!read && image.ReadOnly))
        {
            _drumErrors |= failure;
            NotifyInterrupts();
            return;
        }
        ulong ready = controller == 0 ? Drum1Free : Drum2Free;
        _drumErrors &= ~failure;
        _interrupts &= ~ready;
        _drumPending[controller] = _scheduler.Schedule(CompletionDelayTicks, () =>
        {
            if (read)
            {
                for (int i = 0; i < serviceCount; i++) _memory.PhysicalMemory.WriteRaw(serviceAddress + (uint)i, image.Words[serviceOffset + i]);
                if (!serviceOnly)
                    for (uint i = 0; i < count; i++) _memory.PhysicalMemory.WriteRaw(address + i, image.Words[dataOffset + i]);
            }
            else
            {
                for (int i = 0; i < serviceCount; i++) image.Words[serviceOffset + i] = _memory.PhysicalMemory.ReadRaw(serviceAddress + (uint)i);
                for (uint i = 0; i < count; i++) image.Words[dataOffset + i] = _memory.PhysicalMemory.ReadRaw(address + i);
            }
            _drumPending[controller] = null;
            CompletedDrumTransfers++;
            RaiseInterrupts(ready);
        });
        NotifyInterrupts();
    }
    private static void ValidateUnit(int unit)
    {
        if (unit is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(unit));
    }
    private static NotSupportedException Unsupported(string operation, uint address)
        => new($"Unsupported supervisor {operation}: octal {Convert.ToString(address, 8)}.");
    private sealed record DiskImage(MemoryWord50[] Words, bool ReadOnly);
    private sealed class SerialLine
    {
        public bool Connected;
        public int OutputStage;
        public byte Output;
        public int InputStage;
        public byte InputByte;
        public Queue<byte> Input { get; } = new();
    }
    private sealed class PaperReader
    {
        public byte[] Data = [];
        public int Position;
        public int Mode;
        public byte Register;
        public EventToken? Pending;
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
        public EventToken? Pending;
    }
    private sealed class Channel
    {
        public uint Command;
        public uint Memory;
        public int Unit;
        public int Zone;
        public int Half;
        public uint Status;
        public EventToken? Pending;
    }
}
