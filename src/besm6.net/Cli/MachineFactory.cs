using Besm6.Core;
using Besm6.Loader;

namespace Besm6
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
            return new MachineCore((uint)memSize);
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

            return new DubnaLoader(machine, runtimeAssets.TapesDir)
            {
                InstructionLimit = cfg.DefaultLimit,
                UseWallClock = cfg.UseWallClock,
            };
        }

        /// <summary>
        /// Проверить runtime-ресурсы ДО запуска процессора (SuperPlan Task A4).
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
