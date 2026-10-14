using Besm6.Runtime.Timing.Calendar;
using System.Text.Json;
using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.State;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Machine;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Tests;

[TestClass]
public sealed class MachineExecutionModelTests
{
    private static HardwareMachineConfiguration Timing(ulong control = 300) => new(
        new(new(2000), new(900), new(1500), MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank),
        new(100), new(100), Enum.GetValues<Opcode>().ToDictionary(x => x, _ => new HardwareDuration(control)),
        new(new(20000), new(700), new(900), new(100), new(50), new(500), new(30), new(0)),
        Enum.GetValues<Opcode>().ToDictionary(x => x, _ => new HardwareDuration(1200)));
    private static MachineCore Machine(string instruction = "xta 20, stop")
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(Assembler.Legacy.Assembler.Asm(instruction)), false, false));
        machine.MappedMemory.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(12345), true, true));
        machine.Cpu.StartAt(8); return machine;
    }
    private static string State(MachineCore machine) => JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State));

    [TestMethod]
    [DataRow("xta 20, stop")] [DataRow("a+x 20, stop")] [DataRow("mod 1, stop")] [DataRow("vtm 2(7), stop")]
    public void PublicOwnersPreserveGuestStateAndKeepClockUnits(string code)
    {
        var functional = Machine(code); var hardware = Machine(code); hardware.UseHardwareExecution(Timing());
        var expected = functional.RunInstructions(10); var actual = hardware.RunInstructions(10);
        Assert.IsTrue(expected.Outcome.Stopped); Assert.IsTrue(actual.Outcome.Stopped);
        Assert.AreEqual(expected.Outcome.Instructions, actual.Outcome.Instructions); Assert.AreEqual(State(functional), State(hardware));
        Assert.AreEqual(2UL, functional.Clock.Tick); Assert.AreEqual(0UL, hardware.Clock.Tick);
        Assert.IsTrue(hardware.HardwareTimeline.Now.Nanoseconds > 0); Assert.IsNull(actual.Statistics);
    }

    [TestMethod]
    public void DirectCpuAndHostedLoopCannotBypassHardwareOwner()
    {
        var machine = Machine(); machine.UseHardwareExecution(Timing());
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ExecutionLoop(machine).Run());
        Assert.IsFalse(machine.Step()); Assert.IsTrue(machine.Step()); Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void SwitchingOwnersPreservesRegistersAndBothTimes()
    {
        var machine = Machine(); var timing = Timing(); machine.UseHardwareExecution(timing);
        Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped); ulong nanoseconds = machine.HardwareTimeline.Now.Nanoseconds;
        string state = State(machine); machine.UseFunctionalExecution(); Assert.AreEqual(state, State(machine));
        Assert.AreEqual(nanoseconds, machine.HardwareTimeline.Now.Nanoseconds);
        machine.Cpu.StartAt(8); Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped); Assert.AreEqual(2UL, machine.Clock.Tick);
        machine.UseHardwareExecution(timing); machine.Cpu.StartAt(8);
        Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped); Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.IsTrue(machine.HardwareTimeline.Now.Nanoseconds > nanoseconds);
    }

    [TestMethod]
    public void OwnerChangesRejectPendingEventsAndCallbacks()
    {
        var machine = Machine(); var timing = Timing(); var token = machine.Scheduler.Schedule(1, () => { });
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.UseHardwareExecution(timing));
        Assert.AreEqual(MachineExecutionModel.Functional, machine.ExecutionModel); Assert.IsTrue(machine.Scheduler.Cancel(token));
        machine.UseHardwareExecution(timing);
        var pending = machine.HardwareTimeline.ScheduleAt(new(5000), () => { });
        Assert.ThrowsExactly<InvalidOperationException>(machine.UseFunctionalExecution); Assert.IsTrue(machine.HardwareTimeline.Cancel(pending));
        machine.Cpu.InstructionExecuted += _ =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(machine.UseFunctionalExecution);
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.RunInstructions(10));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Step());
        };
        Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped);
    }

    [TestMethod]
    public void ResetPreservesSelectedOwnerMemoryAndTime()
    {
        var machine = Machine(); machine.UseHardwareExecution(Timing()); Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped);
        var time = machine.HardwareTimeline.Now; machine.ResetCpu(); Assert.AreEqual(MachineExecutionModel.Hardware, machine.ExecutionModel);
        Assert.AreEqual(time, machine.HardwareTimeline.Now); Assert.AreEqual(12345UL, machine.MappedMemory!.Read(16).Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step()); machine.Cpu.StartAt(8);
        Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped);
    }

    private sealed class FakeTime : IExecutionTimeSource
    {
        public double Now = 7; public int Sleeps; public bool Throw;
        public double ElapsedSeconds => Throw ? throw new AssertFailedException("Max touched host timer") : Now;
        public void Sleep(int milliseconds) { Sleeps++; Now += milliseconds / 1000.0; }
        public void Spin() { Now += 0.000001; }
    }

    [TestMethod]
    public void HardwareVirtualPreservesModelTimeAndStateWithoutHostPacing()
    {
        var max = Machine(); var virtualMachine = Machine();
        max.UseHardwareExecution(Timing()); virtualMachine.UseHardwareExecution(Timing());
        var expected = max.RunInstructions(10);
        var actual = virtualMachine.RunHardwareInstructions(10, ExecutionSpeed.Virtual, false, new FakeTime { Throw = true });
        Assert.IsTrue(actual.Outcome.Stopped);
        Assert.AreEqual(expected.Outcome.Instructions, actual.Outcome.Instructions);
        Assert.AreEqual(State(max), State(virtualMachine));
        Assert.AreEqual(max.HardwareTimeline.Now, virtualMachine.HardwareTimeline.Now);
        Assert.IsNull(actual.Statistics);
    }

    [TestMethod]
    public void HardwareOriginalUsesNanosecondsAndRelativeRunDeadlines()
    {
        var original = Machine(); original.UseHardwareExecution(Timing(1_000_000_000)); var time = new FakeTime();
        string state = State(original); var result = original.RunHardwareInstructions(10, ExecutionSpeed.Original, true, time);
        Assert.IsTrue(result.Outcome.Stopped); Assert.IsTrue(time.Sleeps > 0); Assert.IsNull(result.Statistics!.ModelCycles);
        Assert.IsTrue(result.Statistics.ModelSeconds > 2); Assert.IsTrue(result.Statistics.OriginalTempoMet);
        Assert.AreEqual(result.Statistics.ModelSeconds!.Value, result.Statistics.ElapsedSeconds, 0.000002);
        original.Cpu.StartAt(8); var second = original.RunHardwareInstructions(10, ExecutionSpeed.Original, true, time);
        Assert.IsTrue(second.Statistics!.ElapsedSeconds < 2.01); Assert.IsTrue(second.Statistics.OriginalTempoMet);
        var max = Machine(); max.UseHardwareExecution(Timing(1_000_000_000));
        Assert.IsTrue(max.RunHardwareInstructions(10, ExecutionSpeed.Max, false, new FakeTime { Throw = true }).Outcome.Stopped);
        Assert.AreEqual(State(max), State(original));
    }

    [TestMethod]
    public void ModelDiagnosticsObserveRetirementBeforeLaterCalendarEvents()
    {
        var machine = Machine(); machine.UseHardwareExecution(Timing()); var order = new List<string>();
        machine.StepTrace = (_, _) => order.Add("step"); machine.RegisterTrace = (_, _) => order.Add("register"); machine.BeginRegisterTrace();
        machine.HardwareTimeline.ScheduleAt(new(1200), () => order.Add("event"));
        Assert.IsFalse(machine.Step()); Assert.IsTrue(order.Contains("step")); Assert.IsTrue(order.Contains("register"));
        // XTA waits for its memory bank, so the 1200 ns event precedes retirement.
        Assert.AreEqual("event", order[0]); Assert.AreEqual("step", order[1]);
    }

    [TestMethod]
    public void LimitAndFailedAttemptsRemainBoundedAndResumable()
    {
        var machine = Machine(); machine.UseHardwareExecution(Timing()); var first = machine.RunInstructions(1);
        Assert.IsTrue(first.Outcome.LimitExceeded); Assert.AreEqual(1L, first.Outcome.Instructions);
        Assert.IsTrue(machine.RunInstructions(1).Outcome.Stopped);
        var faults = Machine("mod 200, stop"); faults.UseHardwareExecution(Timing());
        var failure = faults.RunInstructions(1); Assert.IsTrue(failure.Outcome.LimitExceeded); Assert.AreEqual(0L, failure.Outcome.Instructions);
        Assert.AreEqual(0UL, faults.Clock.Tick);
    }
    [TestMethod]
    public void UnsupportedProfileAndReplacementTimingDoNotChangeSelectedState()
    {
        var dubna = new MachineCore(); Assert.ThrowsExactly<InvalidOperationException>(() => dubna.UseHardwareExecution(Timing()));
        Assert.AreEqual(MachineExecutionModel.Functional, dubna.ExecutionModel);
        var machine = Machine(); var selected = Timing(); machine.UseHardwareExecution(selected); string before = State(machine);
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.UseHardwareExecution(Timing()));
        Assert.AreEqual(before, State(machine)); Assert.AreEqual(MachineExecutionModel.Hardware, machine.ExecutionModel);
        Assert.IsTrue(machine.RunInstructions(10).Outcome.Stopped);
    }

    [TestMethod]
    public void OriginalAndMaxHaveTheSameCalendarEventAndInstructionOrder()
    {
        List<string> Run(ExecutionSpeed speed)
        {
            var machine = Machine(); machine.UseHardwareExecution(Timing(1_000_000)); var log = new List<string>();
            machine.HardwareTimeline.ScheduleAt(new(500), () => { log.Add("before"); machine.Cpu.SetM(6, 7); });
            machine.HardwareTimeline.ScheduleAt(new(1_000_000), () => log.Add("during"));
            machine.Cpu.InstructionExecuted += opcode => log.Add(opcode.ToString());
            var result = machine.RunHardwareInstructions(10, speed, true, new FakeTime());
            Assert.IsTrue(result.Outcome.Stopped); log.Add(State(machine)); log.Add(machine.HardwareTimeline.Now.Nanoseconds.ToString());
            return log;
        }
        CollectionAssert.AreEqual(Run(ExecutionSpeed.Max), Run(ExecutionSpeed.Original));
    }

}
