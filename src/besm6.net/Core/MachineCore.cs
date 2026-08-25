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

        // Свойство-мост для совместимости с существующим Debugger.
        public Processor Processor => Cpu;

        /// <summary>Хук трассировки: вызывается после каждой инструкции. null = трассировка выключена.</summary>
        public Action<int, long>? StepTrace { get; set; }

        public MachineCore(int memorySize = 32768, string? puncherOutputDir = null)
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
                Memory.Write(startAddress + i, program[i]);
            }
            Cpu.SetPc(startAddress);
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
                long val = BitConverter.ToInt64(data, i * 8);
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
            if (StepTrace != null)
            {
                int pc = (int)Cpu.GetPc();
                StepTrace(pc, Memory.Read(pc).Value);
            }
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