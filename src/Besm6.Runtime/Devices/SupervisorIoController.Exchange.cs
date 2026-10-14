using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private readonly DiskImage?[] _disks = new DiskImage?[16];
    private readonly DiskImage?[] _drums = new DiskImage?[2];
    private readonly IoEventToken?[] _drumPending = new IoEventToken?[2];
    private readonly Channel[] _channels = [new() { Unit = 0 }, new() { Unit = 8 }];
    private uint _diskErrors;
    private uint _drumErrors;

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
        channel.Pending = ScheduleIo(IoEventKind.Exchange, CompletionDelayTicks, () =>
        {
            TransferMedia(read, image.Words, serviceOffset, serviceCount, serviceAddress,
                dataOffset, read && serviceOnly ? 0 : count, address, () =>
            {
            channel.Pending = null;
            CompletedTransfers++;
            RaiseInterrupts(ReadyMask(index));
            }, () => channel.Pending = null);
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
        channel.Pending = ScheduleIo(IoEventKind.Exchange, CompletionDelayTicks, () =>
        {
            TransferMedia(true, words, 0, 4, serviceAddress, 0, 0, 0, () =>
            {
                channel.Pending = null;
                CompletedHeaderReads++;
                RaiseInterrupts(ReadyMask(index));
            }, () => channel.Pending = null);
        });
        NotifyInterrupts();
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
        _drumPending[controller] = ScheduleIo(IoEventKind.Exchange, CompletionDelayTicks, () =>
        {
            TransferMedia(read, image.Words, serviceOffset, serviceCount, serviceAddress,
                dataOffset, read && serviceOnly ? 0 : checked((int)count), address, () =>
            {
            _drumPending[controller] = null;
            CompletedDrumTransfers++;
            RaiseInterrupts(ready);
            }, () => _drumPending[controller] = null);
        });
        NotifyInterrupts();
    }
    private static void ValidateUnit(int unit)
    {
        if (unit is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(unit));
    }
    private sealed record DiskImage(MemoryWord50[] Words, bool ReadOnly);
    private sealed class Channel
    {
        public uint Command;
        public uint Memory;
        public int Unit;
        public int Zone;
        public int Half;
        public uint Status;
        public IoEventToken? Pending;
    }
}
