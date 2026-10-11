using System;
using System.IO;
using System.Diagnostics;

namespace Besm6.Runtime
{
    /// <summary>
    /// Единый bounded execution loop: instruction/wall-clock/hang/loop limits,
    /// arithmetic intercept, формирование LoadResult.
    /// </summary>
    internal sealed class ExecutionLoop
    {
        private readonly MachineCore _machine;
        private readonly ExtracodeHandler? _extracode;
        private readonly Action<string>? _verboseLog;
        private readonly Action<string>? _output;
        private readonly Action<string>? _progressOutput;

        public long InstructionLimit { get; set; } = 1_000_000_000L;
        /// <summary>Optional host safety bound, independent of successfully completed guest commands.</summary>
        public long? StepAttemptLimit { get; set; }
        public long WallClockLimitMs { get; set; } = 0;
        public bool LoopDetect { get; set; } = false;
        private long _instructionsExecuted;
        public long InstructionsExecuted => _instructionsExecuted;
        public bool HaltedByStop { get; private set; }
        private ExecutionSpeed _speed;
        public ExecutionSpeed Speed
        {
            get => _speed;
            set
            {
                if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
                _speed = value;
            }
        }
        public bool CollectStatistics { get; set; }
        public bool GuestClockEnabled { get; set; }
        public bool ProgressEnabled { get; set; }
        public ExecutionStatistics? Statistics { get; private set; }
        public Action<Opcode>? InstructionExecuted { get; set; }
        private ExecutionAccountingSession? _accounting;
        private long TotalCycles => _accounting?.TotalCycles ?? 0;

        public Action<int, ulong>? InstructionTrace { get; set; }
        public Action<uint, bool, uint, uint>? CppInstructionTrace { get; set; }
        public Action<string, ulong>? RegisterTrace { get; set; }

        /// <summary>
        /// Полная типизированная трассировка исполненных инструкций для диагностики.
        /// Подписывается на хук процессора со снимками регистров.
        /// </summary>
        public Action<InstructionTraceRecord>? TypedInstructionTrace { get; set; }

        public ExecutionLoop(MachineCore machine, ExtracodeHandler extracode,
            Action<string>? verboseLog, Action<string>? output, Action<string>? progressOutput)
        {
            _machine = machine;
            _extracode = extracode;
            _verboseLog = verboseLog;
            _output = output;
            _progressOutput = progressOutput;
            _extracode.GuestTimeSeconds = ReadGuestTimeSeconds;
        }

        // STIME/CTIME (счетное время): the documented resolution is 0.02 s.
        // Hosted Dubna has one logical CPU clock; this does not model device overlap.
        private double ReadGuestTimeSeconds() => GuestClockEnabled ? (TotalCycles / 200_000) * 0.02 : 0;

        internal ExecutionLoop(MachineCore machine) => _machine = machine;

        public void InstallHook() => _machine.Cpu.ExtracodeDispatch =
            (_extracode ?? throw new InvalidOperationException("No hosted extracode handler.")).Handle;

        public LoadResult Run()
        {
            _machine.EnsureExecutionAllowed();
            if (StepAttemptLimit is < 1) throw new ArgumentOutOfRangeException(nameof(StepAttemptLimit));
            Statistics = null;
            bool countCycles = Speed == ExecutionSpeed.Original || CollectStatistics || GuestClockEnabled;
            _accounting = countCycles ? new ExecutionAccountingSession() : null;
            Action<Opcode>? counter = _accounting is { } accounting ? accounting.Observe : null;
            Action<Opcode>? observer = InstructionExecuted;
            var diagnostics = new ExecutionDiagnosticsSession(_machine, this);
            var stopwatch = countCycles || WallClockLimitMs > 0 ? new Stopwatch() : null;
            long allocatedBefore = CollectStatistics ? GC.GetAllocatedBytesForCurrentThread() : 0;
            int gen0Before = CollectStatistics ? GC.CollectionCount(0) : 0;
            bool previousInstructionCache = _machine.Cpu.InstructionCacheEnabled;
            try
            {
                _machine.Cpu.InstructionCacheEnabled = Speed == ExecutionSpeed.Max;
                diagnostics.AttachFileTraceWriters();
                diagnostics.AttachInstructionObservers(counter, observer);
                return RunCore(stopwatch, diagnostics);
            }
            finally
            {
                stopwatch?.Stop();
                if (countCycles)
                    Statistics = new ExecutionStatistics(Speed, _accounting!.TotalInstructions, TotalCycles,
                        stopwatch!.Elapsed.TotalSeconds,
                        CollectStatistics ? GC.GetAllocatedBytesForCurrentThread() - allocatedBefore : 0,
                        CollectStatistics ? GC.CollectionCount(0) - gen0Before : 0);
                diagnostics.DetachExecutionObservers();
                _machine.Cpu.InstructionCacheEnabled = previousInstructionCache;
                try { _extracode?.FinishOutput(); }
                finally { diagnostics.Dispose(); }
            }
        }

        private LoadResult RunCore(Stopwatch? wallStopwatch, ExecutionDiagnosticsSession diagnostics)
        {
            long limit = InstructionLimit;
            long wallLimitMs = WallClockLimitMs;
            var pacer = Speed == ExecutionSpeed.Original
                ? new ExecutionPacer(new StopwatchExecutionTimeSource(wallStopwatch!)) : null;
            double? wallLimitSeconds = wallLimitMs > 0 ? wallLimitMs / 1000.0 : null;
            long nextCheckpoint = ExecutionPacer.CheckpointCycles;
            _instructionsExecuted = 0;
            HaltedByStop = false;
            long lastReport = 0;
            var progressOutput = ProgressEnabled ? _progressOutput : null;
            long? attemptLimit = StepAttemptLimit;
            long stepAttempts = 0;
            bool executeBlocks = Speed == ExecutionSpeed.Max && !LoopDetect && progressOutput is null && attemptLimit is null;

            const int LoopWindow = 20_000;
            const int LoopRange = 16;
            long[]? kHistory = LoopDetect ? new long[LoopWindow] : null;
            int kHistIdx = 0;

            diagnostics.AttachRuntimeObservers();

            wallStopwatch?.Start();
            while (InstructionsExecuted < limit && (attemptLimit is null || stepAttempts < attemptLimit.Value))
            {
                try
                {
                    bool stopped;
                    if (executeBlocks)
                    {
                        int count = (int)Math.Min(256, limit - InstructionsExecuted);
                        // Keep precisely the old wall-clock checkpoints, even after
                        // an exception interrupted the preceding block.
                        if (wallLimitMs > 0)
                            count = Math.Min(count, 4096 - (int)(InstructionsExecuted & 4095));
                        stopped = _machine.ExecuteBlock(count, ref _instructionsExecuted);
                    }
                    else
                    {
                        stepAttempts++;
                        stopped = _machine.Step(ref _instructionsExecuted);
                    }
                    if (pacer is not null && (stopped || TotalCycles >= nextCheckpoint))
                    {
                        if (!pacer.Synchronize(TotalCycles, wallLimitSeconds))
                            return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                        nextCheckpoint = TotalCycles + ExecutionPacer.CheckpointCycles;
                    }
                    if (stopped)
                    {
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    if (wallLimitMs > 0 && (InstructionsExecuted & 4095) == 0
                        && wallStopwatch!.ElapsedMilliseconds > wallLimitMs)
                    {
                        if (_verboseLog != null) _verboseLog("");
                        return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    long currentK = kHistory is not null || progressOutput is not null ? _machine.Cpu.GetK() : 0;
                    if (kHistory is not null)
                    {
                        kHistory[kHistIdx] = currentK;
                        kHistIdx = (kHistIdx + 1) % LoopWindow;
                    }

                    if (LoopDetect && InstructionsExecuted >= LoopWindow && (InstructionsExecuted % LoopWindow) == 0)
                    {
                        long minK = long.MaxValue, maxK = long.MinValue;
                        for (int i = 0; i < LoopWindow; i++)
                        {
                            long v = kHistory![i];
                            if (v < minK) minK = v;
                            if (v > maxK) maxK = v;
                        }
                        if ((maxK - minK) < LoopRange)
                        {
                            string diag = $"Loop detected: K stuck in range 0{minK:X4}-0{maxK:X4} " +
                                         $"for {LoopWindow / 1000}K+ instructions. " +
                                         "MONSYS is in an I/O wait/abort spin-loop (channel-done not signaled). " +
                                         "This is a known MONSYS kernel gap (same in C++ dubna reference). " +
                                         "See ARCHITECTURE.md for the execution limits and compatibility boundaries.";
                            if (_verboseLog != null) _verboseLog($"\n  [LOOP] {diag}");
                            return LoadResult.Failed(diag, currentK, InstructionsExecuted);
                        }
                    }

                    if (progressOutput != null && InstructionsExecuted - lastReport >= 100_000)
                    {
                        lastReport = InstructionsExecuted;
                        progressOutput($"\r  [{InstructionsExecuted / 1000}K] K=0{currentK:X4}   ");
                    }
                }
                catch (ProcessorException ex)
                {
                    if (_machine.Cpu.Profile == ProcessorProfile.Supervisor) throw;
                    _machine.Cpu.StackCorrection();
                    _extracode?.FinishOutput();

                    if (string.IsNullOrEmpty(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                        if (pacer is not null && !pacer.Synchronize(TotalCycles, wallLimitSeconds))
                            return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    if (_machine.Cpu.Intercept(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                        if (pacer is not null && TotalCycles >= nextCheckpoint)
                        {
                            if (!pacer.Synchronize(TotalCycles, wallLimitSeconds))
                                return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                            nextCheckpoint = TotalCycles + ExecutionPacer.CheckpointCycles;
                        }
                        if (_verboseLog != null)
                            _verboseLog($"\r  [INTERCEPT @ 0{_machine.Cpu.GetK():X4}] {ex.Message} → 0{_machine.Cpu.GetK():X4}\n");
                        continue;
                    }

                    _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                    if (_verboseLog != null) _verboseLog("");
                    return LoadResult.Failed(ex.Message, _machine.Cpu.GetK(), InstructionsExecuted);
                }
            }
            if (_verboseLog != null) _verboseLog("");
            return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
        }
    }
}
