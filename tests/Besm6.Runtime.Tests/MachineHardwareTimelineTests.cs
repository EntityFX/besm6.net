using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class MachineHardwareTimelineTests
{
    private static MachineCore LoadedMachine()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), stop")) }, 1);
        return machine;
    }

    [TestMethod]
    public void HardwareCallbackCannotRunCpuOrEitherClockRecursively()
    {
        var machine = LoadedMachine();
        var timeline = machine.HardwareTimeline;
        timeline.Schedule(new(100), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.RunInstructions(10));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Scheduler.AdvanceTo(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => ((SimulationClock)machine.Clock).Advance(1));
            Assert.ThrowsExactly<InvalidOperationException>(() => timeline.AdvanceTo(new(100)));
            Assert.AreEqual(1U, machine.Cpu.GetK());
        });
        timeline.AdvanceTo(new(100));
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
    }

    [TestMethod]
    public void InstructionSchedulerCannotAdvanceHardwareEvenOnFirstAccess()
    {
        var machine = LoadedMachine();
        machine.Scheduler.Schedule(0, () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.HardwareTimeline.AdvanceTo(new(0)));
            machine.HardwareTimeline.Schedule(new(0), () => { }); // Scheduling remains allowed.
        });
        machine.Scheduler.AdvanceTo(0);
        machine.HardwareTimeline.AdvanceTo(new(0));
        Assert.IsNull(machine.HardwareTimeline.NextEventTime);
        Assert.IsFalse(machine.Step());
    }

    [TestMethod]
    public void CallbackFailureKeepsCauseAndRestoresAllExecutionGuards()
    {
        var machine = LoadedMachine();
        var cause = new InvalidOperationException("hardware callback failure");
        machine.HardwareTimeline.Schedule(new(50), () => throw cause);
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => machine.HardwareTimeline.AdvanceTo(new(100)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(new HardwareInstant(50), machine.HardwareTimeline.Now);
        machine.Scheduler.AdvanceTo(0);
        Assert.IsFalse(machine.Step());
        machine.HardwareTimeline.AdvanceTo(new(100));
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ResetCpuPreservesHardwareTimeAndQueuedEvents()
    {
        var machine = LoadedMachine();
        var timeline = machine.HardwareTimeline;
        int calls = 0;
        timeline.ScheduleAt(new(200), () => calls++);
        timeline.AdvanceTo(new(100));
        machine.Step();
        machine.ResetCpu();
        Assert.AreSame(timeline, machine.HardwareTimeline);
        Assert.AreEqual(new HardwareInstant(100), timeline.Now);
        Assert.AreEqual(new HardwareInstant(200), timeline.NextEventTime);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreNotEqual(0UL, machine.Memory.Read(1).Value);
        timeline.AdvanceTo(new(200));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void CallbacksMayScheduleCancelAndInstallDiagnosticHooks()
    {
        var machine = LoadedMachine();
        var timeline = machine.HardwareTimeline;
        int events = 0, traces = 0;
        var cancelled = timeline.ScheduleAt(new(200), () => Assert.Fail());
        timeline.ScheduleAt(new(100), () =>
        {
            Assert.IsTrue(timeline.Cancel(cancelled));
            timeline.Schedule(new(0), () => events++);
            machine.StepTrace = (_, _) => traces++;
        });
        timeline.AdvanceTo(new(200));
        Assert.AreEqual(1, events);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(1, traces);
    }
}
