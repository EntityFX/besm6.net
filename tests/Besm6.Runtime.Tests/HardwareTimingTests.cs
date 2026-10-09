using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareTimingTests
{
    [TestMethod]
    public void TimeArithmeticPreservesNanosecondsAndRejectsOverflowAndNegativeElapsed()
    {
        Assert.AreEqual(new HardwareInstant(385), new HardwareInstant(110) + new HardwareDuration(275));
        Assert.AreEqual(new HardwareDuration(275), new HardwareInstant(385).ElapsedSince(new(110)));
        Assert.ThrowsExactly<OverflowException>(() => _ = new HardwareInstant(ulong.MaxValue) + new HardwareDuration(1));
        Assert.ThrowsExactly<OverflowException>(() => _ = new HardwareDuration(ulong.MaxValue) + new HardwareDuration(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HardwareInstant(110).ElapsedSince(new(111)));
        Assert.AreEqual(new HardwareDuration(110), BookTimingSpecification.Cycles(1));
        Assert.ThrowsExactly<OverflowException>(() => BookTimingSpecification.Cycles(ulong.MaxValue));
    }

    [TestMethod]
    public void EqualTimeCallbacksAndNestedSchedulingHaveStableOrder()
    {
        var timeline = new HardwareTimeline();
        var events = new List<string>();
        timeline.Schedule(new(330), () => { events.Add("first@" + timeline.Now.Nanoseconds); timeline.Schedule(new(0), () => events.Add("nested")); });
        timeline.ScheduleAt(new(330), () => events.Add("second"));
        timeline.Schedule(new(110), () => events.Add("early"));
        timeline.AdvanceTo(new(440));
        CollectionAssert.AreEqual(new[] { "early", "first@330", "second", "nested" }, events);
        Assert.AreEqual(new HardwareInstant(440), timeline.Now);
        Assert.IsNull(timeline.NextEventTime);
    }

    [TestMethod]
    public void CancelledExpiredDefaultAndForeignTokensDoNotConstrainTimeline()
    {
        var timeline = new HardwareTimeline();
        var other = new HardwareTimeline();
        var cancelled = timeline.Schedule(new(110), () => Assert.Fail("Cancelled callback executed"));
        var completed = timeline.Schedule(new(220), () => { });
        var foreign = other.Schedule(new(110), () => { });
        Assert.AreEqual(cancelled.Id, foreign.Id);
        Assert.AreNotEqual(cancelled, foreign);
        Assert.IsFalse(timeline.Cancel(foreign));
        Assert.IsFalse(timeline.Cancel(default));
        Assert.IsTrue(timeline.Cancel(cancelled));
        Assert.IsFalse(timeline.Cancel(cancelled));
        Assert.AreEqual(new HardwareInstant(220), timeline.NextEventTime);
        timeline.AdvanceTo(new(220));
        Assert.IsFalse(timeline.Cancel(completed));
        Assert.IsTrue(other.Cancel(foreign));
    }

    [TestMethod]
    public void CallbackMayCancelAndRescheduleWithoutAdvancingTimeRecursively()
    {
        var timeline = new HardwareTimeline();
        HardwareEventToken later = default;
        var events = new List<int>();
        timeline.Schedule(new(110), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => timeline.AdvanceTo(new(110)));
            Assert.IsTrue(timeline.Cancel(later));
            timeline.Schedule(new(55), () => events.Add(2));
            events.Add(1);
        });
        later = timeline.Schedule(new(220), () => Assert.Fail());
        timeline.AdvanceTo(new(330));
        CollectionAssert.AreEqual(new[] { 1, 2 }, events);
    }

    [TestMethod]
    public void CallbackFailurePreservesBoundaryAndRemainingEventsAndCanResume()
    {
        var timeline = new HardwareTimeline();
        var cause = new InvalidOperationException("synthetic callback failure");
        var failed = timeline.Schedule(new(110), () => throw cause);
        bool finished = false;
        timeline.Schedule(new(220), () => finished = true);
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => timeline.AdvanceTo(new(330)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(failed, failure.Token);
        Assert.AreEqual(new HardwareInstant(110), failure.Time);
        Assert.AreEqual(failure.Time, timeline.Now);
        Assert.IsFalse(timeline.Cancel(failed));
        Assert.IsFalse(finished);
        Assert.AreEqual(new HardwareInstant(220), timeline.NextEventTime);
        timeline.AdvanceTo(new(330));
        Assert.IsTrue(finished);
    }

    [TestMethod]
    public void InvalidSchedulingAndOverflowLeaveClockAndQueueUntouched()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 1));
        timeline.Schedule(new(1), () => { });
        Assert.ThrowsExactly<OverflowException>(() => timeline.Schedule(new(2), () => { }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timeline.ScheduleAt(new(0), () => { }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new(0)));
        Assert.ThrowsExactly<ArgumentNullException>(() => timeline.Schedule(new(0), null!));
        Assert.AreEqual(new HardwareInstant(ulong.MaxValue - 1), timeline.Now);
        Assert.AreEqual(new HardwareInstant(ulong.MaxValue), timeline.NextEventTime);
        timeline.AdvanceTo(new(ulong.MaxValue));
    }

    [TestMethod]
    public void ExplicitResourcesOverlapAndDependenciesDelayPublication()
    {
        var au = new HardwareResource(HardwareResourceKind.ArithmeticUnit);
        var bank = new HardwareResource(HardwareResourceKind.MemoryBank, 3);
        var calendar = new HardwareResourceCalendar(new[] { au, bank });
        var timeline = new HardwareTimeline();
        // Synthetic durations: this test proves the primitive, not BESM-6 arbitration.
        var first = calendar.Reserve(au, new(0), new(550));
        var parallel = calendar.Reserve(bank, new(0), new(2000));
        var dependent = calendar.Reserve(au, parallel.Finish, new(330));
        Assert.AreEqual(new HardwareReservation(new(0), new(550)), first);
        Assert.AreEqual(new HardwareReservation(new(0), new(2000)), parallel);
        Assert.AreEqual(new HardwareReservation(new(2000), new(2330)), dependent);
        int value = 7;
        timeline.ScheduleAt(dependent.Finish, () => value = 42);
        timeline.AdvanceTo(new(2329));
        Assert.AreEqual(7, value);
        timeline.AdvanceTo(new(2330));
        Assert.AreEqual(42, value);
        var sameResource = calendar.Reserve(bank, new(100), new(110));
        Assert.AreEqual(new HardwareInstant(2000), sameResource.Start);
        Assert.AreEqual(new HardwareInstant(2110), sameResource.Finish);
    }

    [TestMethod]
    public void InvalidResourcesAndOverflowDoNotChangeReservations()
    {
        var resource = new HardwareResource(HardwareResourceKind.ControlUnit);
        Assert.ThrowsExactly<ArgumentException>(() => new HardwareResourceCalendar(new[] { resource, resource }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HardwareResourceCalendar(new[] { new HardwareResource(HardwareResourceKind.MemoryBank, -1) }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HardwareResourceCalendar(new[] { new HardwareResource((HardwareResourceKind)99) }));
        var calendar = new HardwareResourceCalendar(new[] { resource });
        Assert.ThrowsExactly<ArgumentException>(() => calendar.AvailableAt(new(HardwareResourceKind.ArithmeticUnit)));
        Assert.ThrowsExactly<OverflowException>(() => calendar.Reserve(resource, new(ulong.MaxValue), new(1)));
        Assert.AreEqual(default(HardwareInstant), calendar.AvailableAt(resource));
    }

    [TestMethod]
    public void SeparateTimelineDoesNotAdvanceMachineClockOrChangeLegacyTiming()
    {
        var machine = new MachineCore();
        machine.Memory.Write(8, new((ulong)(uint)Opcode.Stop << 36));
        machine.Cpu.StartAt(8);
        var timeline = new HardwareTimeline();
        timeline.AdvanceTo(new(110000));
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(200, Besm6Timing.CyclesOf(Opcode.APlusX) * Besm6Timing.NanosecondsPerCycle);
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(new HardwareInstant(110000), timeline.Now);
    }

    // Nanoseconds independently calculated from visually checked appendix rows,
    // never from the production Cycles() helper or legacy Besm6Timing table.
    [TestMethod]
    [DataRow(Opcode.APlusX, 330UL, 550UL, 1210UL, 30800UL, 247)]
    [DataRow(Opcode.AMinusX, 330UL, 550UL, 1210UL, 30800UL, 247)]
    [DataRow(Opcode.XMinusA, 330UL, 550UL, 1210UL, 30800UL, 247)]
    [DataRow(Opcode.Amx, 330UL, 550UL, 1210UL, 30800UL, 247)]
    [DataRow(Opcode.AMulX, 330UL, 1650UL, 1980UL, 17820UL, 247)]
    [DataRow(Opcode.ADivX, 330UL, 5170UL, 5500UL, 19140UL, 247)]
    [DataRow(Opcode.Xta, 330UL, 220UL, 275UL, 330UL, 247)]
    [DataRow(Opcode.Aex, 330UL, 220UL, 275UL, 330UL, 247)]
    [DataRow(Opcode.Atx, 330UL, 330UL, 330UL, 330UL, 247)]
    [DataRow(Opcode.Arx, 330UL, 330UL, 660UL, 2970UL, 247)]
    [DataRow(Opcode.Apx, 330UL, 5830UL, 5830UL, 5830UL, 247)]
    [DataRow(Opcode.Aux, 330UL, 5830UL, 5830UL, 5830UL, 247)]
    [DataRow(Opcode.Acx, 330UL, 5940UL, 6160UL, 8580UL, 247)]
    [DataRow(Opcode.Anx, 330UL, 770UL, 3520UL, 8580UL, 247)]
    [DataRow(Opcode.Stx, 660UL, 550UL, 660UL, 660UL, 247)]
    [DataRow(Opcode.Xts, 660UL, 550UL, 660UL, 660UL, 247)]
    [DataRow(Opcode.Aax, 330UL, 440UL, 440UL, 440UL, 247)]
    [DataRow(Opcode.Aox, 330UL, 440UL, 440UL, 440UL, 247)]
    [DataRow(Opcode.Avx, 330UL, 440UL, 550UL, 2750UL, 247)]
    [DataRow(Opcode.EPlusX, 330UL, 330UL, 550UL, 14630UL, 247)]
    [DataRow(Opcode.EMinusX, 330UL, 330UL, 550UL, 14630UL, 247)]
    [DataRow(Opcode.Xtr, 330UL, 220UL, 275UL, 330UL, 248)]
    [DataRow(Opcode.Ntr, 330UL, 220UL, 275UL, 330UL, 248)]
    [DataRow(Opcode.Rte, 330UL, 220UL, 330UL, 330UL, 248)]
    [DataRow(Opcode.Yta, 330UL, 330UL, 550UL, 14630UL, 248)]
    [DataRow(Opcode.EPlusN, 330UL, 330UL, 550UL, 14630UL, 248)]
    [DataRow(Opcode.EMinusN, 330UL, 330UL, 550UL, 14630UL, 248)]
    [DataRow(Opcode.Ati, 1540UL, 330UL, 330UL, 330UL, 248)]
    [DataRow(Opcode.Sti, 1540UL, 330UL, 330UL, 330UL, 248)]
    [DataRow(Opcode.Ita, 660UL, 330UL, 330UL, 330UL, 248)]
    [DataRow(Opcode.Its, 990UL, 550UL, 660UL, 660UL, 248)]
    [DataRow(Opcode.Wtc, 1430UL, 330UL, 330UL, 330UL, 248)]
    public void PublishedAuEnvelopesMatchIndependentNanosecondVectors(Opcode opcode,
        ulong control, ulong min, ulong average, ulong max, int page)
    {
        Assert.IsTrue(BookTimingSpecification.TryGet(opcode, out var entry));
        Assert.AreEqual(new HardwareDuration(control), entry.ControlFirst);
        Assert.IsNull(entry.ControlSecond);
        Assert.AreEqual(new ArithmeticTimingEnvelope(new(min), new(average), new(max)), entry.Arithmetic);
        Assert.AreEqual(page, entry.PrintedPage);
    }

    [TestMethod]
    [DataRow(Opcode.Asx, 247)]
    [DataRow(Opcode.Asn, 248)]
    public void ShiftsHaveNoInventedAverage(Opcode opcode, int page)
    {
        Assert.IsTrue(BookTimingSpecification.TryGet(opcode, out var entry));
        Assert.AreEqual(new ArithmeticTimingEnvelope(new(440), null, new(7480)), entry.Arithmetic);
        Assert.AreEqual(page, entry.PrintedPage);
    }

    [TestMethod]
    [DataRow(Opcode.Mtj, 660UL, 248)]
    [DataRow(Opcode.JPlusM, 660UL, 248)]
    [DataRow(Opcode.Utc, 440UL, 248)]
    [DataRow(Opcode.Vtm, 440UL, 249)]
    [DataRow(Opcode.Utm, 440UL, 249)]
    [DataRow(Opcode.Uj, 770UL, 249)]
    [DataRow(Opcode.Vjm, 770UL, 249)]
    [DataRow(Opcode.Ij, 770UL, 249)]
    public void ControlOnlyRowsDoNotInventAuWork(Opcode opcode, ulong control, int page)
    {
        Assert.IsTrue(BookTimingSpecification.TryGet(opcode, out var entry));
        Assert.AreEqual(new HardwareDuration(control), entry.ControlFirst);
        Assert.IsNull(entry.Arithmetic);
        Assert.AreEqual(page, entry.PrintedPage);
    }

    [TestMethod]
    [DataRow(Opcode.Uza, 1650UL, 1320UL)]
    [DataRow(Opcode.U1a, 1650UL, 1320UL)]
    [DataRow(Opcode.Vzm, 770UL, 440UL)]
    [DataRow(Opcode.V1m, 770UL, 440UL)]
    [DataRow(Opcode.Vlm, 770UL, 440UL)]
    public void ConditionalRowsRetainBothPrintedAlternatives(Opcode opcode, ulong first, ulong second)
    {
        Assert.IsTrue(BookTimingSpecification.TryGet(opcode, out var entry));
        Assert.AreEqual(new HardwareDuration(first), entry.ControlFirst);
        Assert.AreEqual(new HardwareDuration(second), entry.ControlSecond);
        Assert.AreEqual(249, entry.PrintedPage);
        if (opcode is Opcode.Uza or Opcode.U1a)
            Assert.AreEqual(new ArithmeticTimingEnvelope(new(330), new(330), new(330)), entry.Arithmetic);
        else Assert.IsNull(entry.Arithmetic);
    }

    [TestMethod]
    [DataRow(Opcode.Stop)]
    [DataRow(Opcode.Mod)]
    [DataRow(Opcode.Ext)]
    [DataRow(Opcode.Op33)]
    [DataRow(Opcode.Op46)]
    [DataRow(Opcode.Op47)]
    [DataRow((Opcode)0x28)]
    [DataRow((Opcode)0x80)]
    public void UnspecifiedInstructionsHaveNoDefaultHardwareCost(Opcode opcode) =>
        Assert.IsFalse(BookTimingSpecification.TryGet(opcode, out _));
}
