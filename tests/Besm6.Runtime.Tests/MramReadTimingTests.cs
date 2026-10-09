using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class MramReadTimingTests
{
    private static MramReadTiming Nominal() => new(MemoryConfiguration.Classical32K, new(2000), new(900));

    [TestMethod]
    public void DataReturnDoesNotReleaseItsBank()
    {
        var memory = Nominal();
        Assert.IsTrue(memory.TryBeginRead(1, new(100), out var read));
        Assert.AreEqual(1, read.Bank);
        Assert.AreEqual(new HardwareInstant(1000), read.DataReady);
        Assert.AreEqual(new HardwareInstant(2100), read.BankAvailable);
        Assert.IsFalse(memory.TryBeginRead(9, read.DataReady, out _));
        Assert.IsFalse(memory.TryBeginRead(17, new(2099), out _));
        Assert.IsTrue(memory.TryBeginRead(9, new(2100), out var next));
        Assert.AreEqual(new HardwareInstant(3000), next.DataReady);
    }

    [TestMethod]
    public void DifferentLowAddressBitsUseIndependentBanks()
    {
        var memory = Nominal();
        for (uint bank = 0; bank < 8; bank++)
        {
            Assert.IsTrue(memory.TryBeginRead(32760 + bank, new(0), out var read));
            Assert.AreEqual((int)bank, read.Bank);
            Assert.AreEqual(new HardwareInstant(900), read.DataReady);
            Assert.IsFalse(memory.TryBeginRead(bank, new(0), out _));
        }
    }

    [TestMethod]
    public void RejectedRequestDoesNotCreateAFutureReservation()
    {
        var memory = Nominal();
        memory.TryBeginRead(0, new(0), out _);
        Assert.IsFalse(memory.TryBeginRead(8, new(1000), out _));
        Assert.AreEqual(new HardwareInstant(2000), memory.AvailableAt(0));
        Assert.IsTrue(memory.TryBeginRead(16, new(2000), out _));
        Assert.AreEqual(new HardwareInstant(4000), memory.AvailableAt(8));
    }

    [TestMethod]
    public void UnspecifiedExpandedGeometryAndInvalidIntervalsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramReadTiming(MemoryConfiguration.Simh512K, new(2000), new(900)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramReadTiming(MemoryConfiguration.Classical32K, new(1999), new(900)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramReadTiming(MemoryConfiguration.Classical32K, new(2000), new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MramReadTiming(MemoryConfiguration.Classical32K, new(2000), new(2001)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Nominal().TryBeginRead(32768, new(0), out _));
    }

    [TestMethod]
    public void OverflowDoesNotConsumeTheBank()
    {
        var memory = Nominal();
        Assert.ThrowsExactly<OverflowException>(() => memory.TryBeginRead(0, new(ulong.MaxValue - 1000), out _));
        Assert.AreEqual(new HardwareInstant(0), memory.AvailableAt(0));
        Assert.IsTrue(memory.TryBeginRead(0, new(0), out _));
    }

    [TestMethod]
    public void ConfiguredCycleMayBeSlowerWithoutChangingReadLatency()
    {
        var memory = new MramReadTiming(MemoryConfiguration.Classical32K, new(2500), new(950));
        memory.TryBeginRead(0, new(100), out var read);
        Assert.AreEqual(new HardwareInstant(1050), read.DataReady);
        Assert.AreEqual(new HardwareInstant(2600), read.BankAvailable);
    }

    [TestMethod]
    public void BankDataReadyAndBrchAcceptanceRemainSeparateEvents()
    {
        var memory = Nominal();
        var transfer = new ArithmeticBufferTransferTiming(new(100));
        var timeline = new HardwareTimeline();
        var log = new List<string>();
        memory.TryBeginRead(0, timeline.Now, out var read);
        timeline.ScheduleAt(read.DataReady, () => log.Add("GBRCh@900"));
        timeline.ScheduleAt(transfer.AcceptAfterBufferWait(read.DataReady), () =>
        {
            Assert.IsFalse(memory.TryBeginRead(8, timeline.Now, out _));
            log.Add("PVR@1050");
        });
        timeline.ScheduleAt(read.BankAvailable, () =>
        {
            Assert.IsTrue(memory.TryBeginRead(8, timeline.Now, out _));
            log.Add("bank@2000");
        });
        timeline.AdvanceTo(new(2000));
        CollectionAssert.AreEqual(new[] { "GBRCh@900", "PVR@1050", "bank@2000" }, log);
        // Nominal0.9us is a published approximation, not a calibrated hardware deadline.
    }
}
