using Besm6.AspNetCore.Web.Protocol;
using Besm6.Runtime;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

namespace Besm6.AspNetCore.Web.Worker;

public sealed partial class WorkerSession
{
    private bool _standalone;
    private bool _hardwareDub;
    private WorkerCommand? _machineCommand;
    private ExecutionStatistics? _standaloneStatistics;
    private bool _decodeMemory = true;

    private (MachineCore Machine, DubnaLoader Loader) CreateHardwareSupervisorSession(
        WorkerCommand command, ExecutionSpeed speed)
    {
        var defaults = HardwareSupervisorDefaults.Create(speed, command.InstructionLimit);
        var machine = defaults.CreateMachine(MachineExecutionModel.Hardware);
        var config = Config.Load(Path.Combine(AppContext.BaseDirectory, "besm6.json"));
        config.Speed = speed;
        config.DefaultLimit = command.InstructionLimit;
        config.GuestClock = command.GuestClock;
        config.Tapes = Path.Combine(AppContext.BaseDirectory, "tapes");
        config.Disk = Path.Combine(_directory, "disk.bin");
        config.Drum = Path.Combine(_directory, "drum.bin");
        var assets = MachineFactory.ValidateRuntimeAssets(config);
        var loader = MachineFactory.CreateLoader(config, machine, assets);
        loader.InstructionLimit = command.InstructionLimit;
        loader.CollectStatistics = command.Statistics;
        loader.WallClockLimitMs = command.WallClockLimitMs;
        loader.HostFileDirectory = _directory;
        return (machine, loader);
    }

    private async Task StartStandaloneAsync(WorkerCommand command)
    {
        string source = await File.ReadAllTextAsync(command.JobPath!);
        if (!source.TrimStart().StartsWith('{'))
            throw new InvalidDataException("Supervisor requires a machine .json configuration, not a .dub job. Select Dubna for .dub, or use Editor > Machine JSON template.");
        var configuration = MachineJobConfiguration.Load(command.JobPath!);
        foreach (string file in (configuration.Devices?.DiskImages.Select(image => image.File) ?? [])
            .Concat(configuration.Devices?.PaperTapes.Select(tape => tape.File) ?? []))
        {
            if (file != Path.GetFileName(file) || !file.StartsWith("media-", StringComparison.Ordinal) || !file.EndsWith(".bin", StringComparison.Ordinal)
                || !Guid.TryParseExact(file[6..^4], "N", out _))
                throw new InvalidDataException("Machine attachments must use uploaded session media-<id>.bin files.");
        }
        _standalone = true; _machineCommand = command; _standaloneStatistics = null; _loader = null;
        _machine = configuration.CreateMachine(command.MachineModel == "supervisor-hardware" ? MachineExecutionModel.Hardware : MachineExecutionModel.Functional);
        AttachDeviceSinks();
        _machine.SupervisorIo!.ConsoleOutput += (_, value) => _output.Append("terminal", ((char)value).ToString());
        _machine.SupervisorIo.SerialOutput += (_, value) => _output.Append("terminal", ((char)value).ToString());
        if (_debug) { _resume.Reset(); await SendSnapshotAsync(); await SendStateAsync("Paused"); }
        else { await SendStateAsync("Running"); StartExecution(); }
    }

    private LoadResult RunHardwareLoaded()
    {
        var token = _runCancellation!.Token;
        var result = _machine!.RunInstructions(_loader!.InstructionLimit, _loader.Speed,
            _loader.CollectStatistics, count =>
            {
                Interlocked.Exchange(ref _instructions, count);
                ExecutionBoundary(token);
            }, _loader.WallClockLimitMs);
        _standaloneStatistics = result.Statistics;
        return result.Outcome;
    }

    private async Task StepHardwareAsync()
    {
        _stepBudget = 1;
        bool stopped = _machine!.Step();
        Interlocked.Exchange(ref _instructions, _instructions + 1);
        await SendSnapshotAsync();
        if (stopped) { _terminal = true; await SendStateAsync("Halted"); }
        else await SendStateAsync("Paused");
    }

    private LoadResult RunStandalone()
    {
        var command = _machineCommand!; var token = _runCancellation!.Token; long previous = 0;
        ExecutionSpeedNames.TryParse(command.Speed ?? "max", out var speed);
        var result = _machine!.RunInstructions(command.InstructionLimit, speed, command.Statistics, count =>
        {
            Interlocked.Exchange(ref _instructions, count);
            if (_debug && count > previous && _stepBudget > 0 && Interlocked.Decrement(ref _stepBudget) == 0)
            {
                _resume.Reset(); SendSnapshotAsync().GetAwaiter().GetResult(); SendStateAsync("Paused").GetAwaiter().GetResult();
            }
            previous = count; ExecutionBoundary(token);
        }, command.WallClockLimitMs);
        _standaloneStatistics = result.Statistics;
        return result.Outcome;
    }
}
