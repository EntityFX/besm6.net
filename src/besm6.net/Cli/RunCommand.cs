using System;
using System.IO;
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
        public string Usage => "besm6 run <file.dub> [--limit N] [--verbose] [--trace] [--config path]";

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
                    case "--config" when i + 1 < args.Length:
                        configPath = args[++i];
                        break;
                }
            }

            Config cfg = Config.Load(configPath);
            if (limit == 0) limit = cfg.DefaultLimit;

            try
            {
                var loader = MachineFactory.CreateLoader(cfg);
                loader.InstructionLimit = limit;
                loader.Verbose = verbose;

                if (trace)
                {
                    loader.InstructionTrace = (pc, word) =>
                    {
                        string dis = Disassembler.DisasmWord(word);
                        Console.WriteLine($"  PC=0{pc:X5}  {dis}");
                    };
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
        }
    }
}