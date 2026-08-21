using System;
using Besm6.Tui;

namespace Besm6.Cli
{
    public sealed class TuiCommand : ICommand
    {
        public string Name => "tui";
        public string Description => "Launch interactive TuI debugger";
        public string Usage => "besm6 tui [file.dub] [--config path]";

        public int Execute(string[] args) 
        {
            string? jobFile = null;
            string? configPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length) { configPath = args[++i]; }
                else { jobFile = args[i]; }
            }

            var cfg = Config.Load(configPath);
            var tui = new TuiApp(cfg, jobFile);
            return tui.Run();
        }
    }
}
