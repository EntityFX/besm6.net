using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class MachineDeviceConfigurationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void StartupClocksUseTheirSelectedCalendarAndPanelsAreSeparateFromPhysicalStorage(bool hardware)
    {
        var job = Job(hardware);
        job.MemoryWords.Add(new() { Address = 2, Panel = true, Raw50 = MemoryWord50.Form(new(123), true, true).RawValue });
        job.MemoryWords.Add(new() { Address = 2, Raw50 = MemoryWord50.Form(new(456), true, true).RawValue });
        job.Devices = new() { StartTimer = true, IncludeSlowClock = true, StartSerialClock = true,
            TimerPeriodTicks = hardware ? 0UL : 10UL, SerialPeriodTicks = hardware ? 0UL : 5UL,
            ScratchDrums = new() { 0, 1 }, SerialTerminals = new() { 1 }, Printers = new() { 0 } };
        var machine = job.CreateMachine();
        Assert.AreEqual(123UL, machine.MappedMemory!.GetPanel(2).Data.Value);
        Assert.AreEqual(456UL, machine.MappedMemory.PhysicalMemory.ReadRaw(2).Data.Value);
        Assert.AreEqual(hardware, machine.SupervisorIo!.UsesHardwareTiming);
        var before = machine.MappedMemory.GetPanel(2);
        machine.ResetCpu();
        Assert.AreEqual(before, machine.MappedMemory.GetPanel(2));
        Assert.AreEqual(hardware ? MachineExecutionModel.Hardware : MachineExecutionModel.Functional, machine.ExecutionModel);
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.UseFunctionalExecution());
        machine.SupervisorIo.StopTimer(); machine.SupervisorIo.StopSerialClock();
        machine.UseFunctionalExecution();
        Assert.IsFalse(machine.SupervisorIo.UsesHardwareTiming);
    }

    [TestMethod]
    public void RelativeAttachmentsAreVerifiedAndImagesRemainLocalMemoryCopies()
    {
        string directory = Path.Combine(Path.GetTempPath(), "besm6-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] bytes = new byte[SupervisorIoController.ZoneWords * 8];
            for (int i = 0; i < SupervisorIoController.ZoneWords; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8, 8), 2UL << 48);
            File.WriteAllBytes(Path.Combine(directory, "numeric.bin"), bytes);
            File.WriteAllText(Path.Combine(directory, "input.txt"), "*NAME TEST\n*END FILE\n");
            var job = Job(false);
            job.Devices = new()
            {
                DiskImages = new() { new() { Unit = 0, File = "numeric.bin", Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), ReadOnly = false } },
                PaperTapes = new() { new() { Reader = 0, File = "input.txt", Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "input.txt")))) } }
            };
            string path = Path.Combine(directory, "job.json");
            File.WriteAllText(path, JsonSerializer.Serialize(job));
            var loaded = MachineJobConfiguration.Load(path);
            var first = loaded.CreateMachine(); var second = loaded.CreateMachine();
            Assert.AreEqual(SupervisorIoController.ZoneWords, first.SupervisorIo!.SnapshotDisk(0).Length);
            CollectionAssert.AreEqual(first.SupervisorIo.SnapshotDisk(0), second.SupervisorIo!.SnapshotDisk(0));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(directory, "numeric.bin")));
            loaded.Devices!.DiskImages[0].Sha256 = new string('0', 64);
            Assert.ThrowsExactly<ArgumentException>(() => loaded.CreateMachine());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void InvalidUnitsClocksDuplicatesAndPanelAddressesFailBeforeExecution()
    {
        foreach (var devices in new MachineDeviceConfiguration[]
        {
            new() { ScratchDrums = new() { 0, 0 } }, new() { Printers = new() { 2 } },
            new() { SerialTerminals = new() { 0 } }, new() { ScratchDisks = new() { new() { Unit = 16 } } },
            new() { ScratchDisks = new() { new() { Unit = 0 }, new() { Unit = 0 } } },
            new() { StartTimer = true }, new() { StartSerialClock = true }
        })
        {
            var job = Job(false); job.Devices = devices;
            Assert.ThrowsExactly<ArgumentException>(() => job.CreateMachine());
        }
        var hardware = Job(true); hardware.Devices = new() { TimerPeriodTicks = 1 };
        Assert.ThrowsExactly<ArgumentException>(() => hardware.CreateMachine());
        var panel = Job(false); panel.MemoryWords.Add(new() { Address = 8, Panel = true });
        Assert.ThrowsExactly<ArgumentException>(() => panel.CreateMachine());
    }

    [TestMethod]
    public void AdmittedDevicePlanIsIndependentOfMutableDto()
    {
        var devices = new MachineDeviceConfiguration { StartTimer = true, TimerPeriodTicks = 10 };
        var admit = devices.Prepare(Directory.GetCurrentDirectory(), MachineExecutionModel.Functional);
        devices.StartTimer = false; devices.TimerPeriodTicks = 0;
        var machine = Job(false).CreateMachine(); admit(machine.SupervisorIo!);
        machine.Scheduler.AdvanceTo(10);
        Assert.AreNotEqual(0UL, machine.SupervisorIo!.ExternalInterrupts & SupervisorIoController.TimerInterrupt);
    }

    private static MachineJobConfiguration Job(bool hardware) => new()
    {
        ExecutionModel = hardware ? "hardware" : "functional",
        MemoryWords = new() { new() { Address = 8, Raw50 = MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm("stop, stop")), false, false).RawValue } },
        Timing = new()
        {
            BankCycleNs = 2000, ReadLatencyNs = 900, WriteVisibilityNs = 1500,
            BufferHitNs = 100, ArithmeticCycleNs = 100, ControlDurationsNs = new() { ["Stop"] = 300 },
            Devices = new() { ExchangeReadyNs = 20000, ConsoleReplyNs = 700, PaperFeedNs = 900,
                PrinterCharacterNs = 100, PrinterZeroNs = 50, TimerPeriodNs = 500, SerialPeriodNs = 30 }
        }
    };

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void HardwareIoDecoratorMustIdentifyTheActualSharedBus(bool foreign)
    {
        var job = Job(true);
        job.MemoryWords[0].Raw50 = MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm("ext 4003, stop")), false, false).RawValue;
        job.Timing!.ControlDurationsNs["Ext"] = 300;
        var machine = job.CreateMachine();
        var controller = foreign ? Job(false).CreateMachine().SupervisorIo! : machine.SupervisorIo!;
        var decorator = new Decorator(controller); machine.Cpu.Supervisor!.Io = decorator;
        if (foreign) Assert.ThrowsExactly<InvalidOperationException>(() => machine.RunInstructions(10));
        else
        {
            Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped);
            Assert.AreEqual(1, decorator.Calls);
            Assert.AreEqual(0UL, machine.Clock.Tick);
        }
    }

    private sealed class Decorator(SupervisorIoController inner) : IHardwareSupervisorIo
    {
        public SupervisorIoController HardwareController => inner;
        public int Calls { get; private set; }
        public ulong PendingExternalInterrupts => inner.PendingExternalInterrupts;
        public Word48 ReadRegister(uint register) { Calls++; return inner.ReadRegister(register); }
        public void WriteRegister(uint register, Word48 value) { Calls++; inner.WriteRegister(register, value); }
        public Word48 ReadDevice(uint port) { Calls++; return inner.ReadDevice(port); }
        public void WriteDevice(uint port, Word48 value) { Calls++; inner.WriteDevice(port, value); }
    }
}
