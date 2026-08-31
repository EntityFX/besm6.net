using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Besm6.Core
{
    /// <summary>
    /// Главный класс симулятора БЭСМ-6.
    /// Объединяет все компоненты в единую систему.
    ///
    /// Единственный исполнительный движок — <see cref="Processor"/> (точный порт
    /// dubna/processor.cpp). Устаревший конвейер ControlUnit/ArithmeticUnit
    /// удалён из активного пути. Память и устройства используются как
    /// инфраструктура для загрузки программ и I/O.
    /// </summary>
    public class MachineCore
    {
        public IMemory Memory { get; }
        public Processor Cpu { get; }
        public DeviceManager Devices { get; }
        public Puncher Puncher { get; }
        public Plotter Plotter { get; }

        // ── Уровень B (SuperPlan Task B1): дискретное модельное время ──
        // Часы и планировщик привязаны к одному источнику модельного времени.
        // Шаг инструкции стоит TicksPerInstruction тиков. Это НЕ изменяет наблюдаемую
        // семантику уровня A (CPU-путь не трогается) — изолированный эксперимент
        // по правилу SuperPlan: «до закрытия Gate A задачи уровня B — только
        // изолированные эксперименты и не заменяют текущий исполнительный путь».
        /// <summary>Документированная стоимость одной CPU-инструкции в модельных тиках.</summary>
        public const ulong TicksPerInstruction = 1;

        private readonly SimulationClock _clock = new();
        private readonly EventScheduler _scheduler;

        /// <summary>Модельные часы (read-only вид уровня B).</summary>
        public ISimulationClock Clock => _clock;

        /// <summary>Планировщик событий модельного времени (уровень B).</summary>
        public IEventScheduler Scheduler => _scheduler;

        // Свойство-мост для совместимости с существующим Debugger.
        public Processor Processor => Cpu;

        /// <summary>Хук трассировки: вызывается после каждой инструкции. null = трассировка выключена.</summary>
        public Action<int, ulong>? StepTrace { get; set; }

        /// <summary>
        /// Хук трассировки ИЗМЕНЕНИЙ регистров после каждого шага — точный аналог
        /// регистра ("ACC", "RMR", "M0".."M15", "RAU", "MOD" или "CLEARMOD") и его
        /// значением. Печатает только изменённые регистры (сравнение с prev-состоянием),
        /// </summary>
        public Action<string, ulong>? RegisterTrace { get; set; }

        private bool _rtActive;
        private ulong _rtAcc, _rtRmr, _rtRau;
        private uint _rtMod;
        private uint[] _rtM = new uint[16];
        private bool _rtApply;

        /// <summary>Зафиксировать текущее состояние как базу сравнения (вызывать до цикла шагов).</summary>
        public void BeginRegisterTrace()
        {
            _rtActive = true;
            _rtAcc = Cpu.GetAcc().Value;
            _rtRmr = Cpu.GetRmr().Value;
            _rtRau = Cpu.GetRau();
            _rtMod = (uint)Cpu.Mod;
            _rtApply = Cpu.ApplyModReg;
            for (int i = 0; i < 16; i++) _rtM[i] = Cpu.GetM(i);
        }

        private void EmitRegisterTrace()
        {
            var sink = RegisterTrace;
            if (sink == null) return;
            if (!_rtActive) { BeginRegisterTrace(); return; }
            ulong acc = Cpu.GetAcc().Value;
            ulong rmr = Cpu.GetRmr().Value;
            uint rau = Cpu.GetRau();
            uint mod = (uint)Cpu.Mod;
            bool apply = Cpu.ApplyModReg;
            if (acc != _rtAcc) sink("ACC", acc);
            if (rmr != _rtRmr) sink("RMR", rmr);
            for (int i = 0; i < 16; i++)
            {
                uint v = Cpu.GetM(i);
                if (v != _rtM[i]) sink("M" + Convert.ToString(i, 8), v);
            }
            if (rau != _rtRau) sink("RAU", rau);
            if (apply != _rtApply) sink(apply ? "MOD" : "CLEARMOD", mod);
            // Обновить prev-состояние.
            _rtAcc = acc; _rtRmr = rmr; _rtRau = rau; _rtMod = mod; _rtApply = apply;
            for (int i = 0; i < 16; i++) _rtM[i] = Cpu.GetM(i);
        }

        public MachineCore(uint memorySize = 32768, string? puncherOutputDir = null)
        {
            var coreMemory = new CoreMemory(memorySize);
            Devices = new DeviceManager();

            // Регистрируем стандартные устройства.
            Devices.RegisterDevice(0x1000, new ConsoleDevice());
            Devices.RegisterDevice(0x2000, new DiskDevice("besm6_disk.bin"));
            Devices.RegisterDevice(0x3000, new MagneticDrumDevice("besm6_drum.bin"));
            Devices.RegisterDevice(0x4000, new TeletypeDevice(1));

            // Используем системную шину для маршрутизации между памятью и устройствами.
            Memory = new SystemBus(coreMemory, Devices);

            Cpu = new Processor(Memory);
            Puncher = new Puncher(Memory, puncherOutputDir);
            Plotter = new Plotter();

            // B1: планировщик событий привязан к тем же модельным часам машины.
            _scheduler = new EventScheduler(_clock);
        }

        /// <summary>
        /// Сброс состояния машины в начальное.
        /// </summary>
        public void Reset()
        {
            Cpu.Reset();
        }

        /// <summary>
        /// Загрузка программы из массива слов.
        /// </summary>
        public void LoadProgram(Word48[] program, int startAddress = 0)
        {
            for (int i = 0; i < program.Length; i++)
            {
                Memory.Write((uint)(startAddress + i), program[i]);
            }
            Cpu.SetPc((uint)startAddress);
        }

        /// <summary>
        /// Загрузка программы из бинарного файла.
        /// Ожидается файл, где каждое слово представлено как 8-байтовое число (long).
        /// </summary>
        public void LoadBinary(string path, int startAddress = 0)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Binary file not found: {path}");

            byte[] data = File.ReadAllBytes(path);
            int wordCount = data.Length / 8;
            Word48[] program = new Word48[wordCount];

            for (int i = 0; i < wordCount; i++)
            {
                ulong val = BitConverter.ToUInt64(data, i * 8);
                program[i] = new Word48(val);
            }

            LoadProgram(program, startAddress);
        }

        /// <summary>
        /// Выполнение одной инструкции.
        /// Возвращает true, когда процессор остановлен (команда СТОП).
        /// </summary>
        public bool Step()
        {
            bool stopped = Cpu.Step();
            // B1: одна выполненная инструкция = TicksPerInstruction тиков модельного времени.
            // Не влияет на наблюдаемую семантику уровня A (только учёт модельного времени).
            _clock.Advance(TicksPerInstruction);
            if (StepTrace != null)
            {
                int pc = (int)Cpu.GetPc();
                StepTrace(pc, Memory.Read((uint)pc).Value);
            }
            EmitRegisterTrace();
            return stopped;
        }

        /// <summary>
        /// Запуск машины до достижения условия остановки.
        /// Останавливается по команде СТОП либо по внешнему условию.
        /// </summary>
        public void Run(Action<MachineCore>? breakCondition = null)
        {
            for (;;)
            {
                if (Step())
                    break;
                breakCondition?.Invoke(this);
            }
        }

        public override string ToString()
        {
            return $"MachineCore [PC: {Cpu.PC:X5}, Acc: 0x{Cpu.Acc:X12}]";
        }
    }
}