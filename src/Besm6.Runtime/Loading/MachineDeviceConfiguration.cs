using System.Security.Cryptography;
using System.Text;

namespace Besm6.Runtime;

/// <summary>Explicit startup attachments. File names resolve against the job file,
/// images remain in memory, and no media is written back to a host path.</summary>
public sealed class MachineDeviceConfiguration
{
    public List<MachineDiskImage> DiskImages { get; set; } = new();
    public List<MachineScratchDisk> ScratchDisks { get; set; } = new();
    public List<int> ScratchDrums { get; set; } = new();
    public List<int> SerialTerminals { get; set; } = new();
    public List<int> Printers { get; set; } = new();
    public List<MachinePaperTape> PaperTapes { get; set; } = new();
    public bool StartTimer { get; set; }
    public bool IncludeSlowClock { get; set; }
    public bool StartSerialClock { get; set; }
    public ulong TimerPeriodTicks { get; set; }
    public ulong SerialPeriodTicks { get; set; }

    internal Action<SupervisorIoController> Prepare(string directory, MachineExecutionModel model)
    {
        ArgumentNullException.ThrowIfNull(DiskImages); ArgumentNullException.ThrowIfNull(ScratchDisks);
        ArgumentNullException.ThrowIfNull(ScratchDrums); ArgumentNullException.ThrowIfNull(SerialTerminals);
        ArgumentNullException.ThrowIfNull(Printers); ArgumentNullException.ThrowIfNull(PaperTapes);
        if (model == MachineExecutionModel.Hardware && (TimerPeriodTicks != 0 || SerialPeriodTicks != 0))
            throw new ArgumentException("Hardware clocks use the timing configuration, not tick periods.");
        if (model == MachineExecutionModel.Functional &&
            (StartTimer && TimerPeriodTicks == 0 || StartSerialClock && SerialPeriodTicks == 0))
            throw new ArgumentException("Enabled functional clocks require positive tick periods.");
        var units = new HashSet<int>();
        var disks = new List<(int Unit, byte[] Bytes, bool ReadOnly)>();
        foreach (var disk in DiskImages)
        {
            if (disk is null || disk.Unit is < 0 or > 15 || !units.Add(disk.Unit))
                throw new ArgumentException("Disk units must be unique and within 0..15.");
            var bytes = ReadAsset(directory, disk.File, disk.Sha256);
            if (bytes.Length == 0 || bytes.Length % (SupervisorIoController.ZoneWords * 8) != 0)
                throw new ArgumentException("Invalid SIMH disk image length.");
            disks.Add((disk.Unit, bytes, disk.ReadOnly));
        }
        foreach (var disk in ScratchDisks)
            if (disk is null || disk.Unit is < 0 or > 15 || !units.Add(disk.Unit) || disk.Volume is < 2048 or > 4095)
                throw new ArgumentException("Scratch disk unit/volume is invalid or duplicated.");
        ValidateUnits(ScratchDrums, 0, 1); ValidateUnits(SerialTerminals, 1, 24); ValidateUnits(Printers, 0, 1);
        var readers = new HashSet<int>();
        var tapes = new List<(int Reader, string Text)>();
        foreach (var tape in PaperTapes)
        {
            if (tape is null || tape.Reader is < 0 or > 1 || !readers.Add(tape.Reader))
                throw new ArgumentException("Paper readers must be unique and within 0..1.");
            var bytes = ReadAsset(directory, tape.File, tape.Sha256);
            tapes.Add((tape.Reader, new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')));
        }
        // Capture startup values so mutation of the DTO cannot change an admitted plan.
        var scratch = ScratchDisks.Select(disk => (disk.Unit, disk.Volume)).ToArray();
        var drums = ScratchDrums.ToArray(); var terminals = SerialTerminals.ToArray(); var printers = Printers.ToArray();
        bool timer = StartTimer, slow = IncludeSlowClock, serial = StartSerialClock;
        ulong timerPeriod = TimerPeriodTicks, serialPeriod = SerialPeriodTicks;
        return io =>
        {
            foreach (var disk in disks) io.AttachSimhDiskImage(disk.Unit, disk.Bytes, disk.ReadOnly);
            foreach (var disk in scratch) io.CreateScratchDisk(disk.Unit, disk.Volume);
            foreach (int drum in drums) io.CreateScratchDrum(drum);
            foreach (int terminal in terminals) io.ConfigureSerialTerminal(terminal);
            foreach (int printer in printers) io.ConfigurePrinter(printer);
            foreach (var tape in tapes) io.AttachPaperTapeText(tape.Reader, tape.Text);
            if (model == MachineExecutionModel.Hardware)
            {
                if (serial) io.StartHardwareSerialClock();
                if (timer) io.StartHardwareTimer(slow);
            }
            else
            {
                if (serial) io.StartSerialClock(serialPeriod);
                if (timer) io.StartTimer(timerPeriod, slow);
            }
        };
    }

    private static void ValidateUnits(IEnumerable<int> units, int first, int last)
    {
        var seen = new HashSet<int>();
        foreach (int unit in units)
            if (unit < first || unit > last || !seen.Add(unit))
                throw new ArgumentException("Device units are invalid or duplicated.");
    }

    private static byte[] ReadAsset(string directory, string file, string sha256)
    {
        if (string.IsNullOrWhiteSpace(file) || sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("An attachment requires a file and a 64-digit SHA256.");
        var bytes = System.IO.File.ReadAllBytes(Path.GetFullPath(file, directory));
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Attachment SHA256 mismatch.");
        return bytes;
    }
}

public sealed class MachineDiskImage
{
    public int Unit { get; set; }
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool ReadOnly { get; set; } = true;
}

public sealed class MachineScratchDisk
{
    public int Unit { get; set; }
    public int Volume { get; set; } = 2052;
}

public sealed class MachinePaperTape
{
    public int Reader { get; set; }
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
