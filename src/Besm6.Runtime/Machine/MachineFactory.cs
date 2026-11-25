using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;

namespace Besm6.Runtime.Machine
{
    /// <summary>
    /// Фабрика создания симулятора БЭСМ-6 из конфигурации.
    /// </summary>
    public static class MachineFactory
    {
        /// <summary>
        /// Создать MachineCore из конфигурации.
        /// </summary>
        public static MachineCore CreateMachine(Config? cfg = null)
        {
            cfg ??= Config.Load();
            int memSize = cfg.MemorySize;
            if (cfg.SupervisorHostedServices)
            {
                if (cfg.BufferedMemory || memSize != 32768)
                    throw new ArgumentException("Hosted supervisor jobs require a 32768-word mathematical space and cannot select the Dubna buffered backend.");
                var hosted = MachineCore.CreateHostedSupervisor();
                hosted.Cpu.MemoryWatchpointBatchingEnabled = cfg.MemoryWatchpointBatching;
                return hosted;
            }
            var machine = new MachineCore(cfg.BufferedMemory ? MemoryModel.Buffered : MemoryModel.Dubna, (uint)memSize,
                hostedBufferedMemory: cfg.BufferedMemory);
            machine.Cpu.MemoryWatchpointBatchingEnabled = cfg.MemoryWatchpointBatching;
            return machine;
        }

        /// <summary>
        /// Создать DubnaLoader из конфигурации.
        /// </summary>
        public static DubnaLoader CreateLoader(Config? cfg = null, MachineCore? machine = null,
            ResolvedRuntimeAssets? runtimeAssets = null)
        {
            cfg ??= Config.Load();
            machine ??= CreateMachine(cfg);

            runtimeAssets ??= ValidateRuntimeAssets(cfg);

            // Apply the policy to caller-supplied machines as well as factory instances.
            machine.Cpu.MemoryWatchpointBatchingEnabled = cfg.MemoryWatchpointBatching;

            return new DubnaLoader(machine, runtimeAssets.TapesDir)
            {
                InstructionLimit = cfg.DefaultLimit,
                UseWallClock = cfg.UseWallClock,
                Speed = cfg.Speed,
                GuestClockEnabled = cfg.GuestClock,
                OriginalTimeFactor = cfg.OriginalTimeFactor,
                OriginalMathFactor = cfg.OriginalMathFactor,
                ForexAsFortran = cfg.ForexAsFortran,
            };
        }

        /// <summary>Single construction path for hosted runs and native tools. Settings are
        /// applied to the supplied Config before assets and the machine are created.</summary>
        public static MachineRunSession CreateRunSession(Config config, RunSessionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(settings);
            if (settings.InstructionLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(settings.InstructionLimit));

            config.Speed = settings.Speed;
            config.DefaultLimit = settings.InstructionLimit;
            config.GuestClock = settings.GuestClock;
            config.BufferedMemory = settings.BufferedMemory;
            config.SupervisorHostedServices = settings.SupervisorHostedServices;
            if (settings.TapesDirectory is { } tapes) config.Tapes = tapes;
            if (settings.DiskPath is { } disk) config.Disk = disk;
            if (settings.DrumPath is { } drum) config.Drum = drum;

            var assets = ValidateRuntimeAssets(config);
            var machine = CreateMachine(config);
            var loader = CreateLoader(config, machine, assets);
            loader.InstructionLimit = settings.InstructionLimit;
            loader.CollectStatistics = settings.CollectStatistics;
            loader.WallClockLimitMs = settings.WallClockLimitMs;
            if (settings.UseWallClock is { } useWallClock) loader.UseWallClock = useWallClock;
            if (settings.HostFileDirectory is { } hostDirectory) loader.HostFileDirectory = hostDirectory;
            return new(machine, loader);
        }

        /// <summary>
        /// Проверить runtime-ресурсы ДО запуска процессора.
        /// Возвращает разрешённые пути; при отсутствии/несоответствии checksum бросает
        /// <see cref="RuntimeAssetsException"/> со списком всех проблемных ресурсов и каталогов.
        /// </summary>
        public static ResolvedRuntimeAssets ValidateRuntimeAssets(Config? cfg = null,
            IReadOnlyList<RuntimeAsset>? required = null)
        {
            cfg ??= Config.Load();
            return RuntimeAssets.Resolve(cfg, required);
        }
    }
}
