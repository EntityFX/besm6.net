using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using System;
using System.Collections.Generic;
using System.IO;

namespace Besm6.Runtime.Loading
{
    /// <summary>
    /// Загрузчик программ Dubna (.dub job-скрипты).
    /// Оркестрирует: разбор скрипта, загрузку в память, загрузку MONSYS,
    /// обработку экстракодов и запуск на процессоре.
    /// </summary>
    public sealed class DubnaLoader
    {
        public const int DefaultLoadBase = 512;

        private readonly MachineCore _machine;
        private readonly ExtracodeHandler _extracode;
        private readonly TapeMountService _tapes;
        private readonly JobProgramLoader _programLoader;
        private readonly MonsysBootstrapper _mons;
        private readonly ExecutionLoop _exec;

        public long InstructionLimit
        {
            get => _exec.InstructionLimit;
            set => _exec.InstructionLimit = value;
        }

        /// <summary>Optional bound on processor step attempts, including attempts that do not complete an instruction.</summary>
        public long? StepAttemptLimit
        {
            get => _exec.StepAttemptLimit;
            set => _exec.StepAttemptLimit = value;
        }

        public long WallClockLimitMs
        {
            get => _exec.WallClockLimitMs;
            set => _exec.WallClockLimitMs = value;
        }

        public bool UseWallClock
        {
            get => _extracode.UseWallClock;
            set => _extracode.UseWallClock = value;
        }

        public bool HangDetect
        {
            get => _extracode.HangDetect;
            set => _extracode.HangDetect = value;
        }

        public bool Verbose { get => _exec.ProgressEnabled; set => _exec.ProgressEnabled = value; }
        public bool LoopDetect { get => _exec.LoopDetect; set => _exec.LoopDetect = value; }
        public ExecutionSpeed Speed { get => _exec.Speed; set => _exec.Speed = value; }
        public bool CollectStatistics { get => _exec.CollectStatistics; set => _exec.CollectStatistics = value; }
        public double OriginalTimeFactor { get => _exec.OriginalTimeFactor; set => _exec.OriginalTimeFactor = value; }
        public double OriginalMathFactor { get => _exec.OriginalMathFactor; set => _exec.OriginalMathFactor = value; }
        /// <summary>Enable CTIME/STIME: model time in max/original, host elapsed time in virtual.
        /// Independent of DATE*; 20 ms resolution and a new origin for every execution run.</summary>
        public bool GuestClockEnabled { get => _exec.GuestClockEnabled; set => _exec.GuestClockEnabled = value; }
        /// <summary>Opt-in legacy mapping of *forex to *fortran. By default the monitor receives *forex unchanged.</summary>
        public bool ForexAsFortran { get => _programLoader.ForexAsFortran; set => _programLoader.ForexAsFortran = value; }
        public ExecutionStatistics? Statistics => _exec.Statistics;
        public Action<Opcode>? InstructionExecuted { get => _exec.InstructionExecuted; set => _exec.InstructionExecuted = value; }
        /// <summary>Runs on the execution thread between bounded instruction batches, without enabling instruction tracing.</summary>
        public Action<long>? ExecutionCheckpoint { get => _exec.ExecutionCheckpoint; set => _exec.ExecutionCheckpoint = value; }
        public Action<string>? Output { get; set; }
        /// <summary>Optional terminal output sink; defaults to the normal output sink.</summary>
        public Action<string>? TerminalOutput { get; set; }
        public IReadOnlyList<StorageMountInfo> StorageMounts => _tapes.GetMounts();
        /// <summary>Restrict hosted local, home, and temporary disk files to this directory.</summary>
        public string? HostFileDirectory { get => _tapes.HostFileDirectory; set => _tapes.HostFileDirectory = value; }
        public Action<int, ulong>? InstructionTrace { get => _exec.InstructionTrace; set => _exec.InstructionTrace = value; }
        public Action<uint, bool, uint, uint>? CppInstructionTrace { get => _exec.CppInstructionTrace; set => _exec.CppInstructionTrace = value; }
        public Action<string, ulong>? RegisterTrace { get => _exec.RegisterTrace; set => _exec.RegisterTrace = value; }

        /// <summary>
        /// Полная типизированная трассировка инструкций со снимками регистров.
        /// Для подсчёта опкодов без снимков используется <see cref="InstructionExecuted"/>.
        /// </summary>
        public Action<InstructionTraceRecord>? TypedInstructionTrace
        { get => _exec.TypedInstructionTrace; set => _exec.TypedInstructionTrace = value; }

        public Func<string, string>? Input { get; set; }

        public long InstructionsExecuted => _exec.InstructionsExecuted;
        public bool HaltedByStop => _exec.HaltedByStop;
        public int LoadedBase => _programLoader.LoadedBase;

        public DubnaLoader(MachineCore machine, string? tapesDir = null)
        {
            _machine = machine;
            var verboseLog = (string? s) => { if (Verbose) Console.WriteLine(s); };

            _tapes = new TapeMountService(tapesDir, verboseLog);

            _extracode = new ExtracodeHandler(
                machine,
                diskByTapeId: id => _tapes.GetDiskByTapeId(id),
                diskByUnit: u => _tapes.GetDiskByUnit(u),
                drumByUnit: u => _tapes.GetDrumByUnit(u),
                output: s => (Output ?? (x => Console.Write(x)))(s),
                input: p => { if (Input != null) return Input(p); Console.Write(p); return Console.ReadLine() ?? ""; },
                mountTape: (tapeId, unit) => _tapes.MountTape(unit, tapeId),
                mountTapeWithMode: (tapeId, unit, writePermit) => _tapes.MountTape(unit, tapeId, writePermit),
                fileSearch: (disc, file, write) => _tapes.FileSearch(disc, file, write),
                fileMount: (unit, idx, write, off) => _tapes.FileMount(unit, idx, write, off),
                scratchMount: (unit, zones) => _tapes.ScratchMount(unit, zones),
                findTape: tapeId => _tapes.FindUnitForTapeId(tapeId),
                releaseTapes: mask => _tapes.ReleaseTapes(mask),
                terminalOutput: s => (TerminalOutput ?? Output ?? (x => Console.Write(x)))(s));

            _programLoader = new JobProgramLoader(machine, _tapes, verboseLog);
            _mons = new MonsysBootstrapper(machine, _tapes, _extracode, verboseLog);
            _exec = new ExecutionLoop(machine, _extracode, verboseLog,
                s => (Output ?? (x => Console.Write(x)))(s),
                s => { if (Verbose) Console.Write(s); });
        }
        // ─── Tape lifecycle ───────────────────────────────────────────────────

        public bool MountTape(int unit, long tapeId, bool writePermit = false)
            => _tapes.MountTape(unit, tapeId, writePermit);

        public void ReleaseTapes(long mask) => _tapes.ReleaseTapes(mask);

        public void MountScriptTapes(DubJob job) => _tapes.MountScriptTapes(job);

        // ─── Script to drum ───────────────────────────────────────────────────

        public void WriteScriptToDrum(DubJob job, IEnumerable<string> rawLines)
            => _programLoader.WriteScriptToDrum(job, rawLines);

        // ─── Loading and execution ────────────────────────────────────────────

        public LoadResult RunScript(string path)
        {
            _machine.EnsureExecutionAllowed();
            var job = JobParser.ParseFile(path);
            return RunJob(job, File.ReadAllLines(path));
        }

        public long LoadScript(string path)
        {
            var job = JobParser.ParseFile(path);
            var rawLines = File.ReadAllLines(path);
            _machine.Reset();
            _exec.InstallHook();

            if (job.RawWords.Count > 0)
                return _programLoader.LoadRawWords(job);

            if (job.Execute == null && job.AssemProgram.Count > 0)
                return _programLoader.LoadAssembler(job);

            // MONSYS path (no execution).
            _programLoader.WriteScriptToDrum(job, rawLines);
            _tapes.MountScriptTapes(job);
            _mons.Boot();
            return _machine.Cpu.K;
        }

        public LoadResult RunLoaded()
        {
            _machine.EnsureExecutionAllowed();
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult RunJob(DubJob job, IEnumerable<string> rawLines)
        {
            _machine.EnsureExecutionAllowed();
            _machine.Reset();

            if (job.RawWords.Count > 0)
                return RunRawWords(job);

            if (job.Execute == null && job.AssemProgram.Count > 0)
                return RunAssem(job);

            _programLoader.WriteScriptToDrum(job, rawLines);
            return BootAndRun(job);
        }

        public LoadResult RunRawWords(DubJob job)
        {
            _machine.EnsureExecutionAllowed();
            _programLoader.LoadRawWords(job);
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult RunAssem(DubJob job)
        {
            _machine.EnsureExecutionAllowed();
            _programLoader.LoadAssembler(job);
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult BootAndRun(DubJob job)
        {
            _machine.EnsureExecutionAllowed();
            _tapes.MountScriptTapes(job);
            _mons.Boot();
            _exec.InstallHook();
            if (Verbose) Console.WriteLine("Booting MONSYS from disk 030 (drum 021 -> disk 030)...");
            return _exec.Run();
        }

        public void BootMsDubna() => _mons.Boot();
    }
}
