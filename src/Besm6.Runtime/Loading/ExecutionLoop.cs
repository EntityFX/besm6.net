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
        private readonly ExtracodeHandler _extracode;
        private readonly Action<string>? _verboseLog;
        private readonly Action<string>? _output;
        private readonly Action<string>? _progressOutput;

        private CanonicalTraceWriter? _canonicalTraceWriter;
        private DiagnosticTraceWriter? _diagnosticTraceWriter;

        public long InstructionLimit { get; set; } = 1_000_000_000L;
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
        public bool ProgressEnabled { get; set; }
        public ExecutionStatistics? Statistics { get; private set; }
        public Action<Opcode>? InstructionExecuted { get; set; }
        private long _completedInstructions;
        private long _totalCycles;

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
        }

        public void InstallHook() => _machine.Cpu.ExtracodeDispatch = _extracode.Handle;

        public LoadResult Run()
        {
            Statistics = null;
            _completedInstructions = 0;
            _totalCycles = 0;
            bool countCycles = Speed == ExecutionSpeed.Original || CollectStatistics;
            Action<Opcode>? counter = countCycles ? ObserveExecuted : null;
            Action<Opcode>? observer = InstructionExecuted;
            var stopwatch = countCycles || WallClockLimitMs > 0 ? new Stopwatch() : null;
            long allocatedBefore = CollectStatistics ? GC.GetAllocatedBytesForCurrentThread() : 0;
            int gen0Before = CollectStatistics ? GC.CollectionCount(0) : 0;
            var previousStepTrace = _machine.StepTrace;
            var previousCpuTrace = _machine.Cpu.TraceInstruction;
            var previousRegisterTrace = _machine.RegisterTrace;
            bool previousInstructionCache = _machine.Cpu.InstructionCacheEnabled;
            try
            {
                _machine.Cpu.InstructionCacheEnabled = Speed == ExecutionSpeed.Max;
                AttachFileTraceWriters();
                _machine.Cpu.InstructionExecuted += counter;
                _machine.Cpu.InstructionExecuted += observer;
                return RunCore(stopwatch);
            }
            finally
            {
                stopwatch?.Stop();
                if (countCycles)
                    Statistics = new ExecutionStatistics(Speed, _completedInstructions, _totalCycles,
                        stopwatch!.Elapsed.TotalSeconds,
                        CollectStatistics ? GC.GetAllocatedBytesForCurrentThread() - allocatedBefore : 0,
                        CollectStatistics ? GC.CollectionCount(0) - gen0Before : 0);
                _machine.Cpu.InstructionExecuted -= counter;
                _machine.Cpu.InstructionExecuted -= observer;
                _machine.StepTrace = previousStepTrace;
                _machine.Cpu.TraceInstruction = previousCpuTrace;
                _machine.RegisterTrace = previousRegisterTrace;
                _machine.Cpu.InstructionCacheEnabled = previousInstructionCache;
                try { _extracode.FinishOutput(); }
                finally { DetachFileTraceWriters(); }
            }
        }

        private void ObserveExecuted(Opcode opcode)
        {
            _completedInstructions++;
            _totalCycles += Besm6Timing.CyclesOf(opcode);
        }

        private void AttachFileTraceWriters()
        {
            string? canonicalPath = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");
            if (!string.IsNullOrWhiteSpace(canonicalPath))
            {
                ulong limit = ulong.MaxValue;
                string? value = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE_LIMIT");
                if (ulong.TryParse(value, out ulong parsedLimit))
                    limit = parsedLimit;

                _canonicalTraceWriter = new CanonicalTraceWriter(canonicalPath, limit);
                _machine.Cpu.InstructionTrace += _canonicalTraceWriter.Write;
            }

            if (TypedInstructionTrace is not null)
            {
                // Профайлер опкодов (Runtime/Profiling): только наблюдение, не влияет на семантику.
                _machine.Cpu.InstructionTrace += TypedInstructionTrace;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BESM6_INSTR_TRACE")))
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "instr_trace.log");
                _diagnosticTraceWriter = new DiagnosticTraceWriter(path);
                _machine.Cpu.InstructionTrace += _diagnosticTraceWriter.Write;
                _machine.Cpu.RegisterTrace += _diagnosticTraceWriter.Write;
            }
        }

        private void DetachFileTraceWriters()
        {
            if (TypedInstructionTrace is not null)
            {
                _machine.Cpu.InstructionTrace -= TypedInstructionTrace;
            }

            if (_canonicalTraceWriter is not null)
            {
                _machine.Cpu.InstructionTrace -= _canonicalTraceWriter.Write;
                _canonicalTraceWriter.Dispose();
                _canonicalTraceWriter = null;
            }

            if (_diagnosticTraceWriter is not null)
            {
                _machine.Cpu.InstructionTrace -= _diagnosticTraceWriter.Write;
                _machine.Cpu.RegisterTrace -= _diagnosticTraceWriter.Write;
                _diagnosticTraceWriter.Dispose();
                _diagnosticTraceWriter = null;
            }
        }
        private LoadResult RunCore(Stopwatch? wallStopwatch)
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
            bool executeBlocks = Speed == ExecutionSpeed.Max && !LoopDetect && progressOutput is null;

            const int LoopWindow = 20_000;
            const int LoopRange = 16;
            long[]? kHistory = LoopDetect ? new long[LoopWindow] : null;
            int kHistIdx = 0;

            if (InstructionTrace != null)
            {
                _machine.StepTrace += InstructionTrace;
            }

            if (CppInstructionTrace != null)
            {
                _machine.Cpu.TraceInstruction += CppInstructionTrace;
            }

            if (RegisterTrace != null)
            {
                _machine.BeginRegisterTrace();
                _machine.RegisterTrace += RegisterTrace;
            }

            wallStopwatch?.Start();
            while (InstructionsExecuted < limit)
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
                        stopped = _machine.Step();
                        _instructionsExecuted++;
                    }
                    if (pacer is not null && (stopped || _totalCycles >= nextCheckpoint))
                    {
                        if (!pacer.Synchronize(_totalCycles, wallLimitSeconds))
                            return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                        nextCheckpoint = _totalCycles + ExecutionPacer.CheckpointCycles;
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
                                         "See plans/monsys-kernel-support.md.";
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
                    _machine.Cpu.StackCorrection();
                    _extracode.FinishOutput();

                    if (string.IsNullOrEmpty(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                        if (pacer is not null && !pacer.Synchronize(_totalCycles, wallLimitSeconds))
                            return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    if (_machine.Cpu.Intercept(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                        if (pacer is not null && _totalCycles >= nextCheckpoint)
                        {
                            if (!pacer.Synchronize(_totalCycles, wallLimitSeconds))
                                return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                            nextCheckpoint = _totalCycles + ExecutionPacer.CheckpointCycles;
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
