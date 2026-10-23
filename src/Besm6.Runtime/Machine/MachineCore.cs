using CpuProcessor = Besm6.Processor.Cpu.Processor;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Memory;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Machine;
using Besm6.Runtime.Timing.Arithmetic;
using Besm6.Runtime.Timing.Calendar;
using Besm6.Runtime.Timing.Devices;
using Besm6.Runtime.Timing.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Besm6.Runtime
{
    /// <summary>
    /// Главный класс симулятора БЭСМ-6.
    /// Объединяет все компоненты в единую систему.
    ///
    /// Функциональный исполнительный движок — <see cref="Processor"/> (точный порт
    /// dubna/processor.cpp). Устаревший конвейер ControlUnit/ArithmeticUnit
    /// удалён из активного пути. Память и устройства используются как
    /// инфраструктура для загрузки программ и I/O. Функциональная симуляция
    /// и физическая модель процессора изолированы, ресурсы машины общие.
    /// </summary>
    public partial class MachineCore
    {
        public IHostMemory Memory { get; }
        public ICommandMemory CommandMemory { get; }
        public MemoryModel MemoryModel { get; }
        /// <summary>Present only for the opt-in functional buffered memory backend.</summary>
        public BufferedMemoryBackend? BufferedMemory { get; }
        /// <summary>Mathematical memory, optionally coupled to the supervisor CPU profile.</summary>
        public MappedMemoryBackend? MappedMemory { get; }
        public CpuProcessor Cpu { get; }
        public DeviceManager Devices { get; }
        public Puncher Puncher { get; }
        public Plotter Plotter { get; }

        /// <summary>One successful functional CPU step advances one instruction tick.
        /// Hardware nanoseconds and table cycles are independent time domains.</summary>
        public const ulong TicksPerInstruction = 1;

        private readonly Execution.FunctionalProcessorSimulation _simulation;
        private readonly Execution.ProcessorExecutionOwnership _executionOwnership;
        private Modeling.HardwareProcessorModel? _hardwareModel;

        /// <summary>
        /// Internal staged АУ (арифметическое устройство) attached to this calendar.
        /// Explicit construction for the hardware owner; functional CPU execution
        /// neither creates it nor reads its state. CPU reset preserves it.
        /// </summary>
        internal ArithmeticUnitStages CreateArithmeticUnitStages(HardwareDuration cycle,
            ArithmeticErrorPolicy? errorPolicy = null)
        {
            if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(cycle), "A positive even cycle is required.");
            return HardwareModel.CreateArithmeticUnitStages(cycle, errorPolicy);
        }

        internal Execution.FunctionalProcessorSimulation Simulation => _simulation;
        internal Modeling.HardwareProcessorModel? ExistingHardwareModel => _hardwareModel;

        internal Modeling.HardwareProcessorModel HardwareModel
        {
            get
            {
                if (_hardwareModel is not null) return _hardwareModel;
                var model = new Modeling.HardwareProcessorModel(Cpu.PreparedExecution, MappedMemory?.PhysicalMemory, MappedMemory);
                _executionOwnership.Attach(model);
                return _hardwareModel = model;
            }
        }

        /// <summary>
        /// Lazily created nanosecond calendar owned by this machine. Explicit
        /// advancement only; instruction ticks and existing pacing are unchanged.
        /// CPU reset preserves this calendar and its pending operations.
        /// </summary>
        public Timing.HardwareTimeline HardwareTimeline => HardwareModel.Timeline;

        /// <summary>Functional instruction clock (read-only).</summary>
        public ISimulationClock Clock => _simulation.Clock;

        /// <summary>Functional event calendar, ordered by instruction ticks.</summary>
        public IEventScheduler Scheduler => _simulation.Scheduler;

        // Свойство-мост для совместимости с существующим Debugger.
        public CpuProcessor Processor => Cpu;

        public Action<int, ulong>? StepTrace
        { get => _simulation.StepTrace; set => _simulation.StepTrace = value; }

        public Action<string, ulong>? RegisterTrace
        { get => _simulation.RegisterTrace; set => _simulation.RegisterTrace = value; }

        public void BeginRegisterTrace() => _simulation.BeginRegisterTrace();

        public MachineCore(uint memorySize = 32768, string? puncherOutputDir = null)
            : this(MemoryModel.Dubna, memorySize, puncherOutputDir) { }

        public MachineCore(MemoryModel memoryModel, uint memorySize = 32768, string? puncherOutputDir = null, bool hostedBufferedMemory = false)
            : this(memoryModel, ProcessorProfile.Dubna, memorySize, puncherOutputDir, hostedBufferedMemory: hostedBufferedMemory) { }

        public MachineCore(ProcessorProfile profile, uint memorySize = 32768, string? puncherOutputDir = null)
            : this(profile == ProcessorProfile.Supervisor ? MemoryModel.Mapped : MemoryModel.Dubna,
                profile, memorySize, puncherOutputDir) { }

        /// <summary>Explicit geometry; Simh512K is an experimental SIMH compatibility
        /// configuration and does not claim historical bank wiring or timing.</summary>
        public MachineCore(ProcessorProfile profile, MemoryConfiguration configuration, string? puncherOutputDir = null)
            : this(profile == ProcessorProfile.Supervisor ? MemoryModel.Mapped : MemoryModel.Dubna,
                profile, ConfigurationSize(configuration), puncherOutputDir, configuration) { }

        private static uint ConfigurationSize(MemoryConfiguration configuration) => configuration switch
        {
            MemoryConfiguration.Classical32K => ArchitectureConstants.WordCount,
            MemoryConfiguration.Simh512K => 524288,
            _ => throw new ArgumentOutOfRangeException(nameof(configuration))
        };

        public SupervisorIoController? SupervisorIo { get; }

        private MachineCore(MemoryModel memoryModel, ProcessorProfile profile, uint memorySize, string? puncherOutputDir,
            MemoryConfiguration configuration = MemoryConfiguration.Classical32K, bool hostedBufferedMemory = false)
        {
            if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
            if (configuration != MemoryConfiguration.Classical32K && profile != ProcessorProfile.Supervisor)
                throw new ArgumentException("Expanded geometry requires the explicit supervisor profile.", nameof(configuration));
            IOperandMemory cpuMemory;
            IHostMemory hostMemory;
            ICommandMemory commandMemory;
            MemoryModel = memoryModel;
            switch (memoryModel)
            {
                case MemoryModel.Dubna:
                    var coreMemory = new CoreMemory(memorySize);
                    cpuMemory = coreMemory;
                    hostMemory = coreMemory;
                    commandMemory = coreMemory;
                    break;
                case MemoryModel.Buffered:
                    if (memorySize != ArchitectureConstants.WordCount)
                        throw new ArgumentOutOfRangeException(nameof(memorySize), "Buffered requires 32768 words.");
                    BufferedMemory = new BufferedMemoryBackend(hostedBufferedMemory);
                    cpuMemory = BufferedMemory;
                    hostMemory = BufferedMemory.HostMemory;
                    commandMemory = BufferedMemory;
                    break;
                case MemoryModel.Mapped:
                    if (memorySize != ConfigurationSize(configuration))
                        throw new ArgumentOutOfRangeException(nameof(memorySize), "Mapped capacity must match its explicit configuration.");
                    MappedMemory = new MappedMemoryBackend(configuration);
                    cpuMemory = MappedMemory;
                    hostMemory = MappedMemory.HostMemory;
                    commandMemory = MappedMemory;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(memoryModel));
            }
            Devices = new DeviceManager();

            // Регистрируем стандартные устройства.
            Devices.RegisterDevice(0x1000, new ConsoleDevice());
            Devices.RegisterDevice(0x2000, new DiskDevice("besm6_disk.bin"));
            Devices.RegisterDevice(0x3000, new MagneticDrumDevice("besm6_drum.bin"));
            Devices.RegisterDevice(0x4000, new TeletypeDevice(1));

            // Host access remains memory-only; guest command publication has its own role.
            var bus = new SystemBus(hostMemory, Devices);
            Memory = bus;
            CommandMemory = commandMemory;

            // The buffered backend separates CPU access from coherent host loading/observation.
            // Dubna still uses the original direct CoreMemory path without a bus hop.
            Cpu = new CpuProcessor(cpuMemory, profile);
            Puncher = new Puncher(Memory, puncherOutputDir);
            Plotter = new Plotter();

            // B1: планировщик событий привязан к тем же модельным часам машины.
            _simulation = new(Cpu.FunctionalExecution, Memory);
            _executionOwnership = new(Cpu.FunctionalExecution, _simulation);
            if (Cpu.Supervisor is { } supervisor)
            {
                SupervisorIo = new SupervisorIoController(MappedMemory!, _simulation.Scheduler, () => { });
                supervisor.Io = SupervisorIo;
            }
        }

        /// <summary>
        /// Compatibility alias for ResetCpu; memory, time, events and devices are preserved.
        /// </summary>
        public void Reset() => ResetCpu();

        /// <summary>Use the CPU model's shared nanosecond calendar and MRAM port for devices.
        /// CPU execution selection remains independent; device attachments and registers survive.</summary>
        public void UseHardwareDeviceTiming(MramPortConfiguration memory, SupervisorIoHardwareTiming devices)
        {
            ArgumentNullException.ThrowIfNull(devices);
            EnsureExecutionAllowed();
            if (Cpu.HasPreparedInstruction || ExistingHardwareModel is { IsDriving: true })
                throw new InvalidOperationException("Select device timing at an idle CPU boundary.");
            var io = SupervisorIo ?? throw new InvalidOperationException("Hardware device timing requires the supervisor configuration.");
            io.UseHardwareTiming(HardwareTimeline, HardwareModel.CreateMemoryPort(memory), devices);
        }

        public void UseFunctionalDeviceTiming()
        {
            EnsureExecutionAllowed();
            if (Cpu.HasPreparedInstruction || ExistingHardwareModel is { IsDriving: true })
                throw new InvalidOperationException("Select device timing at an idle CPU boundary.");
            (SupervisorIo ?? throw new InvalidOperationException("No supervisor devices are configured.")).UseFunctionalTiming();
        }

        /// <summary>Resets only the CPU. Memory, time, queued events and devices survive.</summary>
        public void ResetCpu() => Cpu.Reset();

        /// <summary>
        /// Загрузка программы из массива слов.
        /// </summary>
        public void LoadProgram(Word48[] program, int startAddress = 0)
        {
            for (int i = 0; i < program.Length; i++)
            {
                Memory.Write((uint)(startAddress + i), program[i]);
            }
            Cpu.StartAt((uint)startAddress);
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
            if (ExecutionModel == MachineExecutionModel.Functional) return _simulation.Step();
            var result = HardwareStep();
            if (result.Boundary is Modeling.HardwareRunBoundary.WaitingForSignal or Modeling.HardwareRunBoundary.TimeLimit)
                throw new InvalidOperationException($"Hardware step requires external progress: {result.Boundary}.");
            return result.Boundary == Modeling.HardwareRunBoundary.Stop;
        }

        internal bool Step(ref long completed) => _simulation.Step(ref completed);

        internal void EnsureExecutionAllowed()
        {
            if (ExecutionModel != MachineExecutionModel.Functional)
                throw new InvalidOperationException("This entry point belongs to functional execution.");
            _simulation.EnsureExecutionAllowed();
        }

        internal bool ExecuteBlock(int count, ref long instructionsExecuted, long cycleLimit = long.MaxValue) =>
            _simulation.ExecuteBlock(count, ref instructionsExecuted, cycleLimit);

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

        /// <summary>
        /// Bounded execution with shared pacing, without installing hosted extracodes.
        /// For the supervisor profile the limit also bounds step attempts, including
        /// fault delivery, preventing a faulting handler from running forever without
        /// completed commands. Failed attempts do not advance guest ticks or counters.
        /// </summary>
        public MachineExecutionResult RunInstructions(long instructionLimit,
            ExecutionSpeed speed = ExecutionSpeed.Max, bool collectStatistics = false,
            Action<long>? executionCheckpoint = null, long wallClockLimitMs = 0)
        {
            if (instructionLimit < 1) throw new ArgumentOutOfRangeException(nameof(instructionLimit));
            if (ExecutionModel == MachineExecutionModel.Hardware)
                return RunHardwareInstructions(instructionLimit, speed, collectStatistics, executionCheckpoint: executionCheckpoint, wallClockLimitMs: wallClockLimitMs);
            var loop = new ExecutionLoop(this)
            {
                InstructionLimit = instructionLimit,
                StepAttemptLimit = Cpu.Profile == ProcessorProfile.Supervisor ? instructionLimit : null,
                Speed = speed,
                CollectStatistics = collectStatistics,
                ExecutionCheckpoint = executionCheckpoint,
                WallClockLimitMs = wallClockLimitMs
            };
            var outcome = loop.Run();
            return new(outcome, loop.Statistics);
        }

        public override string ToString()
        {
            return $"MachineCore [K: {Cpu.K:X5}, A: 0x{Cpu.A:X12}]";
        }
    }
}
