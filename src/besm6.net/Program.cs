using System;
using System.Collections.Generic;
using Besm6.Cli;
using Besm6.Core;

namespace Besm6
{
    class Program
    {
        static int Main(string[] args)
        {
            // Build command registry.
            var commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase)
            {
                { "run",    new RunCommand() },
                { "asm",    new AsmCommand() },
                { "disasm", new DisasmCommand() },
                { "check",  new CheckCommand() },
                { "tui",    new TuiCommand() },
                { "help",   null! }, // filled below
            };
            commands["help"] = new HelpCommand(new List<ICommand>(commands.Values));

            if (args.Length == 0)
            {
                // Interactive debugger (default mode).
                Console.WriteLine("=== BESM-6 Simulator Startup ===");
                var cfg = Config.Load();
                var machine = MachineFactory.CreateMachine(cfg);
                var debugger = new Debugger(machine);
                debugger.Start();
                return 0;
            }

            string cmdName = args[0];
            if (!commands.TryGetValue(cmdName, out var cmd))
            {
                Console.Error.WriteLine($"Unknown command: {cmdName}");
                Console.Error.WriteLine("Run 'besm6 help' for usage.");
                return 1;
            }

            // Pass remaining args.
            string[] cmdArgs = args.Length > 1
                ? args[1..]
                : Array.Empty<string>();

            return cmd.Execute(cmdArgs);
        }
    }
}