using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticBufferTransferTimingTests
{
    [TestMethod]
    [DataRow(100UL, 1300UL, 2150UL)]
    [DataRow(110UL, 1330UL, 2165UL)]
    public void ReadyRequestAndWaitReleaseHaveDistinctPhaseOrigins(
        ulong quantum, ulong requestedAcceptance, ulong waitedAcceptance)
    {
        var timing = new ArithmeticBufferTransferTiming(new(quantum));
        Assert.AreEqual(new HardwareInstant(requestedAcceptance), timing.AcceptFromReadyBuffer(new(1000)));
        Assert.AreEqual(new HardwareInstant(waitedAcceptance), timing.AcceptAfterBufferWait(new(2000)));
    }

    [TestMethod]
    public void HalfCycleIsExactAndUnknownOrFractionalQuantumIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticBufferTransferTiming(new(0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticBufferTransferTiming(new(101)));
        var timing = new ArithmeticBufferTransferTiming(new(2));
        Assert.AreEqual(new HardwareInstant(3), timing.AcceptAfterBufferWait(new(0)));
        Assert.AreEqual(new HardwareInstant(6), timing.AcceptFromReadyBuffer(new(0)));
    }

    [TestMethod]
    public void DurationAndInstantOverflowAreRejected()
    {
        var timing = new ArithmeticBufferTransferTiming(new(100));
        Assert.ThrowsExactly<OverflowException>(() => timing.AcceptFromReadyBuffer(new(ulong.MaxValue)));
        Assert.ThrowsExactly<OverflowException>(() => timing.AcceptAfterBufferWait(new(ulong.MaxValue)));
        var huge = new ArithmeticBufferTransferTiming(new(ulong.MaxValue - 1));
        Assert.ThrowsExactly<OverflowException>(() => huge.AcceptFromReadyBuffer(new(0)));
        Assert.ThrowsExactly<OverflowException>(() => huge.AcceptAfterBufferWait(new(0)));
    }

    [TestMethod]
    public void TransferCompletionCannotStartSuccessorBeforePredecessorIzop()
    {
        var timeline = new HardwareTimeline();
        var timing = new ArithmeticBufferTransferTiming(new(100));
        var pipeline = new ArithmeticPipelineControl();
        var log = new List<string>();
        pipeline.Commands.TryReceive(1);
        pipeline.Commands.TryReceive(2);
        pipeline.GrantCommandPermission();
        pipeline.TryAcceptOperand(true);
        pipeline.TryStartOperation();
        timeline.ScheduleAt(new(900), () => pipeline.GrantCommandPermission());
        timeline.ScheduleAt(timing.AcceptFromReadyBuffer(new(1000)), () =>
        {
            Assert.IsTrue(pipeline.TryAcceptOperand(true));
            Assert.IsFalse(pipeline.TryStartOperation());
            log.Add("PVR@1300");
        });
        timeline.ScheduleAt(new(1500), () =>
        {
            Assert.AreEqual(1U, pipeline.CompleteOperation());
            Assert.IsTrue(pipeline.TryStartOperation());
            log.Add("IZOP/SPOP@1500");
        });
        timeline.AdvanceTo(new(1500));
        CollectionAssert.AreEqual(new[] { "PVR@1300", "IZOP/SPOP@1500" }, log);
        // 100ns and transfer interval follow the selected specification; other
        // event instants are synthetic dependency vectors, not whole CPU timing.
    }
}
