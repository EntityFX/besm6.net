using Besm6.AspNetCore.Web.Protocol;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

namespace Besm6.AspNetCore.Web.Worker;

public sealed partial class WorkerSession
{
    private bool _standalone;
    private WorkerCommand? _machineCommand;
    private ExecutionStatistics? _standaloneStatistics;
    private bool _decodeMemory = true;

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
