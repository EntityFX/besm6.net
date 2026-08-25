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
        public static DubnaLoader CreateLoader(Config? cfg = null, MachineCore? machine = null)
        {
            cfg ??= Config.Load();
            machine ??= CreateMachine(cfg);

            string? tapesDir = cfg.Tapes != null ? cfg.ResolvePath(cfg.Tapes) : null;

            return new DubnaLoader(machine, tapesDir)
            {
                InstructionLimit = cfg.DefaultLimit,
            };
        }
    }
}