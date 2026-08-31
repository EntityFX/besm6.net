using System;
using System.Collections.Generic;
using Besm6.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Уровень B, SuperPlan Task B1 — дискретное модельное время.
    /// Тесты фиксируют детерминированную семантику SimulationClock + EventScheduler:
    /// порядок, отмена, нулевая задержка, вложенное scheduling, запрет движения
    /// времени назад и воспроизводимость (один workload -> одна последовательность).
    /// Чистые тесты: без DateTime, thread scheduling и реальных часов.
    /// </summary>
    [TestClass]
    public class EventSchedulerTests
    {
        [TestMethod]
        public void SameTick_Events_ExecutedInRegistrationOrder()
        {
            var sched = new EventScheduler();
            var order = new List<string>();
            sched.Schedule(10, () => order.Add("first"));
            sched.Schedule(10, () => order.Add("second"));
            sched.Schedule(10, () => order.Add("third"));
            sched.AdvanceTo(10);
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, order);
        }

        [TestMethod]
        public void EarlierTime_ExecutedBeforeLater_EvenIfScheduledLater()
        {
            var sched = new EventScheduler();
            var order = new List<string>();
            sched.Schedule(20, () => order.Add("late"));   // time 20
            sched.Schedule(5, () => order.Add("early"));   // time 5
            sched.AdvanceTo(20);
            CollectionAssert.AreEqual(new[] { "early", "late" }, order);
        }

        [TestMethod]
        public void ZeroDelay_ExecutesOnAdvanceToCurrentTime()
        {
            var sched = new EventScheduler();
            int fired = 0;
            sched.Schedule(0, () => fired++);
            sched.AdvanceTo(0);
            Assert.AreEqual(1, fired);
        }

        [TestMethod]
        public void Cancel_PreventsExecution_AndReturnsTrue()
        {
            var sched = new EventScheduler();
            int fired = 0;
            var token = sched.Schedule(10, () => fired++);
            Assert.IsTrue(sched.Cancel(token));
            sched.AdvanceTo(10);
            Assert.AreEqual(0, fired);
        }

        [TestMethod]
        public void Cancel_Twice_SecondCallReturnsFalse()
        {
            var sched = new EventScheduler();
            var token = sched.Schedule(10, () => { });
            Assert.IsTrue(sched.Cancel(token));
            Assert.IsFalse(sched.Cancel(token));
        }

        [TestMethod]
        public void NestedScheduling_CallbacksCanSchedule_FollowingEvents()
        {
            var sched = new EventScheduler();
            var order = new List<string>();
            sched.Schedule(5, () =>
            {
                order.Add("a");
                sched.Schedule(5, () => order.Add("b")); // schedule at time 10 from within time 5
            });
            sched.AdvanceTo(10);
            CollectionAssert.AreEqual(new[] { "a", "b" }, order);
        }

        [TestMethod]
        public void AdvanceTo_Backward_Throws()
        {
            var sched = new EventScheduler();
            sched.AdvanceTo(100);
            Assert.Throws<InvalidOperationException>(() => sched.AdvanceTo(50));
        }

        [TestMethod]
        public void Now_ReflectsAdvanceTo_AndStaysMonotonic()
        {
            var sched = new EventScheduler();
            Assert.AreEqual(0UL, sched.Now);
            sched.AdvanceTo(42);
            Assert.AreEqual(42UL, sched.Now);
            sched.AdvanceTo(42); // no-op (same tick)
            Assert.AreEqual(42UL, sched.Now);
        }

        [TestMethod]
        public void Deterministic_TwoRuns_ProduceIdenticalTickSequence()
        {
            Func<EventScheduler, List<ulong>> run = sched =>
            {
                var seq = new List<ulong>();
                sched.Schedule(3, () => seq.Add(sched.Now));
                sched.Schedule(1, () => seq.Add(sched.Now));
                sched.Schedule(3, () => seq.Add(sched.Now));
                sched.AdvanceTo(10);
                return seq;
            };
            CollectionAssert.AreEqual(run(new EventScheduler()), run(new EventScheduler()));
        }

        [TestMethod]
        public void EventsAfterTargetTick_AreNotExecuted()
        {
            var sched = new EventScheduler();
            int fired = 0;
            sched.Schedule(50, () => fired++);
            sched.AdvanceTo(10); // before the event's time
            Assert.AreEqual(0, fired);
            Assert.AreEqual(10UL, sched.Now);
        }

        [TestMethod]
        public void Schedule_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EventScheduler().Schedule(0, null!));
        }
    }

    /// <summary>Тесты на сам <see cref="SimulationClock"/> (монотонность, запрет отката).</summary>
    [TestClass]
    public class SimulationClockTests
    {
        [TestMethod]
        public void Tick_IsInitial_ByDefault()
        {
            Assert.AreEqual(0UL, new SimulationClock().Tick);
            Assert.AreEqual(7UL, new SimulationClock(7).Tick);
        }

        [TestMethod]
        public void Advance_MovesForward()
        {
            var clock = new SimulationClock();
            clock.Advance(5);
            clock.Advance(3);
            Assert.AreEqual(8UL, clock.Tick);
        }

        [TestMethod]
        public void AdvanceTo_Backward_Throws()
        {
            var clock = new SimulationClock(10);
            Assert.Throws<InvalidOperationException>(() => clock.AdvanceTo(9));
        }

        [TestMethod]
        public void ExposesISimulationClock()
        {
            ISimulationClock clock = new SimulationClock();
            Assert.AreEqual(0UL, clock.Tick);
        }
    }
}
