using System.Diagnostics;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Runtime;

public partial class MachineCore
{
    public MachineExecutionModel ExecutionModel { get; private set; }
    private HardwareMachineConfiguration? _hardwareConfiguration;
    private bool _publicHardwareRunning;

    private void EnsureOwnerSwitchAllowed()
    {
        if (_publicHardwareRunning || Cpu.ExecutionProhibited || Cpu.HasPreparedInstruction ||
            ExistingHardwareModel is { IsDriving: true } || HardwareEventsPending ||
            _simulation.Scheduler.NextEventTick.HasValue)
            throw new InvalidOperationException("Select the execution owner at an idle CPU and calendar boundary.");
    }

    private bool HardwareEventsPending => ExistingHardwareModel?.Timeline.NextEventTime.HasValue == true;

    /// <summary>Select hardware execution of the shared supervisor CPU. Configuration is fixed
    /// for this machine; switching owners preserves registers, memory and both clock values.</summary>
    public void UseHardwareExecution(HardwareMachineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureOwnerSwitchAllowed();
        if (Cpu.Profile != ProcessorProfile.Supervisor || MappedMemory is null || SupervisorIo is null)
            throw new InvalidOperationException("Hardware execution requires the supervisor machine profile.");
        if (_hardwareConfiguration is not null && !ReferenceEquals(_hardwareConfiguration, configuration))
            throw new InvalidOperationException("Reuse this machine's selected hardware configuration.");
        SupervisorIo.EnsureCalendarSwitchAllowed();
        // Validate storage bounds/timing without changing the live machine or allocating events.
        _ = new MramMemoryPort(MappedMemory.PhysicalMemory, new HardwareTimeline(), configuration.Memory);
        HardwareModel.ConfigureAutomaticExecution(configuration.Execution);
        SupervisorIo.UseHardwareTiming(HardwareTimeline, HardwareModel.CreateMemoryPort(configuration.Memory), configuration.Devices);
        if (_hardwareConfiguration is null)
            HardwareModel.InstructionCompleted += outcome =>
            {
                if (outcome.Status == HardwareInstructionStatus.Completed) _simulation.EmitCompletedDiagnostics();
            };
        _hardwareConfiguration = configuration;
        Cpu.PreparedExecutionOnly = true;
        ExecutionModel = MachineExecutionModel.Hardware;
    }

    public void UseFunctionalExecution()
    {
        EnsureOwnerSwitchAllowed();
        SupervisorIo?.UseFunctionalTiming();
        Cpu.PreparedExecutionOnly = false;
        ExecutionModel = MachineExecutionModel.Functional;
    }

    private HardwareRunOutcome HardwareStep()
    {
        if (Cpu.ExecutionProhibited || _publicHardwareRunning)
            throw new InvalidOperationException("Nested machine execution is not allowed.");
        return HardwareModel.AdvanceAutomaticTo(new(ulong.MaxValue), 1);
    }

    internal MachineExecutionResult RunHardwareInstructions(long instructionLimit, ExecutionSpeed speed,
        bool collectStatistics, IExecutionTimeSource? suppliedTime = null)
    {
        if (_publicHardwareRunning || Cpu.ExecutionProhibited)
            throw new InvalidOperationException("Nested machine execution is not allowed.");
        if (instructionLimit < 1) throw new ArgumentOutOfRangeException(nameof(instructionLimit));
        if (ExecutionModel != MachineExecutionModel.Hardware) throw new InvalidOperationException("Select hardware execution first.");
        if (!Enum.IsDefined(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        var model = HardwareModel; ulong started = model.Timeline.Now.Nanoseconds;
        bool timed = collectStatistics || speed == ExecutionSpeed.Original;
        var watch = timed && suppliedTime is null ? Stopwatch.StartNew() : null;
        IExecutionTimeSource? time = timed && suppliedTime is not null ?
            new RelativeExecutionTimeSource(suppliedTime, suppliedTime.ElapsedSeconds) :
            watch is null ? null : new StopwatchExecutionTimeSource(watch);
        var pacer = speed == ExecutionSpeed.Original ? new ExecutionPacer(time!) : null;
        long allocated = collectStatistics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        int gen0 = collectStatistics ? GC.CollectionCount(0) : 0;
        ulong checkpoint = 10_000_000, completed = 0;
        LoadResult outcome = LoadResult.StoppedByLimit(Cpu.GetK(), 0);
        _publicHardwareRunning = true;
        try
        {
            // Bound failed attempts independently, just as the functional supervisor runner does.
            for (long attempt = 0; attempt < instructionLimit; attempt++)
            {
                var step = model.AdvanceAutomaticTo(new(ulong.MaxValue), 1);
                completed += step.Completed;
                ulong elapsed = model.Timeline.Now.Nanoseconds - started;
                if (pacer is not null && elapsed >= checkpoint)
                {
                    pacer.SynchronizeSeconds(elapsed / 1_000_000_000.0);
                    checkpoint = elapsed > ulong.MaxValue - 10_000_000 ? ulong.MaxValue : elapsed + 10_000_000;
                }
                if (step.Boundary == HardwareRunBoundary.Stop)
                { outcome = LoadResult.Halt(Cpu.GetK(), checked((long)completed)); break; }
                if (step.Boundary is HardwareRunBoundary.Cancelled or HardwareRunBoundary.WaitingForSignal or HardwareRunBoundary.TimeLimit)
                { outcome = LoadResult.Failed($"Hardware execution boundary: {step.Boundary}.", Cpu.GetK(), checked((long)completed)); break; }
                outcome = LoadResult.StoppedByLimit(Cpu.GetK(), checked((long)completed));
            }
            pacer?.SynchronizeSeconds((model.Timeline.Now.Nanoseconds - started) / 1_000_000_000.0);
            var statistics = timed ? new ExecutionStatistics(speed, checked((long)completed), null,
                time!.ElapsedSeconds, collectStatistics ? GC.GetAllocatedBytesForCurrentThread() - allocated : 0,
                collectStatistics ? GC.CollectionCount(0) - gen0 : 0,
                (model.Timeline.Now.Nanoseconds - started) / 1_000_000_000.0) : null;
            return new(outcome, statistics);
        }
        finally { watch?.Stop(); _publicHardwareRunning = false; }
    }
}
