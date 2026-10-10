using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareTimelineBoundaryTests
{
    [TestMethod]
    public void ExecutionBoundaryLeavesSameTimeEventsQueuedAndReleasesCallbackGuards()
    {
        var machine = new MachineCore();
        var timeline = machine.HardwareTimeline;
        bool ready = false;
        var order = new List<int>();
        timeline.ScheduleAt(new(100), () => { order.Add(1); ready = true; });
        timeline.ScheduleAt(new(100), () => order.Add(2));
        timeline.ScheduleAt(new(200), () => order.Add(3));
        timeline.AdvanceUntil(new(300), () => ready);
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
        Assert.AreEqual(new HardwareInstant(100), timeline.NextEventTime);
        Assert.IsFalse(timeline.IsAdvancing);
        Assert.IsFalse(machine.Cpu.ExecutionProhibited);
        Assert.IsFalse(machine.Simulation.Clock.AdvancementProhibited);
        CollectionAssert.AreEqual(new[] { 1 }, order);
        timeline.AdvanceTo(new(300));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
    }

    [TestMethod]
    public void AnAlreadyReadyBoundaryDoesNotConsumeCurrentEventsOrMoveTime()
    {
        var timeline = new HardwareTimeline(new(100));
        bool ran = false;
        timeline.ScheduleAt(new(100), () => ran = true);
        timeline.AdvanceUntil(new(200), () => true);
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
        Assert.IsFalse(ran);
        timeline.AdvanceTo(new(100));
        Assert.IsTrue(ran);
        Assert.ThrowsExactly<ArgumentNullException>(() => timeline.AdvanceUntil(new(200), null!));
    }

    [TestMethod]
    public void BoundaryPredicateCannotAdvanceRecursivelyAndItsFailureReleasesGuards()
    {
        var timeline = new HardwareTimeline();
        var cause = new InvalidOperationException("Synthetic boundary failure");
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => timeline.AdvanceUntil(new(100), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => timeline.AdvanceTo(new(0)));
            throw cause;
        }));
        Assert.AreSame(cause, failure);
        Assert.IsFalse(timeline.IsAdvancing);
        Assert.AreEqual(new HardwareInstant(0), timeline.Now);
        timeline.AdvanceTo(new(100));
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
    }
}
