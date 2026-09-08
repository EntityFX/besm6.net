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
        public long InstructionsExecuted { get; private set; }
        public bool HaltedByStop { get; private set; }

        public Action<int, ulong>? InstructionTrace { get; set; }
        public Action<uint, bool, uint, uint>? CppInstructionTrace { get; set; }
        public Action<string, ulong>? RegisterTrace { get; set; }

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
            try
            {
                AttachFileTraceWriters();
                return RunCore();
            }
            finally
            {
                _extracode.FinishOutput();
                DetachFileTraceWriters();
            }
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
        private LoadResult RunCore()
        {
            long limit = InstructionLimit;
            long wallLimitMs = WallClockLimitMs;
            var wallStopwatch = Stopwatch.StartNew();
            InstructionsExecuted = 0;
            HaltedByStop = false;
            long lastReport = 0;

            const int LoopWindow = 20_000;
            const int LoopRange = 16;
            long[] kHistory = new long[LoopWindow];
            int kHistIdx = 0;

            if (InstructionTrace != null)
            {
                long[] counter = { 0 };
                _machine.StepTrace = (k, word) =>
                {
                    counter[0]++;
                    InstructionTrace(k, word);
                };
            }

            if (CppInstructionTrace != null)
            {
                _machine.Cpu.TraceInstruction = (k, rf, rk, op) => CppInstructionTrace(k, rf, rk, op);
            }

            if (RegisterTrace != null)
            {
                _machine.BeginRegisterTrace();
                _machine.RegisterTrace = RegisterTrace;
            }

            while (InstructionsExecuted < limit)
            {
                try
                {
                    bool stopped = _machine.Step();
                    InstructionsExecuted++;
                    if (stopped)
                    {
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    if (wallLimitMs > 0 && (InstructionsExecuted & 4095) == 0
                        && wallStopwatch.ElapsedMilliseconds > wallLimitMs)
                    {
                        if (_verboseLog != null) _verboseLog("");
                        return LoadResult.StoppedByLimit(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    long currentK = _machine.Cpu.GetK();
                    kHistory[kHistIdx % LoopWindow] = currentK;
                    kHistIdx++;

                    if (LoopDetect && InstructionsExecuted >= LoopWindow && (InstructionsExecuted % LoopWindow) == 0)
                    {
                        long minK = long.MaxValue, maxK = long.MinValue;
                        for (int i = 0; i < LoopWindow; i++)
                        {
                            long v = kHistory[i];
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

                    if (_progressOutput != null && InstructionsExecuted - lastReport >= 100_000)
                    {
                        lastReport = InstructionsExecuted;
                        _progressOutput($"\r  [{InstructionsExecuted / 1000}K] K=0{currentK:X4}   ");
                    }
                }
                catch (ProcessorException ex)
                {
                    _machine.Cpu.StackCorrection();
                    _extracode.FinishOutput();

                    if (string.IsNullOrEmpty(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetK(), InstructionsExecuted);
                    }

                    if (_machine.Cpu.Intercept(ex.Message))
                    {
                        _machine.Cpu.CanonPost(_machine.Cpu.GetK(), _machine.Cpu._rightInstrFlag);
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