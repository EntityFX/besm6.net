using System;
using System.Collections.Generic;

namespace Besm6.Cli
{
    public sealed class HelpCommand : ICommand
    {
        public string Name => "help";
        public string Description => "Show help";
        public string Usage => "besm6 help";

        private readonly List<ICommand> _commands;

        public HelpCommand(List<ICommand> commands)
        {
            _commands = commands;
        }

        public int Execute(string[] args) 
        {
            Console.WriteLine("BESM-6 Simulator");
            Console.WriteLine("Usage: besm6 <command> [args]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            foreach (var cmd in _commands)
            {
                Console.WriteLine("  " + cmd.Name.PadRight(10) + " " + cmd.Description);
            }
            Console.WriteLine();
            Console.WriteLine("Run without a command to start the interactive debugger.");
            return 0;
        }
    }
}
