using Besm6.Runtime.Timing.Calendar;
using Besm6.Architecture.Memory;
using Besm6.Processor.Memory;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class MramRequestReuseTests
{
    private static MramMemoryPort Port(PhysicalMemory memory, HardwareTimeline time) =>
        new(memory, time, new(new(2000), new(900), new(1500),
            MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank));
    private static MemoryWord50 Word(ulong value) => MemoryWord50.Form(new(value), true, true);

    [TestMethod]
    public void CancelledInFlightTokenCannotCancelANewRequestWhileItsEventIsQueued()
    {
        var memory = new PhysicalMemory();
        var timeline = new HardwareTimeline();
        var port = Port(memory, timeline);
        memory.WriteRaw(1, Word(11)); memory.WriteRaw(2, Word(22));
        var delivered = new List<MramTransfer>();
        var old = port.Read(8, _ => Assert.Fail("Cancelled request returned"));
        var survivor = port.Read(1, delivered.Add);
        timeline.ScheduleAt(new(200), () => { });
        timeline.AdvanceTo(new(100));
        Assert.IsTrue(port.Cancel(old));
        var replacement = port.Read(2, delivered.Add);
        Assert.IsTrue(replacement.Id > survivor.Id);
        Assert.IsFalse(port.Cancel(old));
        timeline.AdvanceTo(new(900));
        Assert.AreEqual(1, delivered.Count);
        Assert.AreEqual(survivor, delivered[0].Token);
        Assert.AreEqual(Word(11), delivered[0].Word);
        Assert.AreEqual(1, port.PendingRequests);
        Assert.IsFalse(port.Cancel(old));
        timeline.AdvanceTo(new(1000));
        Assert.AreEqual(2, delivered.Count);
        Assert.AreEqual(replacement, delivered[1].Token);
        Assert.AreEqual(Word(22), delivered[1].Word);
        Assert.AreEqual(new HardwareInstant(100), delivered[1].Started);
        Assert.AreEqual(0, port.PendingRequests);
        Assert.IsFalse(port.Cancel(replacement));
    }

    [TestMethod]
    public void CancelledQueueNodesAndCompletedRequestsCanBeReusedWithoutChangingFifo()
    {
        var memory = new PhysicalMemory();
        var timeline = new HardwareTimeline();
        var port = Port(memory, timeline);
        var tokens = new List<MramRequestToken>();
        var delivered = new List<MramTransfer>();
        for (uint index = 0; index < 256; index++)
        {
            memory.WriteRaw(index * 8, Word(index));
            tokens.Add(port.Read(index * 8, delivered.Add));
        }
        for (int index = 0; index < tokens.Count; index += 2)
            Assert.IsTrue(port.Cancel(tokens[index]));
        timeline.AdvanceTo(new(256_000));
        Assert.AreEqual(128, delivered.Count);
        for (int index = 0; index < delivered.Count; index++)
        {
            Assert.AreEqual(tokens[index * 2 + 1], delivered[index].Token);
            Assert.AreEqual(Word((ulong)(index * 2 + 1)), delivered[index].Word);
            Assert.AreEqual(new HardwareInstant((ulong)index * 2000), delivered[index].Started);
        }
        memory.WriteRaw(1, Word(999));
        var next = port.Read(1, delivered.Add);
        foreach (var token in tokens) Assert.IsFalse(port.Cancel(token));
        timeline.AdvanceTo(new(256_900));
        Assert.AreEqual(next, delivered[^1].Token);
        Assert.AreEqual(Word(999), delivered[^1].Word);
        Assert.AreEqual(129, delivered.Count);
        Assert.AreEqual(0, port.PendingRequests);
    }

    [TestMethod]
    public void FailedReceiverReleasesRequestWithoutReplayingItsFailureOnReuse()
    {
        var memory = new PhysicalMemory();
        var timeline = new HardwareTimeline();
        var port = Port(memory, timeline);
        var cause = new ApplicationException("MRAM receiver");
        var failed = port.Read(8, _ => throw cause);
        var exception = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(900)));
        Assert.AreSame(cause, exception.InnerException);
        Assert.AreEqual(0, port.PendingRequests);
        memory.WriteRaw(1, Word(77));
        MramTransfer? result = null;
        var replacement = port.Read(1, value => result = value);
        Assert.IsFalse(port.Cancel(failed));
        timeline.AdvanceTo(new(1800));
        Assert.IsNotNull(result);
        Assert.AreEqual(replacement, result.Value.Token);
        Assert.AreEqual(Word(77), result.Value.Word);
        Assert.AreEqual(0, port.PendingRequests);
    }
}
