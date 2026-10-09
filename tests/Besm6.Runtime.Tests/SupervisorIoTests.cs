using System.Buffers.Binary;

namespace Besm6.Tests;

[TestClass]
public sealed class SupervisorIoTests
{
    [TestMethod]
    public void PrinterEventsHaveIndependentConfigurablePeriodFromDma()
    {
        var (_, scheduler, io) = Create();
        io.ConfigurePrinter(0);
        io.PrinterPulseTicks = 100;
        io.PrinterZeroTicks = 50;
        Assert.AreEqual(20UL, io.CompletionDelayTicks);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => io.PrinterPulseTicks = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => io.PrinterZeroTicks = 0);
        io.WriteDevice(12, new(4));
        scheduler.AdvanceTo(99);
        Assert.AreEqual(0UL, io.ExternalInterrupts & (1UL << 47));
        scheduler.AdvanceTo(100);
        Assert.AreEqual(1UL << 47, io.ExternalInterrupts & (1UL << 47));
    }
    [TestMethod]
    public void RepeatedPaperFeedRetainsOnePendingRowAndMotorOffDoesNotFeed()
    {
        var (_, scheduler, io) = Create();
        io.AttachPaperTape(0, new byte[] { 17, 29 });
        io.WriteDevice(8, new(5));
        scheduler.AdvanceTo(20);
        Assert.AreEqual(0UL, io.ReadDevice(2060).Value);
        io.WriteDevice(8, new(4));
        io.WriteDevice(8, new(5));
        io.WriteDevice(8, new(5));
        scheduler.AdvanceTo(40); // Starting row; data has not advanced.
        Assert.AreEqual(0UL, io.ReadDevice(2060).Value);
        io.WriteDevice(8, new(5));
        io.WriteDevice(8, new(5));
        scheduler.AdvanceTo(60);
        Assert.AreEqual(17UL, io.ReadDevice(2060).Value);
        io.WriteDevice(8, new(5));
        scheduler.AdvanceTo(80);
        Assert.AreEqual(29UL, io.ReadDevice(2060).Value);
    }
    [TestMethod]
    public void UnknownSimhPollIsExplicitlyRestrictedToExperimentalConfiguration()
    {
        var classical = Create();
        Assert.ThrowsExactly<NotSupportedException>(() => classical.Io.ReadDevice(2125));
        var memory = new MappedMemoryBackend(MemoryConfiguration.Simh512K);
        var scheduler = new EventScheduler();
        var io = new SupervisorIoController(memory, scheduler, () => { });
        io.RaiseInterrupts(123);
        var flags = io.ExternalInterrupts;
        Assert.AreEqual(Word48.Zero, io.ReadDevice(2125));
        Assert.AreEqual(flags, io.ExternalInterrupts);
        Assert.AreEqual(0, io.CompletedTransfers);
        Assert.ThrowsExactly<NotSupportedException>(() => io.ReadDevice(2126));
    }
    private static (MappedMemoryBackend Memory, EventScheduler Scheduler, SupervisorIoController Io) Create()
    {
        var memory = new MappedMemoryBackend();
        var scheduler = new EventScheduler();
        return (memory, scheduler, new(memory, scheduler, () => { }));
    }

    private static MemoryWord50[] Image(int zones = 2)
        => Enumerable.Range(0, zones * SupervisorIoController.ZoneWords)
            .Select(i => MemoryWord50.Form(new((ulong)i), (i & 1) != 0, (i & 1) != 0)).ToArray();

    private static void Request(SupervisorIoController io, uint command, int zone, int half = 0, int controller = 0)
    {
        io.WriteDevice((uint)(3 + controller), new(command));
        io.WriteDevice((uint)(19 + controller), new((ulong)(0x800 | (zone << 1) | half)));
    }

    [TestMethod]
    public void PageDmaWaitsForEventAndPreservesRealParity()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image);
        io.WriteRegister(30, new(SupervisorIoController.Channel3Free));
        Request(io, 0x60000 | (3U << 12), 1);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(3072).Data.Value);
        Assert.AreEqual(0UL, io.PendingExternalInterrupts);
        scheduler.AdvanceTo(19);
        Assert.AreEqual(0, io.CompletedTransfers);
        scheduler.AdvanceTo(20);
        for (int i = 0; i < 1024; i++) Assert.AreEqual(image[1040 + i], memory.PhysicalMemory.ReadRaw((uint)(3072 + i)));
        for (int i = 0; i < 8; i++) Assert.AreEqual(image[1032 + i], memory.PhysicalMemory.ReadRaw((uint)(24 + i)));
        Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(SupervisorIoController.Channel3Free, io.PendingExternalInterrupts);
    }

    [TestMethod]
    public void TrackDmaSelectsSecondHalfAndSecondChannel()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(8, image);
        Request(io, 0x20000 | (2U << 12) | 0x800, 1, 1, 1);
        scheduler.AdvanceTo(20);
        for (int i = 0; i < 512; i++) Assert.AreEqual(image[1552 + i], memory.PhysicalMemory.ReadRaw((uint)(2560 + i)));
        for (int i = 0; i < 4; i++) Assert.AreEqual(image[1036 + i], memory.PhysicalMemory.ReadRaw((uint)(36 + i)));
        Assert.AreEqual(SupervisorIoController.Channel4Free, io.ExternalInterrupts);
    }

    [TestMethod]
    public void SystemOnlyDoesNotOverwritePayload()
    {
        var (memory, scheduler, io) = Create();
        io.AttachDisk(0, Image());
        memory.PhysicalMemory.Store(2048, new(987), true, true);
        Request(io, 0x160000 | (2U << 12), 1);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(987UL, memory.PhysicalMemory.ReadRaw(2048).Data.Value);
        Assert.AreEqual(1032UL, memory.PhysicalMemory.ReadRaw(24).Data.Value);
    }

    [TestMethod]
    public void PhysicalZeroAndLastWordParticipateInDma()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image);
        Request(io, 0x60000, 0);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(image[8], memory.PhysicalMemory.ReadRaw(0));
        Request(io, 0x60000 | (31U << 12), 1);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(image[2063], memory.PhysicalMemory.ReadRaw(32767));
    }

    [TestMethod]
    public void Simh512DiskDmaUsesAllNineteenPhysicalAddressBits()
    {
        var memory = new MappedMemoryBackend(MemoryConfiguration.Simh512K);
        var scheduler = new EventScheduler();
        var io = new SupervisorIoController(memory, scheduler, () => { });
        var image = Image();
        io.AttachDisk(0, image, readOnly: false);
        const uint lastPage = 15U << 23 | 31U << 12;
        Request(io, 0x60000 | lastPage, 1);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(524288U, memory.PhysicalMemory.CapacityWords);
        Assert.AreEqual(image[1040], memory.PhysicalMemory.ReadRaw(523264));
        Assert.AreEqual(image[2063], memory.PhysicalMemory.ReadRaw(524287));
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(32767).Data.Value);
        var raw = new MemoryWord50(123); // Raw malformed controls must survive DMA.
        memory.PhysicalMemory.WriteRaw(524287, raw);
        Request(io, 0x40000 | lastPage, 0);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(raw, io.SnapshotDisk(0)[1031]);
        // The same encoded extended address still fails in the default book configuration.
        var (_, _, classical) = Create();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => classical.WriteDevice(3, new(0x60000 | lastPage)));
    }

    [TestMethod]
    public void Simh512DrumDmaIncludesLastPhysicalSectorWithoutWrapping()
    {
        var memory = new MappedMemoryBackend(MemoryConfiguration.Simh512K);
        var scheduler = new EventScheduler();
        var io = new SupervisorIoController(memory, scheduler, () => { });
        var image = Image(256);
        io.AttachDrum(1, image, readOnly: false);
        const uint lastSector = 15U << 23 | 0x1FC00;
        const uint zoneAndSector = 255U << 2 | 3;
        io.WriteDevice(2, new(0x20000 | lastSector | zoneAndSector));
        scheduler.AdvanceTo(20);
        Assert.AreEqual(image[255 * 1032 + 776], memory.PhysicalMemory.ReadRaw(524032));
        Assert.AreEqual(image[^1], memory.PhysicalMemory.ReadRaw(524287));
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(32767).Data.Value);
        var raw = new MemoryWord50(321);
        memory.PhysicalMemory.WriteRaw(524287, raw);
        io.WriteDevice(2, new(lastSector | zoneAndSector));
        scheduler.AdvanceTo(40);
        Assert.AreEqual(raw, io.SnapshotDrum(1)[^1]);
        var (_, _, classical) = Create();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => classical.WriteDevice(2, new(0x20000 | lastSector | zoneAndSector)));
    }

    [TestMethod]
    public void DmaDoesNotConsumeOrInvalidateCpuBuffers()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image);
        memory.HostMemory.Write(1024, new(777));
        memory.FetchInstruction(1024, false);
        memory.Write(1024, new(888));
        var operands = memory.GetOperandSnapshot();
        var instructions = memory.GetInstructionSnapshot();
        Request(io, 0x60000 | (1U << 12), 0);
        scheduler.AdvanceTo(20);
        CollectionAssert.AreEqual(operands, memory.GetOperandSnapshot());
        CollectionAssert.AreEqual(instructions, memory.GetInstructionSnapshot());
        Assert.AreEqual(image[8], memory.PhysicalMemory.ReadRaw(1024));
        Assert.AreEqual(888UL, memory.Read(1024).Value);
        Assert.AreEqual(777UL, memory.FetchInstruction(1024, false).Value);
    }

    [TestMethod]
    public void WritableDmaPreservesRawControlsAndHostImageIsCopied()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image, readOnly: false);
        var raw = new MemoryWord50(123); // Deliberately invalid control remains raw.
        memory.PhysicalMemory.WriteRaw(1024, raw);
        Request(io, 0x40000 | (1U << 12), 1);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(raw, io.SnapshotDisk(0)[1040]);
        Assert.AreNotEqual(raw, image[1040]);
        var snapshot = io.SnapshotDisk(0);
        snapshot[1040] = default;
        Assert.AreEqual(raw, io.SnapshotDisk(0)[1040]);
    }

    [TestMethod]
    public void ReadonlyMissingAndOutOfRangeDisksLatchChannelError()
    {
        var (_, scheduler, io) = Create();
        Request(io, 0x60000, 0);
        Assert.AreEqual(16UL, io.ReadDevice(2077).Value);
        io.AttachDisk(0, Image());
        Request(io, 0x40000, 0);
        Assert.AreEqual(16UL, io.ReadDevice(2077).Value);
        Request(io, 0x60000, 3);
        Assert.AreEqual(16UL, io.ReadDevice(2077).Value);
        scheduler.AdvanceTo(100);
        Assert.AreEqual(0, io.CompletedTransfers);
    }

    [TestMethod]
    public void EmptyKmdSelectionDeselectsWithoutChangingSignalsAndCanBeReselected()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image);
        io.RaiseInterrupts(SupervisorIoController.TimerInterrupt);
        io.WriteDevice(19, new(1024)); // Literal octal02000: selection strobe with empty mask.
        Assert.AreEqual(SupervisorIoController.TimerInterrupt, io.ExternalInterrupts);
        Assert.AreEqual(0UL, io.ReadDevice(2077).Value);
        io.WriteDevice(19, new(9)); // Status query after deselection is not an attached unit.
        Assert.AreEqual(0UL, io.ReadDevice(2051).Value);
        Request(io, 0x60000 | (1U << 12), 0);
        Assert.AreEqual(16UL, io.ReadDevice(2077).Value);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(0, io.CompletedTransfers);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(1024).Data.Value);
        io.WriteDevice(19, new(1025)); // Select unit0 again.
        Request(io, 0x60000 | (1U << 12), 0);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(image[8], memory.PhysicalMemory.ReadRaw(1024));
    }

    [TestMethod]
    public void SelectedSimhCompareCodesStrobeHasNoObservableEffectOrDma()
    {
        var (memory, scheduler, io) = Create();
        var image = Image();
        io.AttachDisk(0, image);
        io.RaiseInterrupts(SupervisorIoController.TimerInterrupt);
        io.WriteDevice(19, new(6)); // Literal006, pinned disk_ctl only breaks.
        Assert.AreEqual(SupervisorIoController.TimerInterrupt, io.ExternalInterrupts);
        Assert.AreEqual(0UL, io.ReadDevice(2077).Value);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(0, io.CompletedTransfers);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(1024).Data.Value);
        Request(io, 0x60000 | (1U << 12), 0);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(image[8], memory.PhysicalMemory.ReadRaw(1024));
    }

    [TestMethod]
    public void InvalidRangeAndBusyRequestsFailBeforeMutation()
    {
        var (_, _, io) = Create();
        io.AttachDisk(0, Image());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => io.WriteDevice(3, new(0x60000 | 0x800000)));
        Request(io, 0x60000, 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => io.WriteDevice(3, new(0x60000)));
        Assert.ThrowsExactly<InvalidOperationException>(() => io.WriteDevice(19, new(0x800)));
        Assert.ThrowsExactly<InvalidOperationException>(() => io.AttachDisk(0, Image()));
    }

    [TestMethod]
    public void RegisterClearCannotClearWiredChannelSignals()
    {
        var (_, _, io) = Create();
        io.RaiseInterrupts(SupervisorIoController.Channel3Free | SupervisorIoController.TimerInterrupt);
        io.WriteRegister(30, new(Word48.Mask48));
        io.WriteRegister(31, Word48.Zero);
        Assert.AreEqual(SupervisorIoController.Channel3Free, io.ReadRegister(159).Value);
        Assert.AreEqual(io.ReadRegister(159), io.ReadRegister(31));
        io.WriteDevice(3, new(0x60000));
        Assert.AreEqual(0UL, io.PendingExternalInterrupts);
    }

    [TestMethod]
    public void OptionalSimhSlowClockRaisesLiteralBitTenOnFourthPulseAndRestartResetsPhase()
    {
        var memory = new MappedMemoryBackend();
        var scheduler = new EventScheduler();
        var notifications = new List<ulong>();
        SupervisorIoController? io = null;
        io = new(memory, scheduler, () => notifications.Add(io!.ExternalInterrupts));
        io.StartTimer(5, includeSlowClock: true);
        for (ulong tick = 5; tick <= 15; tick += 5)
        {
            scheduler.AdvanceTo(tick);
            Assert.AreEqual(1UL << 39, io.ExternalInterrupts);
            io.WriteRegister(31, Word48.Zero);
        }
        notifications.Clear();
        scheduler.AdvanceTo(20);
        Assert.AreEqual((1UL << 39) | (1UL << 9), io.ExternalInterrupts);
        CollectionAssert.AreEqual(new[] { (1UL << 39) | (1UL << 9) }, notifications.ToArray());
        io.WriteRegister(31, Word48.Zero);
        io.StopTimer();
        scheduler.AdvanceTo(40);
        Assert.AreEqual(0UL, io.ExternalInterrupts);
        io.StartTimer(5, includeSlowClock: true);
        scheduler.AdvanceTo(55);
        Assert.AreEqual(0UL, io.ExternalInterrupts & (1UL << 9));
        scheduler.AdvanceTo(60);
        Assert.AreEqual(1UL << 9, io.ExternalInterrupts & (1UL << 9));
        io.WriteRegister(31, Word48.Zero);
        io.StartTimer(5); // Existing overload preserves timer-only behavior and cancels prior schedule.
        scheduler.AdvanceTo(80);
        Assert.AreEqual(1UL << 39, io.ExternalInterrupts);
    }

    [TestMethod]
    public void PeriodicTimerUsesTicksAndCanBeCancelled()
    {
        var (_, scheduler, io) = Create();
        io.StartTimer(5);
        scheduler.AdvanceTo(4);
        Assert.AreEqual(0UL, io.ExternalInterrupts);
        scheduler.AdvanceTo(5);
        Assert.AreEqual(SupervisorIoController.TimerInterrupt, io.ExternalInterrupts);
        io.WriteRegister(31, Word48.Zero);
        scheduler.AdvanceTo(10);
        Assert.AreEqual(SupervisorIoController.TimerInterrupt, io.ExternalInterrupts);
        io.StopTimer();
        io.WriteRegister(31, Word48.Zero);
        scheduler.AdvanceTo(30);
        Assert.AreEqual(0UL, io.ExternalInterrupts);
    }

    [TestMethod]
    public void ConsoleCompletionAndInputUsePeripheralMaskAndEvenParity()
    {
        var (_, scheduler, io) = Create();
        var output = new List<byte>();
        io.ConsoleOutput += (_, value) => output.Add(value);
        io.ConfigureConsole(0);
        Assert.AreNotEqual(0U, io.PeripheralInterrupts & 0x200);
        io.WriteDevice(24, Word48.Zero); // Acknowledge initial CAN_PRINT.
        io.WriteDevice(28, new(0xA00));
        io.WriteDevice(124, new(0xC1));
        CollectionAssert.AreEqual(new byte[] { 65 }, output.ToArray());
        Assert.AreEqual(0UL, io.ExternalInterrupts);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(SupervisorIoController.PeripheralInterrupt, io.ExternalInterrupts);
        io.WriteDevice(24, Word48.Zero);
        Assert.AreEqual(0UL, io.ExternalInterrupts);
        io.ReceiveConsole(0, 1);
        Assert.AreEqual(129UL, io.ReadDevice(2172).Value);
        Assert.AreEqual(SupervisorIoController.PeripheralInterrupt, io.ExternalInterrupts);
    }

    [TestMethod]
    public void SimhSemanticTagsAreTranslatedToPayloadDependentRealControls()
    {
        var (memory, scheduler, io) = Create();
        var bytes = new byte[SupervisorIoController.ZoneWords * 8];
        for (int i = 0; i < SupervisorIoController.ZoneWords; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8, 8), (uint)i | ((ulong)(uint)((i & 1) + 1) << 48));
        io.AttachSimhDiskImage(0, bytes);
        Request(io, 0x60000 | (1U << 12), 0);
        scheduler.AdvanceTo(20);
        for (int i = 0; i < 1024; i++)
        {
            var word = memory.PhysicalMemory.ReadRaw((uint)(1024 + i));
            Assert.IsTrue(word.HasValidOperandControl);
            Assert.AreEqual((i & 1) == 0, word.HasValidInstructionControl(false));
            Assert.AreEqual((i & 1) == 0, word.HasValidInstructionControl(true));
        }
        Array.Clear(bytes);
        Assert.ThrowsExactly<ArgumentException>(() => io.AttachSimhDiskImage(0, bytes));
    }

    [TestMethod]
    public void DisconnectedDeviceQueriesAreExplicitAndUnknownPortsFail()
    {
        var (_, _, io) = Create();
        Assert.AreEqual(0xC0000UL, io.ReadDevice(2073).Value);
        Assert.AreEqual(0xFFFFFFUL, io.ReadDevice(2115).Value);
        Assert.ThrowsExactly<NotSupportedException>(() => io.ReadDevice(2049));
        Assert.ThrowsExactly<NotSupportedException>(() => io.ReadDevice(2125));
        Assert.ThrowsExactly<NotSupportedException>(() => io.WriteDevice(5, Word48.Zero));
        io.ConfigureSerialTerminal(1);
        io.WriteDevice(107, Word48.Zero); // Explicit selected-SIMH functional adapter behavior.
        Assert.ThrowsExactly<NotSupportedException>(() => io.WriteRegister(35, Word48.Zero));
    }

    [TestMethod]
    public void TrackHeaderAddressZeroMatchesPinnedKmdCombEncoding()
    {
        var (memory, scheduler, io) = Create();
        io.AttachDisk(0, Image());
        io.WriteDevice(19, new(7));
        Assert.AreEqual(0, io.CompletedHeaderReads);
        scheduler.AdvanceTo(20);
        ulong[] expected = [0x1310842148C6, 0x10842000042, 0x1339CE739CE7, 0x1FFFFFFFFFFF];
        for (uint i = 0; i < 4; i++)
        {
            var word = memory.PhysicalMemory.ReadRaw(24 + i);
            Assert.AreEqual(expected[i], word.Data.Value);
            Assert.IsTrue(word.HasValidOperandControl);
            Assert.IsFalse(word.HasValidInstructionControl(false));
        }
        Assert.AreEqual(1, io.CompletedHeaderReads);
        Assert.AreEqual(0, io.CompletedTransfers);
    }

    [TestMethod]
    public void DrumPageRoundTripUsesPhysicalServiceWordsAndIndependentReadyBit()
    {
        var (memory, scheduler, io) = Create();
        io.CreateScratchDrum(0);
        var raw = new MemoryWord50(456); // Raw error pattern survives storage.
        memory.PhysicalMemory.WriteRaw(1024, raw);
        memory.PhysicalMemory.Store(8, new(321), true, true);
        io.WriteDevice(1, new(0x40000 | (1U << 12) | (3U << 2)));
        Assert.AreEqual(0, io.CompletedDrumTransfers);
        scheduler.AdvanceTo(20);
        Assert.AreEqual(SupervisorIoController.Drum1Free, io.ExternalInterrupts);
        Assert.AreEqual(raw, io.SnapshotDrum(0)[3 * 1032 + 8]);
        memory.PhysicalMemory.Store(1024, Word48.Zero, true, true);
        io.WriteDevice(1, new(0x60000 | (1U << 12) | (3U << 2)));
        Assert.AreEqual(0UL, io.ExternalInterrupts);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(raw, memory.PhysicalMemory.ReadRaw(1024));
        Assert.AreEqual(321UL, memory.PhysicalMemory.ReadRaw(8).Data.Value);
        Assert.AreEqual(2, io.CompletedDrumTransfers);
    }

    [TestMethod]
    public void DrumSectorSelectsCorrectQuarterAndDoesNotTransferAnotherQuarter()
    {
        var (memory, scheduler, io) = Create();
        var image = Image(256);
        io.AttachDrum(1, image);
        io.WriteDevice(2, new(0x20000 | (4U << 12) | 0xC00 | (5U << 2) | 3));
        scheduler.AdvanceTo(20);
        Assert.AreEqual(image[5 * 1032 + 8 + 768], memory.PhysicalMemory.ReadRaw(4864));
        Assert.AreEqual(image[5 * 1032 + 8 + 1023], memory.PhysicalMemory.ReadRaw(5119));
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(5120).Data.Value);
        Assert.AreEqual(image[5 * 1032 + 6], memory.PhysicalMemory.ReadRaw(22));
        Assert.AreEqual(SupervisorIoController.Drum2Free, io.ExternalInterrupts);
        Assert.ThrowsExactly<NotSupportedException>(() => io.WriteDevice(2, new(0x460000)));
    }

    [TestMethod]
    public void SerialOutputConsumesInvertedLittleEndianFrameAtBitClock()
    {
        var (_, _, io) = Create();
        io.ConfigureSerialTerminal(1);
        Assert.IsTrue(io.IsSerialIdle);
        var output = new List<byte>();
        io.SerialOutput += (line, code) => { Assert.AreEqual(1, line); output.Add(code); };
        const byte code = 65;
        io.WriteDevice(96, new(1U << 23)); io.PulseSerialClock(); // start
        Assert.IsFalse(io.IsSerialIdle);
        for (int bit = 0; bit < 7; bit++)
        {
            io.WriteDevice(96, new(((code & (1 << bit)) == 0 ? 1UL : 0UL) << 23));
            io.PulseSerialClock();
        }
        io.WriteDevice(96, new(1U << 23)); io.PulseSerialClock(); // even parity of inverted data
        Assert.AreEqual(0, output.Count);
        io.WriteDevice(96, Word48.Zero); io.PulseSerialClock(); // stop
        Assert.IsTrue(io.IsSerialIdle);
        CollectionAssert.AreEqual(new byte[] { code }, output.ToArray());
    }

    [TestMethod]
    public void SerialInputClockReturnsStartDataParityAndStopBitsWithoutDroppingMask()
    {
        var (_, scheduler, io) = Create();
        io.ConfigureSerialTerminal(24);
        io.WriteRegister(30, new(SupervisorIoController.SerialInterrupt));
        io.ReceiveSerial(24, new byte[] { 1 });
        io.StartSerialClock(3);
        scheduler.AdvanceTo(3);
        Assert.AreEqual(1UL, io.ReadDevice(2112).Value);
        Assert.AreEqual(SupervisorIoController.SerialInterrupt, io.PendingExternalInterrupts);
        Assert.AreNotEqual(0UL, io.ExternalInterrupts & SupervisorIoController.SerialInputStart);
        scheduler.AdvanceTo(6); Assert.AreEqual(0UL, io.ReadDevice(2112).Value); // data bit 0 = one
        for (ulong tick = 9; tick <= 24; tick += 3)
        {
            scheduler.AdvanceTo(tick); Assert.AreEqual(1UL, io.ReadDevice(2112).Value); // remaining six zeros inverted
        }
        scheduler.AdvanceTo(27); Assert.AreEqual(0UL, io.ReadDevice(2112).Value); // parity: odd original
        scheduler.AdvanceTo(30); Assert.AreEqual(0UL, io.ReadDevice(2112).Value); // stop
        io.StopSerialClock();
        Assert.AreEqual(SupervisorIoController.SerialInterrupt, io.ExternalInterruptMask);
    }

    [TestMethod]
    public void PaperReaderHasDummyStartRowsOddParityCardsAndEofReadiness()
    {
        var (_, scheduler, io) = Create();
        io.AttachPaperTapeText(0, "\u001dА0\n\u001d");
        Assert.AreNotEqual(0UL, io.ReadDevice(2073).Value & 0x8000);
        io.WriteDevice(8, new(4));
        for (int row = 0; row <= 241; row++)
        {
            io.WriteDevice(8, new(5));
            scheduler.AdvanceTo((ulong)((row + 1) * 20));
            ulong value = io.ReadDevice(2060).Value;
            if (row == 0) Assert.AreEqual(0UL, value); // Motor dummy synchronization.
            else if (row == 1) Assert.AreEqual(32UL, value); // А already has odd parity.
            else if (row == 2) Assert.AreEqual(128UL, value); // GOST digit zero gains parity bit.
            else if (row <= 120) Assert.AreEqual(0UL, value); // Card padding.
            else if (row <= 240) Assert.AreEqual((row - 121) % 5 == 0 ? 128UL : 0UL, value); // ENDA3.
            else Assert.AreEqual(0UL, value); // Tail.
        }
        io.WriteDevice(8, new(5));
        Assert.AreEqual(0UL, io.ReadDevice(2073).Value & 0x8000);
        Assert.AreNotEqual(0UL, io.ExternalInterrupts & (1UL << 41));
    }

    [TestMethod]
    public void PrinterWheelHammersOverprintingAndLineFeedProduceActualCodes()
    {
        var (_, scheduler, io) = Create();
        io.ConfigurePrinter(0);
        var lines = new List<byte[][]>();
        io.PrinterOutput += (_, line) => lines.Add(line);
        io.WriteDevice(12, new(4));
        scheduler.AdvanceTo(20); // Current offset 1 is GOST digit zero.
        io.WriteDevice(32, new(3)); // Positions 0 and 8.
        scheduler.AdvanceTo(40); // Current offset 2 is digit one.
        io.WriteDevice(32, new(1)); // Overprint position zero.
        io.WriteDevice(12, new(1));
        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual(2, lines[0].Length);
        Assert.AreEqual(0, lines[0][0][0]);
        Assert.AreEqual(0, lines[0][0][8]);
        Assert.AreEqual(255, lines[0][0][1]);
        Assert.AreEqual(1, lines[0][1][0]);
        Assert.AreEqual(0UL, io.ReadDevice(2073).Value & (1UL << 23));
        scheduler.AdvanceTo(60);
        Assert.AreNotEqual(0UL, io.ReadDevice(2073).Value & (1UL << 23));
        Assert.AreNotEqual(0UL, io.ExternalInterrupts & (1UL << 47));
        io.WriteDevice(12, new(10));
        io.WriteRegister(31, Word48.Zero);
        scheduler.AdvanceTo(5000);
        Assert.AreEqual(0UL, io.ExternalInterrupts);
    }

    [TestMethod]
    public void DisconnectedParallelConsoleCannotEmitOrCompleteAndDetachCancelsOutput()
    {
        var (_, scheduler, io) = Create();
        int printed = 0;
        io.ConsoleOutput += (_, _) => printed++;
        io.WriteDevice(124, new(65));
        scheduler.AdvanceTo(20);
        Assert.AreEqual(0, printed);
        Assert.AreEqual(0U, io.PeripheralInterrupts);
        Assert.ThrowsExactly<InvalidOperationException>(() => io.ReceiveConsole(0, 1));
        io.ConfigureConsole(0);
        io.WriteDevice(24, Word48.Zero);
        io.WriteDevice(124, new(65));
        io.ConfigureConsole(0, connected: false);
        scheduler.AdvanceTo(40);
        Assert.AreEqual(1, printed);
        Assert.AreEqual(0U, io.PeripheralInterrupts);
    }

    [TestMethod]
    public void ScratchDiskFormatterMatchesPinnedFirstAndLastTrackHeadersAndNumericZeros()
    {
        var (_, _, io) = Create();
        io.CreateScratchDisk(6);
        var image = io.SnapshotDisk(6);
        Assert.AreEqual(1015 * 1032, image.Length);
        foreach (int zone in new[] { 0, 1, 1014 })
        {
            int start = zone * 1032;
            Assert.AreEqual((ulong)(uint)(2 * zone) << 36, image[start].Data.Value);
            Assert.AreEqual((ulong)(uint)(2 * zone + 1) << 36, image[start + 4].Data.Value);
            Assert.AreEqual(0x5F1C7804000UL, image[start + 1].Data.Value); // Literal magic + volume 2052.
            Assert.AreEqual(image[start + 1], image[start + 5]);
            for (int i = 0; i < 1032; i++)
            {
                var word = image[start + i];
                Assert.IsTrue(word.HasValidOperandControl);
                Assert.IsFalse(word.HasValidInstructionControl(false));
                Assert.IsFalse(word.HasValidInstructionControl(true));
                if (i is not (0 or 1 or 4 or 5)) Assert.AreEqual(0UL, word.Data.Value);
            }
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => io.CreateScratchDisk(6, volume: 2047));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => io.CreateScratchDisk(6, volume: 4096));
        Assert.AreEqual(image[1], io.SnapshotDisk(6)[1]);
    }

    [TestMethod]
    public void SelectedInterfaceSignalExtinguishingPreservesActiveSerialFrameInputAndClock()
    {
        var (_, scheduler, io) = Create();
        io.ConfigureSerialTerminal(1);
        io.StartSerialClock(3);
        io.ReceiveSerial(1, new byte[] { 65, 2 });
        var output = new List<byte>();
        io.SerialOutput += (_, value) => output.Add(value);
        io.WriteDevice(96, new(1U << 23));
        scheduler.AdvanceTo(3); // Start bit on both directions.
        io.WriteDevice(107, Word48.Zero); // UVV 0153: pinned SIMH leaves the streams intact.
        Assert.AreEqual(1UL << 23, io.ReadDevice(2112).Value);
        for (int bit = 0; bit < 7; bit++)
        {
            io.WriteDevice(96, new(((65 & (1 << bit)) == 0 ? 1UL : 0UL) << 23));
            scheduler.AdvanceTo((ulong)(6 + 3 * bit));
        }
        io.WriteDevice(96, new(1U << 23)); scheduler.AdvanceTo(27);
        io.WriteDevice(96, Word48.Zero); scheduler.AdvanceTo(30);
        CollectionAssert.AreEqual(new byte[] { 65 }, output.ToArray());
        scheduler.AdvanceTo(42);
        Assert.AreEqual(1UL << 23, io.ReadDevice(2112).Value); // Queued second character still starts.
        io.StopSerialClock();
    }
}
