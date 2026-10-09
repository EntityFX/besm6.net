using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticCommandBufferTests
{
    [TestMethod]
    public void FourRegistersApplyBackpressureUntilOperandAcceptance()
    {
        var buffer = new ArithmeticCommandBuffer();
        foreach (uint code in new uint[] { 11, 22, 33, 44 }) Assert.IsTrue(buffer.TryReceive(code));
        Assert.IsFalse(buffer.CanReceive);
        Assert.IsFalse(buffer.TryReceive(55));
        Assert.IsTrue(buffer.TryPeek(out uint first));
        Assert.AreEqual(11U, first);
        Assert.AreEqual(4, buffer.Count); // PRB observation does not acknowledge PVR.
        Assert.IsFalse(buffer.TryReceive(55));
        Assert.AreEqual(11U, buffer.AcceptOperand());
        Assert.IsTrue(buffer.CanReceive);
        Assert.IsTrue(buffer.TryReceive(55));
        CollectionAssert.AreEqual(new uint[] { 22, 33, 44, 55 }, buffer.Snapshot());
    }

    [TestMethod]
    public void ReceiveAndIssueRingsWrapWithoutConfusingFullAndEmpty()
    {
        var buffer = new ArithmeticCommandBuffer();
        for (uint sequence = 0; sequence < 40; sequence += 4)
        {
            for (uint i = 0; i < 4; i++) Assert.IsTrue(buffer.TryReceive(sequence + i));
            Assert.AreEqual(buffer.IssueRegister, buffer.ReceiveRegister);
            Assert.IsFalse(buffer.CanReceive);
            for (uint i = 0; i < 4; i++) Assert.AreEqual(sequence + i, buffer.AcceptOperand());
            Assert.AreEqual(buffer.IssueRegister, buffer.ReceiveRegister);
            Assert.IsTrue(buffer.CanReceive);
            Assert.IsFalse(buffer.TryPeek(out _));
        }
    }

    [TestMethod]
    public void DelayedOperandRetainsCommandDespiteNewerArrivals()
    {
        var buffer = new ArithmeticCommandBuffer();
        Assert.IsTrue(buffer.TryReceive(0x1FFFF));
        Assert.IsTrue(buffer.TryPeek(out uint waiting));
        Assert.IsTrue(buffer.TryReceive(0)); // Zero is a valid stored code, not an empty marker.
        Assert.IsTrue(buffer.TryReceive(1));
        Assert.IsTrue(buffer.TryReceive(2));
        Assert.IsTrue(buffer.TryPeek(out uint stillWaiting));
        Assert.AreEqual(waiting, stillWaiting);
        Assert.AreEqual(0x1FFFFU, buffer.AcceptOperand());
        Assert.AreEqual(0U, buffer.AcceptOperand());
        Assert.AreEqual(1U, buffer.AcceptOperand());
        Assert.AreEqual(2U, buffer.AcceptOperand());
    }

    [TestMethod]
    public void InvalidCodeAndEmptyAcceptanceDoNotMoveRings()
    {
        var buffer = new ArithmeticCommandBuffer();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffer.TryReceive(0x20000));
        Assert.ThrowsExactly<InvalidOperationException>(() => buffer.AcceptOperand());
        Assert.AreEqual(0, buffer.Count);
        Assert.AreEqual(0, buffer.ReceiveRegister);
        Assert.AreEqual(0, buffer.IssueRegister);
        Assert.IsTrue(buffer.TryReceive(123));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffer.TryReceive(uint.MaxValue));
        Assert.AreEqual(123U, buffer.AcceptOperand());
    }

    [TestMethod]
    public void SnapshotDoesNotExposeRegisterStorage()
    {
        var buffer = new ArithmeticCommandBuffer();
        buffer.TryReceive(17);
        uint[] snapshot = buffer.Snapshot();
        snapshot[0] = 3;
        Assert.AreEqual(17U, buffer.AcceptOperand());
    }

    [TestMethod]
    public void OperandAcceptanceCanPrecedePreviousCompletionOnTimeline()
    {
        var buffer = new ArithmeticCommandBuffer();
        var timeline = new HardwareTimeline();
        var log = new List<string>();
        buffer.TryReceive(42);
        timeline.ScheduleAt(new(50), () => { Assert.AreEqual(42U, buffer.AcceptOperand()); log.Add("PVR"); });
        timeline.ScheduleAt(new(100), () => log.Add("IZOP previous"));
        timeline.AdvanceTo(new(50));
        Assert.IsTrue(buffer.CanReceive);
        Assert.AreEqual(0, buffer.Count);
        timeline.AdvanceTo(new(100));
        CollectionAssert.AreEqual(new[] { "PVR", "IZOP previous" }, log);
        // Times are synthetic. The source establishes ordering freedom, not these durations.
    }
}
