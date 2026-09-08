namespace Besm6.Tui
{
    /// <summary>
    /// Точка входа TUI: разбирает аргументы командной строки
    /// (<c>--config &lt;path&gt;</c>, необязательный путь к .dub, <c>--help</c>),
    /// создаёт конфигурацию, контроллер и панель.
    /// </summary>
    public static class TuiApplication
    {
        /// <summary>Текст справки для <c>besm6-tui --help</c>.</summary>
        public const string HelpText =
            "BESM-6 TUI — interactive machine panel\n" +
            "\n" +
            "Usage: besm6-tui [job-file.dub] [options]\n" +
            "\n" +
            "Options:\n" +
            "  --config <path>   Path to besm6.json (default: ./besm6.json)\n" +
            "  --help, -h        Show this help and exit\n" +
            "\n" +
            "Panel commands:\n" +
            "  load <file.dub>   Load and mount a .dub job\n" +
            "  run               Run the machine until STOP or limit\n" +
            "  step              Execute one instruction\n" +
            "  mem <hex>         Show/set memory window base address\n" +
            "  asm <instr>       Assemble one instruction word\n" +
            "  write <a> <v>     Write a word to memory\n" +
            "  reset             Reset the processor\n" +
            "  quit              Exit\n";

        /// <summary>
        /// Разбирает аргументы и запускает TUI. <c>--help</c> печатает справку
        /// и завершается без входа в интерактивный цикл.
        /// </summary>
        public static int Run(string[] args)
        {
            string? configPath = null;
            string? jobFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--help" || arg == "-h")
                {
                    Console.Write(HelpText);
                    return 0;
                }
                if (arg == "--config")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--config requires a path");
                        return 1;
                    }
                    configPath = args[++i];
                    continue;
                }
                if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                {
                    configPath = arg.Substring("--config=".Length);
                    continue;
                }
                if (arg.StartsWith("--"))
                {
                    Console.Error.WriteLine("Unknown option: " + arg);
                    Console.Error.WriteLine("Run 'besm6-tui --help' for usage.");
                    return 1;
                }
                if (jobFile != null)
                {
                    Console.Error.WriteLine("Only one job file is allowed");
                    return 1;
                }
                jobFile = arg;
            }

            Config config;
            try
            {
                config = Config.Load(configPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("config error: " + ex.Message);
                return 1;
            }

            var controller = new TuiController(config);
            controller.Initialize(jobFile);
            return new TuiApp(controller).Run();
        }
    }
}
