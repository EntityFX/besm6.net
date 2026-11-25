using System.Collections.Concurrent;
using System.Diagnostics;
using Besm6.Architecture.Isa;
using Besm6.Assembler.Disassembly;
using Besm6.Runtime;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

namespace Besm6.Android.Services;

/// <summary>
/// Owns one local BESM-6 session. The execution thread never touches Android UI;
/// callers receive immutable output and register copies and marshal them themselves.
/// </summary>
public sealed class MachineRunService : IDisposable
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);

    private readonly string _tapesDirectory;
    private readonly string _workDirectory;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _pendingInput = new();
    private readonly AutoResetEvent _inputAvailable = new(false);
    private readonly ManualResetEventSlim _resumeGate = new(true);
    private readonly Dictionary<string, int> _plotterOffsets = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cancellation;
    private CancellationToken _token;
    private Task? _runTask;
    private MachineCore? _machine;
    private DubnaLoader? _loader;
    private string? _jobPath;
    private RunSettings? _settings;
    private long _instructions;
    private long _lastSnapshotTimestamp;
    private int _memoryAddress = -1;
    private int _memoryCount = 32;
    private volatile bool _waitingForInput;
    private volatile bool _debugMode;
    private volatile bool _pauseRequested;
    private int _stepBudget = -1;
    private bool _disposed;

    public MachineRunService(string tapesDirectory, string workDirectory)
    {
        _tapesDirectory = tapesDirectory;
        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);
        // DiskDevice/MagneticDrumDevice in Runtime use relative file names. Android's
        // default working directory is read-only, so keep them in app-private storage.
        try { Directory.SetCurrentDirectory(_workDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller still receives the real device error from the run.
        }
    }

    public event Action<string, string>? OutputAppended;
    public event Action<RunPhase>? PhaseChanged;
    public event Action<RegisterSnapshot>? RegistersChanged;
    public event Action<MemorySnapshot>? MemoryChanged;
    public event Action<string>? InputRequested;
    public event Action<RunOutcome>? Completed;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _runTask is { IsCompleted: false };
        }
    }

    public bool IsWaitingForInput => _waitingForInput;

    /// <summary>Starts a fresh machine. A second concurrent run is rejected.</summary>
    public Task Start(string jobPath, RunSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobPath);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out string? error)) throw new ArgumentException(error, nameof(settings));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is { IsCompleted: false })
                throw new InvalidOperationException("Машина уже выполняет задание.");

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _token = _cancellation.Token;
            _jobPath = jobPath;
            _settings = settings;
            _instructions = 0;
            _lastSnapshotTimestamp = 0;
            _debugMode = settings.DebugMode;
            _pauseRequested = false;
            _stepBudget = -1;
            _resumeGate.Set();
            _plotterOffsets.Clear();
            while (_pendingInput.TryDequeue(out _)) { }
            _runTask = Task.Factory.StartNew(
                () => RunCore(_token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return _runTask;
        }
    }

    public bool SubmitInput(string text)
    {
        if (!_waitingForInput || _token.IsCancellationRequested) return false;
        _pendingInput.Enqueue(text);
        _inputAvailable.Set();
        return true;
    }

    public void Pause()
    {
        if (!_debugMode || !IsRunning) return;
        _pauseRequested = true;
    }

    public void Step()
    {
        if (!_debugMode) return;
        _pauseRequested = false;
        Interlocked.Exchange(ref _stepBudget, 1);
        PhaseChanged?.Invoke(RunPhase.Running);
        _resumeGate.Set();
    }

    public void Resume()
    {
        if (!_debugMode) return;
        _pauseRequested = false;
        Interlocked.Exchange(ref _stepBudget, -1);
        PhaseChanged?.Invoke(RunPhase.Running);
        _resumeGate.Set();
    }

    public void RequestMemorySnapshot(int address, int count)
    {
        _memoryAddress = address;
        _memoryCount = Math.Clamp(count, 1, 128);
        if (!IsRunning) PublishMemory();
    }

    public void RequestStop()
    {
        CancellationTokenSource? cancellation;
        lock (_gate) cancellation = _cancellation;
        if (cancellation is null || cancellation.IsCancellationRequested) return;
        PhaseChanged?.Invoke(RunPhase.Stopping);
        cancellation.Cancel();
        _inputAvailable.Set();
        _resumeGate.Set();
    }

    private void RunCore(CancellationToken token)
    {
        try
        {
            RunSettings settings = _settings!;
            string jobPath = _jobPath!;
            PhaseChanged?.Invoke(RunPhase.Preparing);

            LoadResult result = settings.Model == MachineModel.SupervisorHardware
                ? RunSupervisorHardware(jobPath, settings)
                : RunDubna(jobPath, settings);

            _instructions = result.Instructions;
            PublishSnapshot(result.Instructions);
            FlushPlotter();
            Completed?.Invoke(new RunOutcome(result.Success, false, result.LimitExceeded,
                result.Instructions, result.K, result.ErrorMessage));
            PhaseChanged?.Invoke(result.Success ? RunPhase.Completed : RunPhase.Failed);
        }
        catch (OperationCanceledException)
        {
            FinalizeCancelled();
        }
        catch (Exception ex)
        {
            _instructions = _loader?.InstructionsExecuted ?? _instructions;
            PublishSnapshot(_instructions);
            Completed?.Invoke(new RunOutcome(false, false, false, _instructions,
                _machine?.Cpu.K ?? 0, ex.Message));
            PhaseChanged?.Invoke(RunPhase.Failed);
        }
        finally
        {
            try { _machine?.Puncher.Finish(); } catch { /* device shutdown must not mask the result */ }
        }
    }

    private LoadResult RunDubna(string jobPath, RunSettings settings)
    {
        var config = new Config
        {
            Tapes = _tapesDirectory,
            Speed = settings.Speed,
            DefaultLimit = settings.InstructionLimit,
            GuestClock = settings.GuestClock,
            BufferedMemory = settings.Model == MachineModel.DubnaBuffered,
            SupervisorHostedServices = settings.Model == MachineModel.SupervisorFunctional,
            UseWallClock = true,
        };

        var session = MachineFactory.CreateRunSession(config, new RunSessionSettings(
            settings.Speed,
            settings.InstructionLimit,
            GuestClock: settings.GuestClock,
            BufferedMemory: settings.Model == MachineModel.DubnaBuffered,
            SupervisorHostedServices: settings.Model == MachineModel.SupervisorFunctional,
            WallClockLimitMs: settings.WallClockLimitMs,
            CollectStatistics: settings.CollectStatistics,
            TapesDirectory: _tapesDirectory,
            DiskPath: Path.Combine(_workDirectory, "disk.bin"),
            DrumPath: Path.Combine(_workDirectory, "drum.bin"),
            HostFileDirectory: _workDirectory));

        _machine = session.Machine;
        _loader = session.Loader;
        AttachMachine(_machine, _loader);

        if (settings.DebugMode)
        {
            _loader.LoadScript(jobPath);
            PauseAtBoundary();
            return _loader.RunLoaded();
        }

        PhaseChanged?.Invoke(RunPhase.Running);
        return _loader.RunScript(jobPath);
    }

    private LoadResult RunSupervisorHardware(string jobPath, RunSettings settings)
    {
        var defaults = HardwareSupervisorDefaults.Create(settings.Speed, settings.InstructionLimit);
        var machine = defaults.CreateMachine(MachineExecutionModel.Hardware);
        var config = new Config
        {
            Tapes = _tapesDirectory,
            Speed = settings.Speed,
            DefaultLimit = settings.InstructionLimit,
            GuestClock = settings.GuestClock,
            UseWallClock = true,
        };
        var assets = MachineFactory.ValidateRuntimeAssets(config);
        var loader = MachineFactory.CreateLoader(config, machine, assets);
        loader.InstructionLimit = settings.InstructionLimit;
        loader.CollectStatistics = settings.CollectStatistics;
        loader.WallClockLimitMs = settings.WallClockLimitMs;

        _machine = machine;
        _loader = loader;
        AttachMachine(machine, loader);
        loader.LoadScript(jobPath);
        PhaseChanged?.Invoke(RunPhase.Running);
        var execution = machine.RunInstructions(settings.InstructionLimit, settings.Speed,
            settings.CollectStatistics, Checkpoint, settings.WallClockLimitMs);
        return execution.Outcome;
    }

    private void AttachMachine(MachineCore machine, DubnaLoader loader)
    {
        loader.Output = text => OutputAppended?.Invoke(DeviceChannels.Printer, text);
        loader.TerminalOutput = text => OutputAppended?.Invoke(DeviceChannels.Terminal, text);
        loader.Input = ReadInput;
        loader.ExecutionCheckpoint = Checkpoint;

        if (machine.Devices.GetDevice(0x4000) is TeletypeDevice teletype)
            teletype.Output = text => OutputAppended?.Invoke(DeviceChannels.Terminal, text);

        machine.Puncher.CardPunched += card =>
            OutputAppended?.Invoke(DeviceChannels.Punch, PunchCardRenderer.RenderBraille(card));

        if (_debugMode)
        {
            if (machine.UsesHostedSupervisorServices) loader.InstructionExecuted = OnInstruction;
            else machine.Cpu.InstructionExecuted = OnInstruction;
        }
    }

    private void OnInstruction(Opcode opcode)
    {
        if (!_debugMode || opcode == Opcode.Stop) return;
        bool pause = _pauseRequested;
        if (!pause)
        {
            int budget = Volatile.Read(ref _stepBudget);
            if (budget > 0 && Interlocked.Decrement(ref _stepBudget) == 0) pause = true;
        }
        if (pause) PauseAtBoundary();
    }

    private void PauseAtBoundary()
    {
        _pauseRequested = false;
        _resumeGate.Reset();
        PublishSnapshot(_instructions);
        PhaseChanged?.Invoke(RunPhase.Paused);
        _resumeGate.Wait(_token);
        _token.ThrowIfCancellationRequested();
    }

    private void Checkpoint(long completedInstructions)
    {
        Interlocked.Exchange(ref _instructions, completedInstructions);
        _token.ThrowIfCancellationRequested();

        long now = Stopwatch.GetTimestamp();
        if (_lastSnapshotTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastSnapshotTimestamp, now) < SnapshotInterval) return;
        _lastSnapshotTimestamp = now;
        PublishSnapshot(completedInstructions);
        FlushPlotter();
    }

    private string ReadInput(string prompt)
    {
        _waitingForInput = true;
        try
        {
            InputRequested?.Invoke(prompt);
            PhaseChanged?.Invoke(RunPhase.WaitingInput);
            while (true)
            {
                _token.ThrowIfCancellationRequested();
                if (_pendingInput.TryDequeue(out string? line)) return line;
                _inputAvailable.WaitOne(50);
            }
        }
        finally
        {
            _waitingForInput = false;
            if (!_token.IsCancellationRequested) PhaseChanged?.Invoke(RunPhase.Running);
        }
    }

    private void FinalizeCancelled()
    {
        PhaseChanged?.Invoke(RunPhase.Stopping);
        _instructions = _loader?.InstructionsExecuted ?? _instructions;
        PublishSnapshot(_instructions);
        FlushPlotter();
        Completed?.Invoke(new RunOutcome(false, true, false, _instructions, _machine?.Cpu.K ?? 0, null));
        PhaseChanged?.Invoke(RunPhase.Completed);
    }

    private void PublishSnapshot(long instructions)
    {
        if (_machine is null) return;
        try
        {
            var detail = _machine.InspectRegisters();
            var cpu = _machine.Cpu;
            var snapshot = new RegisterSnapshot(
                detail.MemoryModel,
                detail.ExecutionModel,
                detail.ProcessorProfile,
                cpu.K,
                cpu.RightInstruction,
                cpu.A.Value,
                cpu.Y.Value,
                cpu.R,
                cpu.C,
                cpu.ApplyC,
                cpu.M.ToArray(),
                instructions,
                detail.Buffers,
                detail.Registers);
            RegistersChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            // A diagnostic snapshot must not turn into a guest failure.
            OutputAppended?.Invoke(DeviceChannels.Terminal,
                Environment.NewLine + "[снимок регистров недоступен: " + ex.Message + "]" + Environment.NewLine);
        }

        if (_memoryAddress >= 0) PublishMemory();
    }

    private void PublishMemory()
    {
        if (_machine is null || _memoryAddress < 0) return;
        try
        {
            int size = _machine.Memory.Size;
            if (size <= 0) return;
            int address = Math.Clamp(_memoryAddress, 0, size - 1);
            int count = Math.Min(_memoryCount, size - address);
            var words = new MemoryWordView[count];
            for (int i = 0; i < count; i++)
            {
                ulong value = _machine.Memory.Read((uint)(address + i)).Value;
                words[i] = new MemoryWordView(address + i, value, Disassembler.DisasmWord(value));
            }
            MemoryChanged?.Invoke(new MemorySnapshot(address, words));
        }
        catch
        {
            // Memory inspection is diagnostic and must not stop the guest.
        }
    }

    private void FlushPlotter()
    {
        if (_machine is null) return;
        var plotter = _machine.Plotter;
        AppendPlotterDelta(DeviceChannels.Watanabe, plotter.WatanabeLength, plotter.ReadWatanabe);
        AppendPlotterDelta(DeviceChannels.Calcomp, plotter.CalcompLength, plotter.ReadCalcomp);
        AppendPlotterDelta(DeviceChannels.Tektronix, plotter.TektronixLength, plotter.ReadTektronix);
    }

    private void AppendPlotterDelta(string channel, int length, Func<int, string> read)
    {
        int previous = _plotterOffsets.GetValueOrDefault(channel);
        if (length <= previous) return;
        string text = read(previous);
        _plotterOffsets[channel] = length;
        if (text.Length > 0) OutputAppended?.Invoke(channel, text);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation?.Cancel();
        }
        _inputAvailable.Set();
        _resumeGate.Set();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* dispose is best effort */ }
        try { _machine?.Puncher.Finish(); } catch { /* device shutdown */ }
        _cancellation?.Dispose();
        _inputAvailable.Dispose();
        _resumeGate.Dispose();
    }
}

public static class DeviceChannels
{
    public const string Printer = "print";
    public const string Terminal = "terminal";
    public const string Punch = "punch";
    public const string Watanabe = "watanabe";
    public const string Calcomp = "calcomp";
    public const string Tektronix = "tektronix";
}
