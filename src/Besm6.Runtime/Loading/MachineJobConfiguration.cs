using System.Text.Json;
using System.Text.Json.Serialization;
using Besm6.Runtime.Timing;

namespace Besm6.Runtime;

/// <summary>Standalone supervisor machine image and explicit logical timing.
/// Addresses and duration values are decimal; words contain raw 50-bit controls.</summary>
public sealed class MachineJobConfiguration
{
    public string ExecutionModel { get; set; } = "functional";
    public string Speed { get; set; } = "max";
    public string MemoryConfiguration { get; set; } = "classical32k";
    public uint EntryAddress { get; set; } = 8;
    public uint ControlFlags { get; set; } = 0x403;
    public long InstructionLimit { get; set; } = 1_000_000;
    public List<MachineImageWord> MemoryWords { get; set; } = new();
    public MachineTimingConfiguration? Timing { get; set; }
    public MachineDeviceConfiguration? Devices { get; set; }
    private string _assetDirectory = Directory.GetCurrentDirectory();

    public static MachineJobConfiguration Load(string path)
    {
        var absolute = Path.GetFullPath(path);
        var job = JsonSerializer.Deserialize<MachineJobConfiguration>(File.ReadAllText(absolute), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
        ?? throw new JsonException("Machine configuration must be an object.");
        job._assetDirectory = Path.GetDirectoryName(absolute)!;
        return job;
    }

    public MachineExecutionModel ParseExecutionModel() => ParseExecutionModel(ExecutionModel);
    public static MachineExecutionModel ParseExecutionModel(string value) => value switch
    {
        "functional" => MachineExecutionModel.Functional,
        "hardware" => MachineExecutionModel.Hardware,
        _ => throw new ArgumentException("executionModel must be 'functional' or 'hardware'.")
    };

    public ExecutionSpeed ParseSpeed() => ExecutionSpeedNames.TryParse(Speed, out var speed) ? speed :
        throw new ArgumentException("speed must be 'original' or 'max' or 'virtual'.");

    public MachineCore CreateMachine(MachineExecutionModel? executionOverride = null)
    {
        var selected = ParseExecutionModel(); _ = ParseSpeed();
        if (executionOverride is { } forced)
        {
            if (!Enum.IsDefined(forced)) throw new ArgumentOutOfRangeException(nameof(executionOverride));
            selected = forced;
        }
        var memory = MemoryConfiguration switch
        {
            "classical32k" => Besm6.Core.MemoryConfiguration.Classical32K,
            "simh512k" => Besm6.Core.MemoryConfiguration.Simh512K,
            _ => throw new ArgumentException("memoryConfiguration must be 'classical32k' or 'simh512k'.")
        };
        if (InstructionLimit < 1 || EntryAddress >= PhysicalMemory.WordCount)
            throw new ArgumentException("Instruction limit must be positive and CPU entry address must fit 15 bits.");
        var status = new ControlUnitStatus((ControlUnitFlags)ControlFlags);
        ArgumentNullException.ThrowIfNull(MemoryWords);
        uint capacity = memory == Besm6.Core.MemoryConfiguration.Classical32K ? 32768U : 524288U;
        var addresses = new HashSet<(bool Panel, uint Address)>();
        foreach (var word in MemoryWords)
        {
            if (word is null || word.Address >= capacity || (word.Panel && word.Address is not (>= 1 and <= 7)) ||
                !addresses.Add((word.Panel, word.Address)))
                throw new ArgumentException("Memory image addresses must be unique and within physical storage.");
            _ = new MemoryWord50(word.Raw50);
        }
        var timing = Timing?.Create();
        if (selected == MachineExecutionModel.Hardware && timing is null)
            throw new ArgumentException("Hardware execution requires explicit timing configuration.");
        var attachments = Devices?.Prepare(_assetDirectory, selected);
        var machine = new MachineCore(ProcessorProfile.Supervisor, memory);
        machine.Cpu.Supervisor!.Status = status;
        foreach (var word in MemoryWords)
            if (word.Panel) machine.MappedMemory!.SetPanel(word.Address, new(word.Raw50));
            else machine.MappedMemory!.PhysicalMemory.WriteRaw(word.Address, new(word.Raw50));
        machine.Cpu.StartAt(EntryAddress);
        if (selected == MachineExecutionModel.Hardware) machine.UseHardwareExecution(timing!);
        attachments?.Invoke(machine.SupervisorIo!);
        return machine;
    }
}

public sealed class MachineImageWord
{
    public uint Address { get; set; }
    public ulong Raw50 { get; set; }
    public bool Panel { get; set; }
}

public sealed class MachineTimingConfiguration
{
    public ulong BankCycleNs { get; set; }
    public ulong ReadLatencyNs { get; set; }
    public ulong WriteVisibilityNs { get; set; }
    public string ReadSampling { get; set; } = "cycleStart";
    public string Banking { get; set; } = "documentedClassical";
    public ulong BufferHitNs { get; set; }
    public ulong ArithmeticCycleNs { get; set; }
    public Dictionary<string, ulong> ControlDurationsNs { get; set; } = new();
    public Dictionary<string, ulong> ArithmeticDurationsNs { get; set; } = new();
    public MachineDeviceTimingConfiguration? Devices { get; set; }

    public HardwareMachineConfiguration Create()
    {
        ArgumentNullException.ThrowIfNull(Devices);
        var sampling = ReadSampling switch
        {
            "cycleStart" => MramReadSampling.AtCycleStart, "dataReturn" => MramReadSampling.AtDataReturn,
            _ => throw new ArgumentException("readSampling must be 'cycleStart' or 'dataReturn'.")
        };
        var banking = Banking switch
        {
            "documentedClassical" => MramBanking.DocumentedClassical,
            "logicalLowThreeBits" => MramBanking.LogicalLowThreeBits,
            _ => throw new ArgumentException("Unknown explicit MRAM banking selection.")
        };
        return new(new(new(BankCycleNs), new(ReadLatencyNs), new(WriteVisibilityNs), sampling, MramArbitration.FifoPerBank, banking),
            new(BufferHitNs), new(ArithmeticCycleNs), Durations(ControlDurationsNs), Devices.Create(), Durations(ArithmeticDurationsNs));
    }

    private static Dictionary<Opcode, HardwareDuration> Durations(Dictionary<string, ulong> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new Dictionary<Opcode, HardwareDuration>();
        foreach (var pair in source)
        {
            if (!Enum.TryParse<Opcode>(pair.Key, false, out var opcode) ||
                (Enum.GetName(opcode) != pair.Key && !uint.TryParse(pair.Key, out _)))
                throw new ArgumentException($"Unknown opcode timing key: {pair.Key}.");
            if (!result.TryAdd(opcode, new(pair.Value)))
                throw new ArgumentException("Duplicate opcode timing aliases.");
        }
        return result;
    }
}

public sealed class MachineDeviceTimingConfiguration
{
    public ulong ExchangeReadyNs { get; set; }
    public ulong ConsoleReplyNs { get; set; }
    public ulong PaperFeedNs { get; set; }
    public ulong PrinterCharacterNs { get; set; }
    public ulong PrinterZeroNs { get; set; }
    public ulong TimerPeriodNs { get; set; }
    public ulong SerialPeriodNs { get; set; }
    public ulong DmaIssueIntervalNs { get; set; }
    internal SupervisorIoHardwareTiming Create() => new(new(ExchangeReadyNs), new(ConsoleReplyNs), new(PaperFeedNs),
        new(PrinterCharacterNs), new(PrinterZeroNs), new(TimerPeriodNs), new(SerialPeriodNs), new(DmaIssueIntervalNs));
}
