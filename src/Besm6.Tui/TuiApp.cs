namespace Besm6.Tui
{
    /// <summary>
    /// Панель (dashboard) БЭСМ-6: регистры, лампочки-индикаторы и окно памяти
    /// постоянно отображаются на экране и перерисовываются при каждом действии.
    /// TuiApp содержит только ввод-вывод и главный цикл; команды выполняет
    /// <see cref="TuiController"/>, панель рисует <see cref="TuiRenderer"/>.
    /// </summary>
    public sealed class TuiApp
    {
        private readonly TuiController _controller;
        private readonly TuiRenderer _renderer;

        private const string GRAY = "\x1b[90m";
        private const string RESET = "\x1b[0m";

        public TuiApp(TuiController controller, TuiRenderer? renderer = null)
        {
            _controller = controller;
            _renderer = renderer ?? new TuiRenderer();
        }

        /// <summary>Основной интерактивный цикл: ввод → парсер → контроллер → рендер.</summary>
        public int Run()
        {
            Draw();

            while (true)
            {
                Console.Write("\r" + GRAY + "  > " + RESET);
                var line = Console.ReadLine() ?? "";
                line = line.Trim();
                if (line.Length == 0) continue;

                var request = TuiCommandParser.Parse(line);

                if (request.Command == TuiCommand.Quit)
                {
                    Console.WriteLine("\nBESM-6 TUI exited.");
                    return 0;
                }

                if (request.IsKnown)
                    _controller.Execute(request);
                else
                    _controller.State.Status = "Unknown command: " + line.Split(' ')[0].ToLowerInvariant() + "  (help)";

                Draw();
            }
        }

        private void Draw()
        {
            Console.Write(_renderer.Render(_controller.State, _controller.Machine));
        }
    }
}
