using Besm6.Architecture.Isa;
using Besm6.AspNetCore.Web.Protocol;
using Besm6.Assembler.Disassembly;
using Besm6.Runtime;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

namespace Besm6.AspNetCore.Web.Worker;

public sealed class WorkerSession : IDisposable
{
    private readonly string _directory;
    private readonly Func<WorkerEvent, Task> _send;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly Dictionary<string, int> _plotterPositions = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private TaskCompletionSource? _pauseReached;
    private MachineCore? _machine;
    private DubnaLoader? _loader;
    private string? _jobPath;
    private bool _debug;
    private long _instructions;
    private int _stepBudget = -1;
    private Action<Opcode>? _stepObserver;
    private int _snapshotRequested;
    private int _memoryAddress = -1;
    private int _memoryCount = 32;
    private long _punchPosition;
    private volatile bool _terminal;

    public WorkerSession(string directory, Func<WorkerEvent, Task> send)
    {
        _directory = Path.GetFullPath(directory);
        _send = send;
    }

    private async Task SendAsync(string type, object? data)
    {
        await _sendLock.WaitAsync();
        try { await _send(new WorkerEvent(type, data)); }
        finally { _sendLock.Release(); }
    }

    public Task SendStateAsync(string status) => SendAsync("state", new StateData(status));

    public async Task SendErrorAsync(Exception ex)
    {
        await SendAsync("error", new ErrorData(ex.Message, ex.ToString()));
        await SendStateAsync("Failed");
    }

    public async Task HandleAsync(WorkerCommand command)
    {
        switch (command.Type)
        {
            case "start": await StartAsync(command); break;
            case "pause":
                if (_terminal || _machine is null) return;
                _pauseReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _resume.Reset();
                if (_runTask is { IsCompleted: false })
                    await _pauseReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (!_terminal)
                {
                    await SendSnapshotAsync();
                    await SendStateAsync("Paused");
                }
                break;
            case "resume":
                EnsureLoaded();
                _stepBudget = -1;
                if (_stepObserver is not null) _machine!.Cpu.InstructionExecuted -= _stepObserver;
                await SendStateAsync("Running");
                _resume.Set();
                StartExecution();
                break;
            case "step":
                EnsureLoaded();
                if (!_debug) throw new InvalidOperationException("Load a debug job first.");
                if (_resume.IsSet && _runTask is { IsCompleted: false })
                    throw new InvalidOperationException("Pause execution before stepping.");
                _stepBudget = 1;
                _machine!.Cpu.InstructionExecuted -= _stepObserver;
                _machine.Cpu.InstructionExecuted += _stepObserver;
                await SendStateAsync("Stepping");
                _resume.Set();
                StartExecution();
                break;
            case "stop":
                _terminal = true;
                if (_runCancellation is not null) _ = _runCancellation.CancelAsync();
                _resume.Set();
                if (_runTask is not null) await _runTask.WaitAsync(TimeSpan.FromSeconds(5));
                await SendSnapshotAsync();
                await SendStateAsync("Halted");
                break;
            case "snapshot":
            case "devices":
                _memoryAddress = command.MemoryAddress;
                _memoryCount = Math.Clamp(command.MemoryCount, 1, 128);
                if (!_terminal && _resume.IsSet && _runTask is { IsCompleted: false })
                    Interlocked.Or(ref _snapshotRequested, command.Type == "snapshot" ? 1 : 2);
                else
                    await SendSnapshotAsync(command.Type == "snapshot");
                break;
            default: throw new InvalidDataException($"Unknown command: {command.Type}");
        }
    }

    private void EnsureLoaded()
    {
        if (_terminal || _loader is null) throw new InvalidOperationException("Load a new job first.");
    }

    private async Task StartAsync(WorkerCommand command)
    {
        if (_runTask is { IsCompleted: false } previous)
        {
            if (!_terminal) throw new InvalidOperationException("A job is already running.");
            await previous.WaitAsync(TimeSpan.FromSeconds(5));
        }
        if (command.JobPath is null || !string.Equals(Path.GetFullPath(command.JobPath),
                Path.Combine(_directory, "job.dub"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Job must be the uploaded session file.");
        if (command.Mode is not ("debug" or "full")) throw new InvalidDataException("Invalid mode.");
        if (!ExecutionSpeedNames.TryParse(command.Speed ?? "max", out var speed))
            throw new InvalidDataException("Invalid speed.");
        if (command.InstructionLimit is < 1 or > 1_000_000_000 || command.WallClockLimitMs is < 0 or > 3_600_000)
            throw new InvalidDataException("Invalid execution limit.");

        _machine?.Puncher.Finish();
        foreach (string name in new[] { "punch.out", "stdarray.out", "cosy.out" })
            File.Delete(Path.Combine(_directory, name));
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        _runTask = null;
        _pauseReached = null;
        _terminal = false;
        _debug = command.Mode == "debug";
        _jobPath = command.JobPath;
        _instructions = 0;
        _stepBudget = -1;
        _snapshotRequested = 0;
        _punchPosition = 0;
        _plotterPositions.Clear();
        _resume.Set();
        await SendStateAsync("Loading");
        try
        {
            var cfg = Config.Load(Path.Combine(AppContext.BaseDirectory, "besm6.json"));
            cfg.Tapes = Path.Combine(AppContext.BaseDirectory, "tapes");
            cfg.Disk = Path.Combine(_directory, "disk.bin");
            cfg.Drum = Path.Combine(_directory, "drum.bin");
            cfg.DefaultLimit = command.InstructionLimit;
            cfg.Speed = speed;
            cfg.GuestClock = command.GuestClock;
            var assets = MachineFactory.ValidateRuntimeAssets(cfg);
            _machine = MachineFactory.CreateMachine(cfg);
            _loader = MachineFactory.CreateLoader(cfg, _machine, assets);
            _loader.HostFileDirectory = _directory;
            _loader.WallClockLimitMs = command.WallClockLimitMs;
            _loader.Output = text => SendAsync("output", new OutputData("print", text)).GetAwaiter().GetResult();
            _loader.TerminalOutput = text => SendAsync("output", new OutputData("terminal", text)).GetAwaiter().GetResult();
            _loader.Input = _ => "";
            var token = _runCancellation.Token;
            _stepObserver = opcode => InstructionBoundary(opcode, token);
            _loader.ExecutionCheckpoint = count =>
            {
                Interlocked.Exchange(ref _instructions, count);
                ExecutionBoundary(token);
            };
            if (_debug)
            {
                _loader.LoadScript(command.JobPath);
                _resume.Reset();
                await SendSnapshotAsync();
                await SendStateAsync("Paused");
            }
            else
            {
                await SendStateAsync("Running");
                StartExecution();
            }
        }
        catch (Exception ex)
        {
            _terminal = true;
            await SendErrorAsync(ex);
        }
    }

    private void StartExecution()
    {
        if (_runTask is not null) return;
        _runTask = Task.Run(async () =>
        {
            try
            {
                var result = _debug ? _loader!.RunLoaded() : _loader!.RunScript(_jobPath!);
                _instructions = result.Instructions;
                await CompleteAsync(result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _terminal = true;
                await SendErrorAsync(ex);
            }
            finally { _pauseReached?.TrySetResult(); }
        });
    }

    private void InstructionBoundary(Opcode opcode, CancellationToken token)
    {
        Interlocked.Exchange(ref _instructions, _loader!.InstructionsExecuted + 1);
        token.ThrowIfCancellationRequested();
        if (_debug && _stepBudget > 0 && Interlocked.Decrement(ref _stepBudget) == 0 && opcode != Opcode.Stop)
        {
            _resume.Reset();
            SendSnapshotAsync().GetAwaiter().GetResult();
            SendStateAsync("Paused").GetAwaiter().GetResult();
        }
        ExecutionBoundary(token);
    }

    private void ExecutionBoundary(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _snapshotRequested) != 0)
        {
            int requested = Interlocked.Exchange(ref _snapshotRequested, 0);
            SendSnapshotAsync((requested & 1) != 0).GetAwaiter().GetResult();
        }
        if (!_resume.IsSet)
        {
            _pauseReached?.TrySetResult();
            _resume.Wait(token);
        }
        token.ThrowIfCancellationRequested();
    }

    private async Task CompleteAsync(LoadResult result)
    {
        if (_terminal) return;
        _terminal = true;
        Interlocked.Exchange(ref _snapshotRequested, 0);
        await SendSnapshotAsync();
        await SendAsync("completed", new CompletionData(result.Success, result.Stopped,
            result.LimitExceeded, result.K, _instructions, result.ErrorMessage
                ?? (result.LimitExceeded ? "Instruction or wall clock limit reached." : null)));
        await SendStateAsync(result.Success ? "Completed" : "Failed");
    }

    private async Task SendSnapshotAsync(bool includeMachineState = true)
    {
        if (_machine is null) return;
        await _snapshotLock.WaitAsync();
        try
        {
            if (includeMachineState)
            {
                var cpu = _machine.Cpu;
                string disassembly = cpu.K < _machine.Memory.Size
                    ? Disassembler.DisasmWord(_machine.Memory.Read(cpu.K).Value) : "";
                await SendAsync("registers", new RegisterData(
                    Convert.ToString(cpu.K, 8), cpu.RightInstruction,
                    cpu.A.Value.ToString("X12"), cpu.Y.Value.ToString("X12"),
                    Convert.ToString(cpu.R, 8), Convert.ToString(cpu.C, 8),
                    cpu.M.ToArray().Select(x => Convert.ToString(x, 8)).ToArray(),
                    Interlocked.Read(ref _instructions), disassembly));
                int address = _memoryAddress < 0 ? Math.Max(0, (int)cpu.K - 8) : _memoryAddress;
                address = Math.Clamp(address, 0, _machine.Memory.Size - 1);
                var words = Enumerable.Range(address, Math.Min(_memoryCount, _machine.Memory.Size - address))
                    .Select(x => new MemoryWord(Convert.ToString(x, 8), _machine.Memory.Read((uint)x).Value.ToString("X12")))
                    .ToArray();
                await SendAsync("memory", new MemoryData(words));
            }
            await SendDeviceOutputAsync();
        }
        finally { _snapshotLock.Release(); }
    }

    private async Task SendDeviceOutputAsync()
    {
        _machine!.Puncher.Flush();
        string path = Path.Combine(_directory, "punch.out");
        if (File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _punchPosition) _punchPosition = 0;
            stream.Position = _punchPosition;
            using var reader = new StreamReader(stream);
            string chunk = await reader.ReadToEndAsync();
            _punchPosition = stream.Position;
            if (chunk.Length > 0) await SendOutputChunksAsync("punch", chunk);
        }
        var plotter = _machine.Plotter;
        foreach (var (channel, length, read) in new (string, int, Func<int, string>)[]
        {
            ("watanabe", plotter.WatanabeLength, plotter.ReadWatanabe),
            ("calcomp", plotter.CalcompLength, plotter.ReadCalcomp),
            ("tektronix", plotter.TektronixLength, plotter.ReadTektronix)
        })
        {
            int position = _plotterPositions.GetValueOrDefault(channel);
            if (position < length) await SendOutputChunksAsync(channel, read(position));
            _plotterPositions[channel] = length;
        }
        await SendAsync("plotter", new PlotterData(plotter.PageNumber, plotter.WatanabeLength, plotter.CalcompLength, plotter.TektronixLength));
        if (_loader is not null)
        {
            var mounts = _loader.StorageMounts.Select(x => new StorageDeviceData(x.Kind, Convert.ToString(x.Unit, 8),
                x.VolumeId.ToString("X12"), x.ReadOnly, x.Words)).ToArray();
            var files = Directory.GetFiles(_directory).OrderBy(x => x)
                .Select(x => new FileInfo(x)).Select(x => new SessionFileData(x.Name, x.Length)).ToArray();
            await SendAsync("storage", new StorageData(mounts, files));
        }
    }

    private async Task SendOutputChunksAsync(string channel, string text)
    {
        const int chunkSize = 16_384;
        for (int i = 0; i < text.Length; i += chunkSize)
            await SendAsync("output", new OutputData(channel, text.Substring(i, Math.Min(chunkSize, text.Length - i))));
    }

    public void Dispose()
    {
        if (_runCancellation is not null) _ = _runCancellation.CancelAsync();
        _resume.Set();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _machine?.Puncher.Finish();
        _runCancellation?.Dispose();
        _resume.Dispose();
        _sendLock.Dispose();
        _snapshotLock.Dispose();
    }
}
