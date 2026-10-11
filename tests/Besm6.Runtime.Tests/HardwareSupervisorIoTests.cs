using Besm6.Runtime.Timing;
using Besm6.Runtime.Modeling;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareSupervisorIoTests
{
    private static MramPortConfiguration PortTiming(MramReadSampling sampling = MramReadSampling.AtCycleStart) =>
        new(new(2000), new(900), new(1500), sampling, MramArbitration.FifoPerBank);
    private static SupervisorIoHardwareTiming DeviceTiming(ulong interval = 0, ulong exchange = 20000) =>
        new(new(exchange), new(700), new(900), new(100), new(50), new(500), new(30), new(interval));
    private static MachineCore Machine(ulong interval = 0, MramReadSampling sampling = MramReadSampling.AtCycleStart)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.UseHardwareDeviceTiming(PortTiming(sampling), DeviceTiming(interval));
        return machine;
    }
    private static MemoryWord50[] Image(int zones = 2) => Enumerable.Range(0, zones * SupervisorIoController.ZoneWords)
        .Select(i => new MemoryWord50(((ulong)i * 0x200000001UL) & MemoryWord50.Mask50)).ToArray();
    private static void Request(SupervisorIoController io, uint command, int zone = 0, int half = 0, int channel = 0)
    {
        io.WriteDevice((uint)(3 + channel), new(command));
        io.WriteDevice((uint)(19 + channel), new((ulong)(0x800 | zone << 1 | half)));
    }

    [TestMethod]
    [DataRow(true, false, 0, 0)] [DataRow(true, true, 0, 0)]
    [DataRow(false, false, 0, 1)] [DataRow(false, true, 0, 1)]
    [DataRow(true, false, 1, 0)] [DataRow(true, true, 1, 0)]
    [DataRow(false, false, 1, 1)] [DataRow(false, true, 1, 1)]
    public void DiskDmaMatchesFunctionalRawWordsAndAcknowledgesLastTransfer(bool page, bool systemOnly, int channel, int half)
    {
        var actual = Machine(); var expected = new MachineCore(ProcessorProfile.Supervisor);
        var image = Image(); uint command = (page ? 0x60000U : 0x20000U) | (systemOnly ? 0x100000U : 0) | 1U << 12;
        foreach (var machine in new[] { expected, actual })
        {
            machine.SupervisorIo!.AttachDisk(channel * 8, image);
            Request(machine.SupervisorIo, command, 1, half, channel);
        }
        expected.Scheduler.AdvanceTo(20);
        actual.HardwareTimeline.AdvanceTo(new(20000));
        Assert.AreEqual(0, actual.SupervisorIo!.CompletedTransfers);
        Assert.AreEqual(0UL, actual.SupervisorIo.ExternalInterrupts & (channel == 0 ? SupervisorIoController.Channel3Free : SupervisorIoController.Channel4Free));
        actual.HardwareTimeline.AdvanceTo(new(2000000));
        Assert.AreEqual(1, actual.SupervisorIo.CompletedTransfers);
        for (uint address = 0; address < PhysicalMemory.WordCount; address++)
            Assert.AreEqual(expected.MappedMemory!.PhysicalMemory.ReadRaw(address), actual.MappedMemory!.PhysicalMemory.ReadRaw(address));
        Assert.AreEqual(expected.SupervisorIo!.ExternalInterrupts, actual.SupervisorIo.ExternalInterrupts);
        Assert.AreEqual(0UL, actual.Clock.Tick);
    }

    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public void DrumExchangeSharesTimedMemoryAndPreservesParity(bool page, bool read)
    {
        var actual = Machine(); var expected = new MachineCore(ProcessorProfile.Supervisor); var image = Image(256);
        uint command = (page ? 0x40000U : 0) | (read ? 0x20000U : 0) | 2U << 12 | 9U;
        foreach (var machine in new[] { expected, actual })
        {
            machine.SupervisorIo!.AttachDrum(0, image);
            for (uint address = 8; address < 3080; address++) machine.MappedMemory!.PhysicalMemory.WriteRaw(address, new(address * 3UL));
            machine.SupervisorIo.WriteDevice(1, new(command));
        }
        expected.Scheduler.AdvanceTo(20); actual.HardwareTimeline.AdvanceTo(new(2000000));
        Assert.AreEqual(1, actual.SupervisorIo!.CompletedDrumTransfers);
        if (read)
            for (uint address = 0; address < 4096; address++) Assert.AreEqual(expected.MappedMemory!.PhysicalMemory.ReadRaw(address), actual.MappedMemory!.PhysicalMemory.ReadRaw(address));
        else CollectionAssert.AreEqual(expected.SupervisorIo!.SnapshotDrum(0), actual.SupervisorIo.SnapshotDrum(0));
    }

    [TestMethod]
    [DataRow(MramReadSampling.AtCycleStart)] [DataRow(MramReadSampling.AtDataReturn)]
    public void DeviceWritesSampleEachRawMemoryWordAtConfiguredPortBoundary(MramReadSampling sampling)
    {
        var machine = Machine(sampling: sampling); var io = machine.SupervisorIo!;
        io.AttachDisk(0, Image(), readOnly: false);
        var oldWord = new MemoryWord50(123); var newWord = new MemoryWord50(456);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(1024, oldWord);
        Request(io, 0x40000 | 1U << 12, 1);
        machine.HardwareTimeline.ScheduleAt(new(22400), () => machine.MappedMemory.PhysicalMemory.WriteRaw(1024, newWord));
        machine.HardwareTimeline.AdvanceTo(new(2000000));
        Assert.AreEqual(sampling == MramReadSampling.AtCycleStart ? oldWord : newWord, io.SnapshotDisk(0)[1040]);
        Assert.AreEqual(1, io.CompletedTransfers);
    }

    [TestMethod]
    public void HeaderReadyWaitsForItsFourWrites()
    {
        var actual = Machine(); var expected = new MachineCore(ProcessorProfile.Supervisor);
        foreach (var machine in new[] { expected, actual })
        { machine.SupervisorIo!.AttachDisk(0, Image()); machine.SupervisorIo.WriteDevice(19, new(7)); }
        expected.Scheduler.AdvanceTo(20); actual.HardwareTimeline.AdvanceTo(new(21499));
        Assert.AreEqual(0, actual.SupervisorIo!.CompletedHeaderReads);
        actual.HardwareTimeline.AdvanceTo(new(21500)); Assert.AreEqual(1, actual.SupervisorIo.CompletedHeaderReads);
        for (uint address = 24; address < 28; address++) Assert.AreEqual(expected.MappedMemory!.PhysicalMemory.ReadRaw(address), actual.MappedMemory!.PhysicalMemory.ReadRaw(address));
    }

    [TestMethod]
    public void DmaLeavesCpuBufferTagsAndContentsIntact()
    {
        var machine = Machine(); var memory = machine.MappedMemory!; var io = machine.SupervisorIo!;
        io.AttachDisk(0, Image()); memory.HostMemory.Write(1024, new(777)); memory.FetchInstruction(1024, false); memory.Write(1024, new(888));
        var operands = memory.GetOperandSnapshot(); var instructions = memory.GetInstructionSnapshot();
        Request(io, 0x60000 | 1U << 12); machine.HardwareTimeline.AdvanceTo(new(2000000));
        CollectionAssert.AreEqual(operands, memory.GetOperandSnapshot()); CollectionAssert.AreEqual(instructions, memory.GetInstructionSnapshot());
        Assert.AreEqual(888UL, memory.Read(1024).Value); Assert.AreEqual(777UL, memory.FetchInstruction(1024, false).Value);
        Assert.AreEqual(Image()[8], memory.PhysicalMemory.ReadRaw(1024));
    }

    [TestMethod]
    public void DmaAndCpuReadArbitrateThroughTheSameBankQueue()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor); machine.UseHardwareDeviceTiming(PortTiming(), DeviceTiming(exchange: 100));
        var port = machine.HardwareModel.CreateMemoryPort(PortTiming()); var image = Image();
        machine.MappedMemory!.PhysicalMemory.WriteRaw(1024, new(777)); machine.SupervisorIo!.AttachDisk(0, image);
        MramTransfer? cpuRead = null; port.Read(1024, reply => cpuRead = reply);
        Request(machine.SupervisorIo, 0x60000 | 1U << 12);
        machine.HardwareTimeline.AdvanceTo(new(5499)); Assert.AreEqual(777UL, machine.MappedMemory.PhysicalMemory.ReadRaw(1024).Data.Value);
        Assert.AreEqual(new HardwareInstant(900), cpuRead!.Value.DataReady); Assert.AreEqual(777UL, cpuRead.Value.Word.Data.Value);
        machine.HardwareTimeline.AdvanceTo(new(5500)); Assert.AreEqual(image[8], machine.MappedMemory.PhysicalMemory.ReadRaw(1024));
        Assert.AreEqual(0, machine.SupervisorIo.CompletedTransfers); machine.HardwareTimeline.AdvanceTo(new(2000000)); Assert.AreEqual(1, machine.SupervisorIo.CompletedTransfers);
    }

    [TestMethod]
    [DataRow(0UL)] [DataRow(3000UL)]
    public void CpuResetPreservesBurstAndSpacedDeviceRequests(ulong interval)
    {
        var machine = Machine(interval); var io = machine.SupervisorIo!; var image = Image(); io.AttachDisk(0, image);
        Request(io, 0x60000 | 1U << 12); machine.HardwareTimeline.AdvanceTo(new(22000)); machine.ResetCpu();
        Assert.ThrowsExactly<InvalidOperationException>(machine.UseFunctionalDeviceTiming);
        machine.HardwareTimeline.AdvanceTo(new(5000000)); Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(image[1031], machine.MappedMemory!.PhysicalMemory.ReadRaw(2047)); Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void SpacedDmaLetsCpuRequestEnterBetweenDeviceWords()
    {
        var machine = Machine(3000); var port = machine.HardwareModel.CreateMemoryPort(PortTiming());
        machine.SupervisorIo!.AttachDisk(0, Image()); Request(machine.SupervisorIo, 0x160000 | 1U << 12);
        MramTransfer? cpuRead = null;
        machine.HardwareTimeline.ScheduleAt(new(20500), () => port.Read(16, reply => cpuRead = reply)); // Same bank as first service word24.
        machine.HardwareTimeline.AdvanceTo(new(22900));
        Assert.AreEqual(new HardwareInstant(22000), cpuRead!.Value.Started); Assert.AreEqual(0, machine.SupervisorIo.CompletedTransfers);
        machine.HardwareTimeline.AdvanceTo(new(50000)); Assert.AreEqual(1, machine.SupervisorIo.CompletedTransfers);
    }

    [TestMethod]
    public void DevicePeriodsAreNanosecondsAndTickSettingsRemainIndependent()
    {
        var machine = Machine(); var io = machine.SupervisorIo!; io.CompletionDelayTicks = 99;
        io.WriteRegister(30, new(SupervisorIoController.TimerInterrupt | SupervisorIoController.SlowClockInterrupt)); io.StartHardwareTimer(true);
        Assert.ThrowsExactly<InvalidOperationException>(() => io.StartTimer(7));
        machine.HardwareTimeline.AdvanceTo(new(499)); Assert.AreEqual(0UL, io.PendingExternalInterrupts);
        machine.HardwareTimeline.AdvanceTo(new(500)); Assert.AreEqual(SupervisorIoController.TimerInterrupt, io.PendingExternalInterrupts);
        machine.HardwareTimeline.AdvanceTo(new(2000)); Assert.AreEqual(SupervisorIoController.TimerInterrupt | SupervisorIoController.SlowClockInterrupt, io.PendingExternalInterrupts);
        Assert.AreEqual(0UL, machine.Clock.Tick); io.StopTimer(); machine.UseFunctionalDeviceTiming();
        io.StartTimer(3); machine.Scheduler.AdvanceTo(3); Assert.AreEqual(3UL, machine.Clock.Tick); Assert.AreEqual(new HardwareInstant(2000), machine.HardwareTimeline.Now);
    }

    [TestMethod]
    public void PrinterAndPaperFeedHaveIndependentHardwarePeriods()
    {
        var machine = Machine(); var io = machine.SupervisorIo!; io.ConfigurePrinter(0); io.AttachPaperTape(0, new byte[] { 17 });
        io.PrinterPulseTicks = 999; io.PrinterZeroTicks = 999; io.CompletionDelayTicks = 999;
        io.WriteDevice(12, new(4)); io.WriteDevice(8, new(4)); io.WriteDevice(8, new(5));
        machine.HardwareTimeline.AdvanceTo(new(99)); Assert.AreEqual(0UL, io.ExternalInterrupts & (1UL << 47));
        machine.HardwareTimeline.AdvanceTo(new(100)); Assert.AreEqual(1UL << 47, io.ExternalInterrupts & (1UL << 47));
        machine.HardwareTimeline.AdvanceTo(new(900)); io.WriteDevice(8, new(5)); machine.HardwareTimeline.AdvanceTo(new(1800));
        Assert.AreEqual(17UL, io.ReadDevice(2060).Value); Assert.AreEqual(0UL, machine.Clock.Tick);
        io.WriteDevice(12, new(2)); machine.UseFunctionalDeviceTiming();
    }

    [TestMethod]
    public void ConsoleAcknowledgementUsesConfiguredDeadline()
    {
        var machine = Machine(); var io = machine.SupervisorIo!; io.ConfigureConsole(0); io.WriteDevice(24, Word48.Zero);
        io.WriteDevice(28, new(1U << 9)); io.WriteRegister(30, new(SupervisorIoController.PeripheralInterrupt)); io.WriteDevice(124, new(17));
        machine.HardwareTimeline.AdvanceTo(new(699)); Assert.AreEqual(0UL, io.PendingExternalInterrupts);
        machine.HardwareTimeline.AdvanceTo(new(700)); Assert.AreEqual(SupervisorIoController.PeripheralInterrupt, io.PendingExternalInterrupts);
        machine.UseFunctionalDeviceTiming(); Assert.IsFalse(io.UsesHardwareTiming);
    }

    [TestMethod]
    public void SerialClockKeepsNanosecondPeriodAcrossCpuResetAndStopsCleanly()
    {
        var machine = Machine(); var io = machine.SupervisorIo!; io.ConfigureSerialTerminal(1); io.ReceiveSerial(1, new byte[] { 65 });
        io.StartHardwareSerialClock(); Assert.ThrowsExactly<InvalidOperationException>(() => io.StartSerialClock(3)); machine.ResetCpu();
        machine.HardwareTimeline.AdvanceTo(new(31)); Assert.AreEqual(new HardwareInstant(60), machine.HardwareTimeline.NextEventTime);
        io.StopSerialClock(); Assert.IsNull(machine.HardwareTimeline.NextEventTime); machine.UseFunctionalDeviceTiming();
        io.StartSerialClock(2); machine.Scheduler.AdvanceTo(2); io.StopSerialClock(); Assert.AreEqual(2UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void CallbackFailureKeepsTheNextHardwareTimerPulse()
    {
        var machine = Machine(); bool fail = true; var cause = new ApplicationException("observer");
        var io = new SupervisorIoController(machine.MappedMemory!, machine.Scheduler, () => { if (fail) throw cause; });
        io.UseHardwareTiming(machine.HardwareTimeline, machine.HardwareModel.CreateMemoryPort(PortTiming()), DeviceTiming()); io.StartHardwareTimer();
        var error = Assert.ThrowsExactly<HardwareCallbackException>(() => machine.HardwareTimeline.AdvanceTo(new(500)));
        Assert.AreSame(cause, error.InnerException); Assert.AreEqual(new HardwareInstant(1000), machine.HardwareTimeline.NextEventTime);
        fail = false; machine.HardwareTimeline.AdvanceTo(new(1000)); io.StopTimer(); io.UseFunctionalTiming();
    }

    [TestMethod]
    public void HardwareCallbackCannotSwitchCalendarsOrRunCpu()
    {
        var machine = Machine(); bool checkedGuard = false;
        machine.HardwareTimeline.ScheduleAt(new(100), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(machine.UseFunctionalDeviceTiming);
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step()); checkedGuard = true;
        });
        machine.HardwareTimeline.AdvanceTo(new(100)); Assert.IsTrue(checkedGuard);
    }

    [TestMethod]
    public void WrongPhysicalStoreAndCalendarAreRejectedBeforeSwitch()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor); var other = new PhysicalMemory(); var timeline = new HardwareTimeline();
        var port = new MramMemoryPort(other, timeline, PortTiming());
        Assert.ThrowsExactly<ArgumentException>(() => machine.SupervisorIo!.UseHardwareTiming(timeline, port, DeviceTiming()));
        Assert.IsFalse(machine.SupervisorIo!.UsesHardwareTiming);
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.SupervisorIo.StartHardwareTimer());
    }

    [TestMethod]
    public void AbortedSpacedRegistrationPreservesAnAcceptedWriteAndDoesNotAcknowledge()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 3000)); var memory = new PhysicalMemory();
        var port = new MramMemoryPort(memory, timeline, PortTiming()); var dma = new TimedSupervisorDma(port, new(500));
        bool completed = false, aborted = false;
        dma.Transfer(true, [new(123), new(456), new(789)], [new(8,0), new(16,1), new(24,2)], () => completed = true, () => aborted = true);
        var error = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(ulong.MaxValue - 2500)));
        Assert.IsInstanceOfType<OverflowException>(error.InnerException); Assert.IsTrue(aborted); Assert.IsFalse(completed); Assert.AreEqual(0, dma.PendingTransfers);
        timeline.AdvanceTo(new(ulong.MaxValue - 1400)); Assert.AreEqual(new MemoryWord50(123), memory.ReadRaw(8)); Assert.AreEqual(default(MemoryWord50), memory.ReadRaw(16));
        Assert.AreEqual(0, port.PendingRequests);
    }

    [TestMethod]
    public void HardwareTimerInterruptIsAcceptedBySharedCpuAtNextFetch()
    {
        var machine = Machine(); var cpu = machine.Cpu; cpu.Supervisor!.Status = new(ControlUnitFlags.AssignmentBlocked | ControlUnitFlags.ProtectionBlocked);
        machine.Memory.Write(8, new(Besm6.Asm.Assembler.Asm("vtm 7(1), vtm 3(1)")));
        machine.Memory.Write(0x141, new(Besm6.Asm.Assembler.Asm("vtm 11(2), stop"))); cpu.StartAt(8);
        machine.SupervisorIo!.WriteRegister(30, new(SupervisorIoController.TimerInterrupt));
        var controls = new Dictionary<Opcode, HardwareDuration> { [Opcode.Vtm] = new(300), [Opcode.Stop] = new(300) };
        var model = machine.HardwareModel; model.ConfigureAutomaticExecution(new(PortTiming(), new(100), new(100), controls));
        machine.HardwareTimeline.ScheduleAt(new(1100), () => machine.SupervisorIo.StartHardwareTimer());
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, model.AdvanceAutomaticTo(new(1200), 1).Boundary); Assert.AreEqual(7U, cpu.GetM(1));
        machine.HardwareTimeline.AdvanceTo(new(1600)); machine.SupervisorIo.StopTimer();
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, model.AdvanceAutomaticTo(new(10000), 1).Boundary);
        Assert.AreEqual(7U, cpu.GetM(1)); Assert.AreEqual(9U, cpu.GetM(2)); Assert.AreEqual(0x141U, cpu.GetK()); Assert.AreEqual(0UL, machine.Clock.Tick);
    }
    [TestMethod]
    [DataRow(0U)] [DataRow(31U)]
    public void TimedDiskDmaIncludesPhysicalZeroAndLastPage(uint page)
    {
        var machine = Machine(); var image = Image(); machine.SupervisorIo!.AttachDisk(0, image);
        Request(machine.SupervisorIo, 0x60000 | page << 12, 1); machine.HardwareTimeline.AdvanceTo(new(2000000));
        Assert.AreEqual(image[1040], machine.MappedMemory!.PhysicalMemory.ReadRaw(page * 1024));
        Assert.AreEqual(image[2063], machine.MappedMemory.PhysicalMemory.ReadRaw(page * 1024 + 1023));
        Assert.AreEqual(1, machine.SupervisorIo.CompletedTransfers);
    }

    [TestMethod]
    public void AbortedAcceptedReadDoesNotPublishIntoDeviceImage()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 3000)); var memory = new PhysicalMemory();
        memory.WriteRaw(8, new(999)); var media = new MemoryWord50[] { new(123), new(456), new(789) };
        var port = new MramMemoryPort(memory, timeline, PortTiming()); var dma = new TimedSupervisorDma(port, new(500));
        bool aborted = false; dma.Transfer(false, media, [new(8,0), new(16,1), new(24,2)], () => Assert.Fail("No completion on abort"), () => aborted = true);
        Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(ulong.MaxValue - 2500)));
        timeline.AdvanceTo(new(ulong.MaxValue - 1000)); Assert.IsTrue(aborted);
        Assert.AreEqual(new MemoryWord50(123), media[0]); Assert.AreEqual(new MemoryWord50(999), memory.ReadRaw(8));
        Assert.AreEqual(0, port.PendingRequests); Assert.AreEqual(0, dma.PendingTransfers);
    }

    [TestMethod]
    public void DmaObserverFailureLeavesCompletedTransferAndNoGuestAvost()
    {
        var machine = Machine(); bool fail = false; var cause = new ApplicationException("DMA observer");
        var io = new SupervisorIoController(machine.MappedMemory!, machine.Scheduler, () => { if (fail) throw cause; });
        io.UseHardwareTiming(machine.HardwareTimeline, machine.HardwareModel.CreateMemoryPort(PortTiming()), DeviceTiming());
        var image = Image(); io.AttachDisk(0, image); Request(io, 0x160000 | 1U << 12); fail = true;
        var error = Assert.ThrowsExactly<HardwareCallbackException>(() => machine.HardwareTimeline.AdvanceTo(new(21500)));
        Assert.AreSame(cause, error.InnerException); Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(image[0], machine.MappedMemory!.PhysicalMemory.ReadRaw(24));
        Assert.AreEqual(SupervisorIoController.Channel3Free, io.ExternalInterrupts & SupervisorIoController.Channel3Free);
        Assert.AreEqual(0UL, machine.Cpu.Supervisor!.InternalInterrupts); fail = false; io.UseFunctionalTiming();
        io.AttachDisk(0, image); // Terminal pending identity permits replacement.
    }

    [TestMethod]
    public void FunctionalTimerAlsoKeepsItsNextPulseAfterObserverFailure()
    {
        var memory = new MappedMemoryBackend(); var scheduler = new EventScheduler(); bool fail = true;
        var cause = new ApplicationException("timer observer");
        var io = new SupervisorIoController(memory, scheduler, () => { if (fail) throw cause; }); io.StartTimer(5);
        var error = Assert.ThrowsExactly<SchedulerCallbackException>(() => scheduler.AdvanceTo(5));
        Assert.AreSame(cause, error.InnerException); Assert.AreEqual(10UL, scheduler.NextEventTick);
        fail = false; scheduler.AdvanceTo(10); io.StopTimer(); Assert.IsNull(scheduler.NextEventTick);
    }

}
