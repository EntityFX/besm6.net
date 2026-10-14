using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareTimelineQueueTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(63)]
    [DataRow(64)]
    [DataRow(65)]
    [DataRow(257)]
    [DataRow(4096)]
    public void MixedCallbacksAndCancellationMatchIndependentSortedCalendar(int count)
    {
        var timeline = new HardwareTimeline();
        var random = new Random(617);
        var delivered = new List<(ulong Time, ulong Id)>();
        var registered = new List<(ulong Time, ulong Id, HardwareEventToken Token)>();
        for (int index = 0; index < count; index++)
        {
            ulong time = (ulong)random.Next(128);
            ulong id = (ulong)index + 1;
            HardwareEventToken token = index % 2 == 0
                ? timeline.ScheduleAt(new(time), () => delivered.Add((timeline.Now.Nanoseconds, id)))
                : timeline.ScheduleAt(new(time), static (context, value) =>
                {
                    var (clock, log) = ((HardwareTimeline, List<(ulong, ulong)>))context;
                    log.Add((clock.Now.Nanoseconds, value));
                }, (timeline, delivered), id);
            Assert.AreEqual(id, token.Id);
            registered.Add((time, id, token));
        }

        var cancelled = new HashSet<ulong>();
        foreach (var entry in registered)
            if (random.Next(4) == 0)
            {
                Assert.IsTrue(timeline.Cancel(entry.Token));
                Assert.IsFalse(timeline.Cancel(entry.Token));
                cancelled.Add(entry.Id);
            }
        var expected = registered.Where(entry => !cancelled.Contains(entry.Id))
            .OrderBy(entry => entry.Time).ThenBy(entry => entry.Id)
            .Select(entry => (entry.Time, entry.Id)).ToArray();
        HardwareInstant? earliest = expected.Length == 0 ? null : new HardwareInstant(expected[0].Time);
        Assert.AreEqual(earliest, timeline.NextEventTime);
        timeline.AdvanceTo(new(127));
        CollectionAssert.AreEqual(expected, delivered.ToArray());
        Assert.IsNull(timeline.NextEventTime);
        foreach (var entry in registered) Assert.IsFalse(timeline.Cancel(entry.Token));
        Assert.IsFalse(timeline.Cancel(default));
    }

    [TestMethod]
    public void CallbackCanInsertAtCurrentTimeAndCancelAnotherEqualTimeEvent()
    {
        var timeline = new HardwareTimeline();
        var log = new List<int>();
        HardwareEventToken cancelled = default;
        timeline.ScheduleAt(new(100), () =>
        {
            log.Add(1);
            Assert.IsTrue(timeline.Cancel(cancelled));
            timeline.ScheduleAt(timeline.Now, static (context, _) => ((List<int>)context).Add(4), log);
        });
        timeline.ScheduleAt(new(100), () => log.Add(2));
        cancelled = timeline.ScheduleAt(new(100), () => Assert.Fail("Cancelled callback was delivered"));
        timeline.ScheduleAt(new(200), () => log.Add(5));
        timeline.AdvanceTo(new(200));
        CollectionAssert.AreEqual(new[] { 1, 2, 4, 5 }, log);
        Assert.IsNull(timeline.NextEventTime);
    }

    [TestMethod]
    public void CallbackFailureKeepsEqualTimeSuccessorsAndAllowsNewEvents()
    {
        var timeline = new HardwareTimeline();
        var log = new List<int>();
        var cause = new ApplicationException("calendar receiver");
        var failed = timeline.ScheduleAt(new(100), static (context, _) => throw (Exception)context, cause);
        timeline.ScheduleAt(new(100), () => log.Add(1));
        timeline.ScheduleAt(new(200), () => log.Add(3));
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(300)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(failed, failure.Token);
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
        Assert.AreEqual(new HardwareInstant(100), timeline.NextEventTime);
        Assert.IsFalse(timeline.Cancel(failed));
        timeline.ScheduleAt(new(100), () => log.Add(2));
        timeline.AdvanceTo(new(300));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, log);
        Assert.AreEqual(new HardwareInstant(300), timeline.Now);
    }
}
