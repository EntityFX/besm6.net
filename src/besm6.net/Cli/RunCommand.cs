using System;
using System.IO;
using System.Text;
using Besm6.Asm;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Cli
{
    /// <summary>
    /// Команда `run` — загрузить и выполнить .dub файл.
    /// </summary>
    public sealed class RunCommand : ICommand
    {
        public string Name => "run";
        public string Description => "Load and execute a .dub job script";
        public string Usage => "besm6 run <file.dub> [--limit N] [--verbose] [--trace] [--no-wall-clock] [--no-loop-detect] [--hang-detect|--no-hang-detect] [--config path]";

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
            bool loopDetect = false;
            bool noLoopDetect = false;
            bool noWallClock = false;
            bool hangDetect = false;
            bool noHangDetect = false;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--limit" when i + 1 < args.Length:
                        long.TryParse(args[++i], out limit);
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--trace":
                        trace = true;
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
                }
            }

            Config cfg = Config.Load(configPath);
            if (limit == 0) limit = cfg.DefaultLimit;

            StreamWriter? regsWriter = null;
            try
            {
                var loader = MachineFactory.CreateLoader(cfg);
                loader.InstructionLimit = limit;
                loader.Verbose = verbose;
                loader.LoopDetect = loopDetect && !noLoopDetect;
                loader.HangDetect = hangDetect && !noHangDetect;
                if (noWallClock) loader.UseWallClock = false;

                if (trace)
                {
                    loader.InstructionTrace = (pc, word) =>
                    {
                        string dis = Disassembler.DisasmWord((long)word);
                        Console.WriteLine($"  PC=0{pc:X5}  {dis}");
                    };
                }

                if (regsFile != null)
                {
                    regsWriter = new StreamWriter(regsFile, false, new UTF8Encoding(false));
                    Action<string> sink = line => regsWriter!.WriteLine(line);
                    loader.CppInstructionTrace = (pc, rf, rk, op) =>
                        sink(OctPc(pc) + " " + (rf ? "R" : "L") + ": " + OctalInstr(rk));
                    loader.RegisterTrace = (name, val) => sink(RegLine(name, val));
                }

                var result = loader.RunScript(jobFile);
                Console.WriteLine(result);

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

        private static string OctPc(uint pc) => Convert.ToString(pc & 0x7FFF, 8).PadLeft(5, '0');

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

        /// <summary>besm6_print_word_octal: 4 группы по 4 восьмеричных разряда.</summary>
        private static string Word48Oct(ulong v) =>
            OctW((v >> 36) & 0xFFF, 4) + " " + OctW((v >> 24) & 0xFFF, 4) + " " +
            OctW((v >> 12) & 0xFFF, 4) + " " + OctW(v & 0xFFF, 4);

        private static string RegLine(string name, ulong val)
        {
            switch (name)
            {
                case "ACC": return "      ACC = " + Word48Oct(val);
                case "RMR": return "      RMR = " + Word48Oct(val);
                case "RAU": return "      RAU = " + OctW(val, 2);
                case "MOD": return "      MOD = " + OctW(val, 5);
                case "CLEARMOD": return "      Clear MOD";
                default: return "      " + name + " = " + OctW(val, 5);
            }
        }
    }
}
