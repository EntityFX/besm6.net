using System;
using System.Collections.Generic;
using System.IO;

namespace Besm6.Cli
{
    /// <summary>
    /// Testable CLI application runner.
    /// Delegates to the command registry; redirects Console to provided writers.
    /// </summary>
    public static class CliApplication
    {
        public static int Run(string[] args, TextWriter? output = null, TextWriter? error = null)
        {
            var realOut = Console.Out;
            var realErr = Console.Error;
            if (output != null) Console.SetOut(output);
            if (error != null) Console.SetError(error);

            try
            {
                return Dispatch(args);
            }
            finally
            {
                Console.SetOut(realOut);
                Console.SetError(realErr);
            }
        }

        private static int Dispatch(string[] args)
        {
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
            foreach (var cmd in commandList)
                commands.Add(cmd.Name, cmd);

            if (args.Length == 0)
            {
                // No args → show help (exit 0).
                Console.WriteLine("BESM-6 Simulator");
                Console.WriteLine("Usage: besm6 <command> [args]");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                foreach (var c in commandList)
                    Console.WriteLine("  " + c.Name.PadRight(10) + " " + c.Description);
                return 0;
            }

            string cmdName = args[0];
            if (!commands.TryGetValue(cmdName, out var found))
            {
                Console.Error.WriteLine($"Unknown command: {cmdName}");
                Console.Error.WriteLine("Run 'besm6 help' for usage.");
                return 1;
            }

            string[] cmdArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
            return found.Execute(cmdArgs);
        }
    }
}