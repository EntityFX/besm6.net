namespace Besm6.Cli;

/// <summary>Immutable invocation data, separated from machine creation and execution.</summary>
internal sealed record RunOptions
{
    internal const string Usage = "besm6 run <file.dub> [--speed original|max] [--stats] [--guest-clock] [--limit N] [--verbose] [--trace] [--dump-mem FILE] [--dump-mem-at N] [--dump-mem-count N] [--no-wall-clock] [--no-loop-detect] [--hang-detect|--no-hang-detect] [--profile] [--passes N] [--mflops N] [--ops N] [--loop-cycles N] [--baseline-cycles N] [--baseline-instructions N] [--config path]";
    internal string JobFile { get; init; } = string.Empty;
    internal long Limit { get; init; }
    internal bool Verbose { get; init; }
    internal bool Trace { get; init; }
    internal string? ConfigPath { get; init; }
    internal string? RegistersFile { get; init; }
    internal string? MemoryDumpFile { get; init; }
    internal long MemoryDumpAt { get; init; }
    internal int MemoryDumpCount { get; init; }
    internal bool LoopDetect { get; init; }
    internal bool NoLoopDetect { get; init; }
    internal bool NoWallClock { get; init; }
    internal bool HangDetect { get; init; }
    internal bool NoHangDetect { get; init; }
    internal bool Profile { get; init; }
    internal bool Stats { get; init; }
    internal bool GuestClock { get; init; }
    internal ExecutionSpeed? SpeedOverride { get; init; }
    internal long ProfilePasses { get; init; }
    internal long ProfileBaselineCycles { get; init; }
    internal long ProfileBaselineInstructions { get; init; }
    internal long ProfileMflops { get; init; }
    internal long ProfileOps { get; init; }
    internal long ProfileLoopCycles { get; init; }

    internal static bool TryParse(string[] args, out RunOptions options, out string? error)
    {
        options = new();
        error = null;
        if (args.Length == 0) { error = "Usage: " + Usage; return false; }
        string jobFile = args[0];
        long limit = 0;
        bool verbose = false;
        bool trace = false;
        string? configPath = null;
        string? regsFile = null;
        string? memDumpFile = null;
        long memDumpAt = -1;
        int memDumpCount = 0;
        bool loopDetect = false;
        bool noLoopDetect = false;
        bool noWallClock = false;
        bool hangDetect = false;
        bool noHangDetect = false;
        bool profile = false;
        bool stats = false;
        bool guestClock = false;
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
                        error = "Error: --speed requires 'original' or 'max'.";
                        return false;
                    }
                    speedOverride = parsedSpeed;
                    break;
                case "--stats":
                    stats = true;
                    break;
                case "--guest-clock":
                    guestClock = true;
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

        options = new RunOptions
        {
            JobFile = jobFile,
            Limit = limit,
            Verbose = verbose,
            Trace = trace,
            ConfigPath = configPath,
            RegistersFile = regsFile,
            MemoryDumpFile = memDumpFile,
            MemoryDumpAt = memDumpAt,
            MemoryDumpCount = memDumpCount,
            LoopDetect = loopDetect,
            NoLoopDetect = noLoopDetect,
            NoWallClock = noWallClock,
            HangDetect = hangDetect,
            NoHangDetect = noHangDetect,
            Profile = profile,
            Stats = stats,
            GuestClock = guestClock,
            SpeedOverride = speedOverride,
            ProfilePasses = profilePasses,
            ProfileBaselineCycles = profileBaselineCycles,
            ProfileBaselineInstructions = profileBaselineInstructions,
            ProfileMflops = profileMflops,
            ProfileOps = profileOps,
            ProfileLoopCycles = profileLoopCycles,
        };
        return true;
    }

    internal Config LoadConfiguration()
    {
        var config = Config.Load(ConfigPath);
        if (SpeedOverride is ExecutionSpeed speed) config.Speed = speed;
        if (GuestClock) config.GuestClock = true;
        return config;
    }

    internal void ConfigureLoader(DubnaLoader loader, Config config)
    {
        loader.InstructionLimit = Limit == 0 ? config.DefaultLimit : Limit;
        loader.CollectStatistics = Stats;
        loader.Verbose = Verbose;
        loader.LoopDetect = LoopDetect && !NoLoopDetect;
        loader.HangDetect = HangDetect && !NoHangDetect;
        if (NoWallClock) loader.UseWallClock = false;
    }
}
