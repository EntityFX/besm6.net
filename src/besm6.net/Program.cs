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
            var commandList = new List<ICommand>
            {
                new RunCommand(),
                new AsmCommand(),
                new DisasmCommand(),
                new CheckCommand(),
                new TuiCommand(),
            };
            commandList.Add(new HelpCommand(commandList));
            var commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
            foreach (ICommand command in commandList)
                commands.Add(command.Name, command);

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
