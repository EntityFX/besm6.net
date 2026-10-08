using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
        public string Usage => "besm6 run <file.dub> [--speed original|max] [--stats] [--limit N] [--verbose] [--trace] [--dump-mem FILE] [--dump-mem-at N] [--dump-mem-count N] [--no-wall-clock] [--no-loop-detect] [--hang-detect|--no-hang-detect] [--profile] [--passes N] [--mflops N] [--ops N] [--loop-cycles N] [--baseline-cycles N] [--baseline-instructions N] [--config path]";

        public int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine($"Usage: {Usage}");
                return 1;
            }

            string jobFile = args[0];
            long limit = 0;
            bool verbose = false;
            bool trace = false;
            string? configPath = null;
            string? regsFile = null;
            string? memDumpFile = null;
            long memDumpAt = -1;
            bool memDumpDone = false;
            int memDumpCount = 0;
            bool loopDetect = false;
            bool noLoopDetect = false;
            bool noWallClock = false;
            bool hangDetect = false;
            bool noHangDetect = false;
            bool profile = false;
            bool stats = false;
            ExecutionSpeed? speedOverride = null;
            long profilePasses = 0;
            long profileBaselineCycles = 0;
            long profileBaselineInstructions = 0;
            long profileMflops = 0;
        long profileOps = 0;
        long profileLoopCycles = 0;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--speed":
                        if (++i >= args.Length || !ExecutionSpeedNames.TryParse(args[i], out var parsedSpeed))
                        {
                            Console.Error.WriteLine("Error: --speed requires 'original' or 'max'.");
                            return 1;
                        }
                        speedOverride = parsedSpeed;
                        break;
                    case "--stats":
                        stats = true;
                        break;
                    case "--limit" when i + 1 < args.Length:
                        long.TryParse(args[++i], out limit);
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--trace":
                        trace = true;
                        break;
                    case "--dump-mem" when i + 1 < args.Length:
                        // Дамп памяти после прогона в SIMH-формате (6 байт на слово) —
                        // для дизассемблирования образа ОС, реально исполнявшейся на БЭСМ-6.
                        memDumpFile = args[++i];
                        break;
                    case "--dump-mem-at" when i + 1 < args.Length:
                        // Снимок памяти после N инструкций (нужно для дизассемблирования
                        // ОС: после завершения задания монитор затирает свой образ).
                        long.TryParse(args[++i], out memDumpAt);
                        break;
                    case "--dump-mem-count" when i + 1 < args.Length:
                        int.TryParse(args[++i], out memDumpCount);
                        break;
                    case "--trace-regs" when i + 1 < args.Length:
                        // регистров после каждого шага (см. ref/trace.cpp print_instruction/
                        regsFile = args[++i];
                        break;
                    case "--config" when i + 1 < args.Length:
                        configPath = args[++i];
                        break;
                    case "--no-wall-clock":
                        // workload-и (CERNLIB a400/z005, MONSYS-задачи) используют значение
                        // DATE* в потоке управления, и реальные часы ломают воспроизводимость.
                        noWallClock = true;
                        break;
                    case "--loop-detect":
                        // Включить эвристику spin-loop (отладка реальных зависаний).
                        loopDetect = true;
                        break;
                    case "--no-loop-detect":
                        // Отключить эвристику spin-loop (по умолчанию и так выключена).
                        loopDetect = false;
                        noLoopDetect = true;
                        break;
                    case "--hang-detect":
                        // Включить эвристику зависания явно.
                        hangDetect = true;
                        noHangDetect = false;
                        break;
                    case "--no-hang-detect":
                        // Отключить эвристику зависания (500+ экстракодов без вывода) —
                        hangDetect = false;
                        noHangDetect = true;
                        break;
                    case "--profile":
                        // Гистограмма опкодов + модельное время по таблице тактов БЭСМ-6.
                        profile = true;
                        break;
                    case "--passes" when i + 1 < args.Length:
                        // Число проходов тела бенчмарка (для блока Dhrystone/DMIPS).
                        long.TryParse(args[++i], out profilePasses);
                        break;
                    case "--baseline-cycles" when i + 1 < args.Length:
                        // Такты накладных расходов (компиляция/загрузка), которые вычитаются.
                        long.TryParse(args[++i], out profileBaselineCycles);
                        break;
                    case "--baseline-instructions" when i + 1 < args.Length:
                        // Инструкции накладных расходов (компиляция/загрузка), которые вычитаются.
                        long.TryParse(args[++i], out profileBaselineInstructions);
                        break;
                    case "--ops" when i + 1 < args.Length:
                        // Число операций восьми циклов Whetstone (для блока MWIPS).
                        long.TryParse(args[++i], out profileOps);
                        break;
                    case "--loop-cycles" when i + 1 < args.Length:
                        // Такты, приходящиеся только на циклы Whetstone (посегментный замер).
                        long.TryParse(args[++i], out profileLoopCycles);
                        break;
                    case "--mflops" when i + 1 < args.Length:
                        // Число FLOPS, выполненных ядром (для блока MP-MFLOPS). Значение
                        // берётся из строки "FLOPS=" в выводе самой программы.
                        long.TryParse(args[++i], out profileMflops);
                        break;
                }
            }

            StreamWriter? regsWriter = null;
            try
            {
                Config cfg = Config.Load(configPath);
                if (speedOverride is ExecutionSpeed speed) cfg.Speed = speed;
                if (limit == 0) limit = cfg.DefaultLimit;
                // SuperPlan Task A4: fail-fast ДО запуска процессора — чётко перечислить отсутствующие
                // bundled runtime-образы (monsys.9/librar.12/...) и способ восстановления
                // (docs/runtime-assets.md), а не молча упираться в junction ref/dubna.
                ResolvedRuntimeAssets runtimeAssets = MachineFactory.ValidateRuntimeAssets(cfg);

                var machine = MachineFactory.CreateMachine(cfg);
                var loader = MachineFactory.CreateLoader(cfg, machine, runtimeAssets);
                loader.InstructionLimit = limit;
                loader.CollectStatistics = stats;
                loader.Verbose = verbose;
                loader.LoopDetect = loopDetect && !noLoopDetect;
                loader.HangDetect = hangDetect && !noHangDetect;
                if (noWallClock) loader.UseWallClock = false;

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
                        DumpMemory(machine, memDumpFile, memDumpCount);
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
                        sink(OctK(k) + " " + (rf ? "R" : "L") + ": " + OctalInstr(rk));
                    loader.RegisterTrace = (name, val) => sink(RegLine(name, val));
                }

                var result = loader.RunScript(jobFile);
                Console.WriteLine(result);

                if (memDumpFile != null && !memDumpDone)
                    DumpMemory(machine, memDumpFile, memDumpCount);

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
                    WriteStatistics(machine, statistics);
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


        private static string OctW(ulong x, int width) => Convert.ToString((long)x, 8).PadLeft(width, '0');

        private static string OctK(uint k) => Convert.ToString(k & 0x7FFF, 8).PadLeft(5, '0');

        /// <summary>besm6_print_instruction_octal: reg(2) + [длинная: mid(2) addr(5)] | [короткая: op(3) addr(4)].</summary>
        private static string OctalInstr(uint rk)
        {
            int reg = (int)(rk >> 20) & 0x0F;
            if ((rk & 0x80000u) != 0)
            {
                int mid = (int)((rk >> 15) & 0x1F);
                int addrL = (int)(rk & 0x7FFF);
                return OctW((ulong)reg, 2) + " " + OctW((ulong)mid, 2) + " " + OctW((ulong)addrL, 5);
            }
            int op = (int)((rk >> 12) & 0x7F);
            int addr = (int)(rk & 0xFFF);
            return OctW((ulong)reg, 2) + " " + OctW((ulong)op, 3) + " " + OctW((ulong)addr, 4);
        }

        private static void WriteStatistics(MachineCore machine, ExecutionStatistics statistics)
        {
            // Outside the measured loop: a reproducible fingerprint for benchmark parity checks.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> wordBytes = stackalloc byte[6];
            for (uint address = 0; address < (uint)machine.Memory.Size; address++)
            {
                ulong word = machine.Memory.Read(address).Value;
                for (int i = 0; i < 6; i++) wordBytes[i] = (byte)(word >> (40 - 8 * i));
                hash.AppendData(wordBytes);
            }
            var finalState = new ProcessorSnapshot(machine.Cpu.State);
            var report = new
            {
                statistics,
                finalState = new { finalState.K, finalState.IsRightHalf, A = finalState.A.Value,
                    Y = finalState.Y.Value, finalState.R, finalState.C, finalState.ApplyC,
                    finalState.EffectiveAddress, finalState.InterceptCount, finalState.InterceptAddress,
                    finalState.M, ClockTicks = machine.Clock.Tick },
                memorySha256 = Convert.ToHexString(hash.GetHashAndReset()),
            };
            Console.WriteLine("Execution stats: " + JsonSerializer.Serialize(report,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        /// <summary>
        /// Дамп ОЗУ в SIMH-формате (6 байт на 48-битное слово) — вход для
        /// tools/besm6tape.py, которым дизассемблируется образ ОС «Дубна».
        /// </summary>
        private static void DumpMemory(MachineCore machine, string path, int count)
        {
            int total = count > 0 ? count : 32768;   // 0o77777+1
            using var fs = File.Create(path);
            for (uint a = 0; a < (uint)total; a++)
            {
                ulong word = machine.Memory.Read(a).Value;
                for (int sh = 40; sh >= 0; sh -= 8)
                    fs.WriteByte((byte)((word >> sh) & 0xFF));
            }
            Console.Error.WriteLine($"memory dump: {total} words -> {path}");
        }

        /// <summary>besm6_print_word_octal: 4 группы по 4 восьмеричных разряда.</summary>
        private static string Word48Oct(ulong v) =>
            OctW((v >> 36) & 0xFFF, 4) + " " + OctW((v >> 24) & 0xFFF, 4) + " " +
            OctW((v >> 12) & 0xFFF, 4) + " " + OctW(v & 0xFFF, 4);

        private static string RegLine(string name, ulong val)
        {
            switch (name)
            {
                case "A": return "      A = " + Word48Oct(val);
                case "Y": return "      Y = " + Word48Oct(val);
                case "R": return "      R = " + OctW(val, 2);
                case "C": return "      C = " + OctW(val, 5);
                case "CLEARC": return "      Clear C";
                default: return "      " + name + " = " + OctW(val, 5);
            }
        }
    }
}
