using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class MramMemoryPortTests
{
    private static MramPortConfiguration Configuration(MramReadSampling sample = MramReadSampling.AtCycleStart) =>
        new(new(2000), new(900), new(1500), sample, MramArbitration.FifoPerBank);
    private static MemoryWord50 Word(ulong data) => MemoryWord50.Form(new(data), true, true);

    [TestMethod]
    public void FifoSharesBanksBetweenWritesAndReadsButDifferentBanksOverlap()
    {
        var memory = new PhysicalMemory(); var time = new HardwareTimeline();
        var port = new MramMemoryPort(memory, time, Configuration());
        var log = new List<MramTransfer>();
        port.Write(8, Word(17), log.Add); port.Read(8, log.Add); port.Read(1, log.Add);
        time.AdvanceTo(new(899)); Assert.AreEqual(0, log.Count); Assert.AreEqual(default, memory.ReadRaw(8));
        time.AdvanceTo(new(900)); Assert.AreEqual(1U, log[0].Address);
        time.AdvanceTo(new(1500)); Assert.AreEqual(Word(17), memory.ReadRaw(8));
        Assert.AreEqual(new HardwareInstant(2000), port.AvailableAt(8));
        time.AdvanceTo(new(2900)); Assert.AreEqual(Word(17), log[2].Word);
        Assert.AreEqual(new HardwareInstant(2000), log[2].Started);
        Assert.AreEqual(new HardwareInstant(4000), log[2].BankAvailable);
        Assert.AreEqual(0, port.PendingRequests);
    }

    [TestMethod]
    [DataRow(MramReadSampling.AtCycleStart, 1UL)]
    [DataRow(MramReadSampling.AtDataReturn, 2UL)]
    public void ExplicitSamplingDecidesWhichInFlightDataIsReturned(MramReadSampling policy, ulong expected)
    {
        var memory = new PhysicalMemory(); var time = new HardwareTimeline(); memory.WriteRaw(8, Word(1));
        var port = new MramMemoryPort(memory, time, Configuration(policy)); MramTransfer? result = null;
        port.Read(8, value => result = value);
        time.ScheduleAt(new(450), () => memory.WriteRaw(8, Word(2)));
        time.AdvanceTo(new(900)); Assert.AreEqual(Word(expected), result!.Value.Word);
    }

    [TestMethod]
    public void CancellationOfStartedWriteSuppressesPublicationWithoutFreeingBank()
    {
        var memory = new PhysicalMemory(); var time = new HardwareTimeline();
        var port = new MramMemoryPort(memory, time, Configuration()); bool returned = false;
        var write = port.Write(8, Word(7), _ => Assert.Fail("Cancelled write returned"));
        time.AdvanceTo(new(100)); Assert.IsTrue(port.Cancel(write)); Assert.IsFalse(port.Cancel(write));
        port.Read(16, _ => returned = true);
        time.AdvanceTo(new(1999)); Assert.IsFalse(returned); Assert.AreEqual(default, memory.ReadRaw(8));
        time.AdvanceTo(new(2900)); Assert.IsTrue(returned);
    }

    [TestMethod]
    public void CancelQueuedHeadAndMiddlePreservesSurvivingOrderAndNoForeignEventsAreRemoved()
    {
        var time = new HardwareTimeline(); var port = new MramMemoryPort(new(), time, Configuration());
        var log = new List<uint>();
        var first = port.Read(8, _ => Assert.Fail()); var middle = port.Read(16, _ => Assert.Fail());
        port.Read(24, value => log.Add(value.Address)); bool external = false;
        time.ScheduleAt(new(500), () => external = true);
        Assert.IsTrue(port.Cancel(first)); Assert.IsTrue(port.Cancel(middle));
        time.AdvanceTo(new(900)); Assert.IsTrue(external); CollectionAssert.AreEqual(new uint[] { 24 }, log);
        Assert.IsNull(time.NextEventTime);
    }

    [TestMethod]
    public void TerminalTokenAndForeignTokenCannotCancelAnotherTransfer()
    {
        var time = new HardwareTimeline(); var a = new MramMemoryPort(new(), time, Configuration());
        var b = new MramMemoryPort(new(), time, Configuration());
        var token = a.Read(0, _ => { }); Assert.IsFalse(b.Cancel(token)); Assert.IsFalse(a.Cancel(default));
        time.AdvanceTo(new(900)); Assert.IsFalse(a.Cancel(token));
    }

    [TestMethod]
    public void CallbackFailureLeavesTransferPublishedAndSuccessorScheduled()
    {
        var time = new HardwareTimeline(); var memory = new PhysicalMemory();
        var port = new MramMemoryPort(memory, time, Configuration());
        var failure = new ApplicationException("receiver"); bool read = false;
        var token = port.Write(8, Word(5), _ => throw failure);
        port.Read(8, value => { Assert.AreEqual(Word(5), value.Word); read = true; });
        var exception = Assert.ThrowsExactly<HardwareCallbackException>(() => time.AdvanceTo(new(1500)));
        Assert.AreSame(failure, exception.InnerException); Assert.AreEqual(Word(5), memory.ReadRaw(8));
        Assert.IsFalse(port.Cancel(token)); time.AdvanceTo(new(2900)); Assert.IsTrue(read);
    }

    [TestMethod]
    public void ReceiverCanEnqueueAndCancelWithoutRecursivelyAdvancingCalendar()
    {
        var time = new HardwareTimeline(); var port = new MramMemoryPort(new(), time, Configuration()); bool returned = false;
        port.Read(8, _ => { }); // Keep the next same-bank request queued until 2000 ns.
        var queued = port.Read(16, _ => { });
        port.Read(1, _ => {
            Assert.IsTrue(port.Cancel(queued));
            port.Read(9, _ => returned = true);
            Assert.ThrowsExactly<InvalidOperationException>(() => time.AdvanceTo(time.Now));
        });
        time.AdvanceTo(new(900)); time.AdvanceTo(new(2900)); Assert.IsTrue(returned);
    }

    [TestMethod]
    public void SharedStorageRawControlAndCpuResetPreserveHardwareRequest()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor); var model = machine.HardwareModel;
        var port = model.CreateMemoryPort(Configuration());
        Assert.AreSame(port, model.CreateMemoryPort(Configuration()));
        Assert.ThrowsExactly<InvalidOperationException>(() => model.CreateMemoryPort(Configuration(MramReadSampling.AtDataReturn)));
        var raw = new MemoryWord50(0); // Raw control bits are deliberately not interpreted at this port.
        MramTransfer? read = null;
        port.Write(8, raw, _ => { }); port.Read(8, value => read = value);
        machine.ResetCpu(); model.Timeline.AdvanceTo(new(2900));
        Assert.AreEqual(raw, machine.MappedMemory!.PhysicalMemory.ReadRaw(8)); Assert.AreEqual(raw, read!.Value.Word);
        Assert.AreEqual(0UL, machine.Clock.Tick); Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.ThrowsExactly<InvalidOperationException>(() => new MachineCore().HardwareModel.CreateMemoryPort(Configuration()));
    }

    [TestMethod]
    public void InvalidInputsAndTimeOverflowDoNotConsumeRequestOrBank()
    {
        var time = new HardwareTimeline(new(ulong.MaxValue - 1000));
        var port = new MramMemoryPort(new(), time, Configuration());
        Assert.ThrowsExactly<OverflowException>(() => port.Read(8, _ => { }));
        Assert.AreEqual(0, port.PendingRequests); Assert.IsNull(time.NextEventTime);
        Assert.AreEqual(default, port.AvailableAt(8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => port.Read(32768, _ => { }));
        Assert.ThrowsExactly<ArgumentNullException>(() => port.Read(8, null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramMemoryPort(new(MemoryConfiguration.Simh512K), new(), Configuration()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramMemoryPort(new(), new(), Configuration() with { WriteVisibility = new(2001) }));
    }
}
