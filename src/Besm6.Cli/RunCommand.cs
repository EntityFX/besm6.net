using System;
using System.IO;
using System.Text;
using Besm6.Assembler;
using Besm6.Core;
namespace Besm6.Cli
{
    /// <summary>
    /// Команда `run` — загрузить и выполнить .dub файл.
    /// </summary>
    public sealed class RunCommand : ICommand
    {
        public string Name => "run";
        public string Description => "Load and execute a .dub job script";
        public string Usage => RunOptions.Usage;

        public int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine($"Usage: {Usage}");
                return 1;
            }

            if (!RunOptions.TryParse(args, out var options, out string? error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            string jobFile = options.JobFile;
            bool trace = options.Trace;
            string? regsFile = options.RegistersFile;
            string? memDumpFile = options.MemoryDumpFile;
            long memDumpAt = options.MemoryDumpAt;
            int memDumpCount = options.MemoryDumpCount;
            bool profile = options.Profile;
            bool stats = options.Stats;
            long profilePasses = options.ProfilePasses;
            long profileBaselineCycles = options.ProfileBaselineCycles;
            long profileBaselineInstructions = options.ProfileBaselineInstructions;
            long profileMflops = options.ProfileMflops;
            long profileOps = options.ProfileOps;
            long profileLoopCycles = options.ProfileLoopCycles;
            bool memDumpDone = false;

            StreamWriter? regsWriter = null;
            try
            {
                Config cfg = options.LoadConfiguration();
                // Validate bundled runtime assets before machine creation or execution.
                ResolvedRuntimeAssets runtimeAssets = MachineFactory.ValidateRuntimeAssets(cfg);

                var machine = MachineFactory.CreateMachine(cfg);
                var loader = MachineFactory.CreateLoader(cfg, machine, runtimeAssets);
                options.ConfigureLoader(loader, cfg);

                OpcodeProfiler? opcodeProfiler = null;
                if (profile)
                {
                    // Профайлер опкодов: read-only хук, Gate A (семантика инструкций) не затрагивается.
                    opcodeProfiler = new OpcodeProfiler();
                }

                // Memory dumps need full snapshots; opcode profiling uses the allocation-free hook.
                Action<InstructionTraceRecord>? dumpObserver = null;
                if (memDumpFile != null && memDumpAt >= 0)
                {
                    long seen = 0;
                    dumpObserver = rec =>
                    {
                        if (memDumpAt < 0) return;      // снимок уже сделан
                        if (++seen < memDumpAt) return;
                        RunOutput.DumpMemory(machine, memDumpFile, memDumpCount);
                        memDumpAt = -1;
                        memDumpDone = true;
                    };
                }

                loader.TypedInstructionTrace = dumpObserver;
                if (opcodeProfiler != null)
                    loader.InstructionExecuted = opcode => opcodeProfiler.Observe((uint)opcode);

                if (trace)
                {
                    loader.InstructionTrace = (k, word) =>
                    {
                        string dis = Disassembler.DisasmWord((long)word);
                        Console.WriteLine($"  K=0{k:X5}  {dis}");
                    };
                }

                if (regsFile != null)
                {
                    regsWriter = new StreamWriter(regsFile, false, new UTF8Encoding(false));
                    Action<string> sink = line => regsWriter!.WriteLine(line);
                    loader.CppInstructionTrace = (k, rf, rk, op) =>
                        sink(RunOutput.OctK(k) + " " + (rf ? "R" : "L") + ": " + RunOutput.OctalInstr(rk));
                    loader.RegisterTrace = (name, val) => sink(RunOutput.RegLine(name, val));
                }

                var result = loader.RunScript(jobFile);
                Console.WriteLine(result);

                if (memDumpFile != null && !memDumpDone)
                    RunOutput.DumpMemory(machine, memDumpFile, memDumpCount);

                if (opcodeProfiler != null)
                {
                    Console.Write(opcodeProfiler.FormatSummary());
                    if (profilePasses > 0)
                        Console.Write(opcodeProfiler.FormatDhrystone(
                            profilePasses, profileBaselineCycles, profileBaselineInstructions));
                    if (profileOps > 0)
                        Console.Write(opcodeProfiler.FormatWhetstone(
                            profileOps, profileBaselineCycles, profileBaselineInstructions,
                            profileLoopCycles));
                    if (profileMflops > 0)
                        Console.Write(opcodeProfiler.FormatMflops(
                            profileMflops, profileBaselineCycles, profileBaselineInstructions));
                }

                if (stats && loader.Statistics is ExecutionStatistics statistics)
                    RunOutput.WriteStatistics(machine, statistics);
                if (loader.Statistics is { OriginalTempoMet: false })
                    Console.Error.WriteLine("Original speed target not met: host execution is slower than modeled BESM-6 time.");

                if (result.Success) return 0;
                if (result.LimitExceeded)
                {
                    Console.Error.WriteLine("Simulation did not terminate (instruction limit reached).");
                    return 2;
                }
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            finally
            {
                regsWriter?.Flush();
                regsWriter?.Dispose();
            }
        }


    }
}
