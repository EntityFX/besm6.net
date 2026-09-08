using System;
using System.Collections.Generic;
using System.IO;

namespace Besm6.Runtime
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

        public bool Verbose { get; set; }
        public bool LoopDetect { get => _exec.LoopDetect; set => _exec.LoopDetect = value; }
        public Action<string>? Output { get; set; }
        public Action<int, ulong>? InstructionTrace { get => _exec.InstructionTrace; set => _exec.InstructionTrace = value; }
        public Action<uint, bool, uint, uint>? CppInstructionTrace { get => _exec.CppInstructionTrace; set => _exec.CppInstructionTrace = value; }
        public Action<string, ulong>? RegisterTrace { get => _exec.RegisterTrace; set => _exec.RegisterTrace = value; }
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
                releaseTapes: mask => _tapes.ReleaseTapes(mask));

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
            return _machine.Cpu.GetK();
        }

        public LoadResult RunLoaded()
        {
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult RunJob(DubJob job, IEnumerable<string> rawLines)
        {
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
            _programLoader.LoadRawWords(job);
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult RunAssem(DubJob job)
        {
            _programLoader.LoadAssembler(job);
            _exec.InstallHook();
            return _exec.Run();
        }

        public LoadResult BootAndRun(DubJob job)
        {
            _tapes.MountScriptTapes(job);
            _mons.Boot();
            _exec.InstallHook();
            if (Verbose) Console.WriteLine("Booting MONSYS from disk 030 (drum 021 -> disk 030)...");
            return _exec.Run();
        }

        public void BootMsDubna() => _mons.Boot();
    }
}