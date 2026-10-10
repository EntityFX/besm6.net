using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareProcessorDriverTests
{
    private static MachineCore Machine(string code = "xta 10, stop")
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(code))], 1);
        machine.Memory.Write(8, new(123));
        return machine;
    }

    [TestMethod]
    public void ReadinessBoundarySeparatesOperandArrivalCpuPublicationAndSameTimeDeviceEvents()
    {
        var machine = Machine();
        var model = machine.HardwareModel;
        var events = new List<string>();
        machine.Cpu.InstructionExecuted = _ =>
        {
            Assert.AreEqual(new HardwareInstant(100), model.Timeline.Now);
            Assert.AreEqual(321UL, machine.Cpu.GetA().Value);
            Assert.AreEqual(0UL, model.CompletedInstructions);
            events.Add("CPU diagnostic");
        };
        var command = model.PrepareNextInstruction();
        model.Timeline.ScheduleAt(new(100), () =>
        {
            Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
            machine.Memory.Write(8, new(321));
            events.Add("operand arrived");
        });
        model.ScheduleCompletionAt(command, new(100));
        model.InstructionCompleted = result =>
        {
            Assert.AreEqual(1UL, model.CompletedInstructions);
            Assert.AreEqual(HardwareInstructionStatus.Completed, result.Status);
            events.Add("model completion");
        };
        model.Timeline.ScheduleAt(new(100), () =>
        {
            Assert.AreEqual(321UL, machine.Cpu.GetA().Value);
            machine.Memory.Write(8, new(987));
            events.Add("later device event");
        });
        Assert.IsNull(model.AdvanceTo(new(99)));
        Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
        var outcome = model.AdvanceTo(new(100));
        Assert.AreEqual(new HardwareInstant(100), outcome!.Value.Time);
        Assert.AreEqual(321UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(987UL, machine.Memory.Read(8).Value);
        CollectionAssert.AreEqual(new[] { "operand arrived", "CPU diagnostic", "model completion", "later device event" }, events);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ACommandWithoutACompletionSignalNeverGetsAnAverageDeadline()
    {
        var machine = Machine("a/x 10, stop");
        machine.Cpu.SetA(Word48.FromDouble(1).Value);
        var model = machine.HardwareModel;
        var before = JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State));
        model.PrepareNextInstruction();
        Assert.IsNull(model.AdvanceNextEvent());
        Assert.AreEqual(new HardwareInstant(0), model.Timeline.Now);
        Assert.IsNull(model.AdvanceTo(new(1_000_000)));
        Assert.AreEqual(before, JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsTrue(model.HasPendingInstruction);
    }

    [TestMethod]
    public void CancelledSignalCanBeRescheduledAndInvalidScheduleKeepsThePreviousSignal()
    {
        var model = Machine().HardwareModel;
        var command = model.PrepareNextInstruction();
        var old = model.ScheduleCompletionAt(command, new(100));
        Assert.IsNull(model.AdvanceTo(new(50)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.ScheduleCompletionAt(command, new(49)));
        Assert.AreEqual(new HardwareInstant(100), model.Timeline.NextEventTime);
        var replacement = model.ScheduleCompletionAt(command, new(200));
        Assert.IsFalse(model.Timeline.Cancel(old));
        Assert.IsTrue(model.Timeline.Cancel(replacement));
        Assert.IsNull(model.AdvanceTo(new(300)));
        model.ScheduleCompletionAt(command, new(400));
        Assert.AreEqual(new HardwareInstant(400), model.AdvanceNextEvent()!.Value.Time);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void StaleForeignAndDuplicateReadinessSignalsCannotCompleteAnotherCommand()
    {
        var model = Machine("vtm 1(1), stop").HardwareModel;
        var other = Machine().HardwareModel;
        var first = model.PrepareNextInstruction();
        var foreign = other.PrepareNextInstruction();
        Assert.IsFalse(model.TryIndicateCompletionReady(in foreign));
        Assert.IsFalse(model.TryRequestCancellation(in foreign));
        Assert.ThrowsExactly<InvalidOperationException>(() => model.ScheduleCompletionAt(foreign, new(10)));
        Assert.IsTrue(model.TryIndicateCompletionReady(in first));
        Assert.IsFalse(model.TryIndicateCompletionReady(in first));
        Assert.IsFalse(model.AdvanceNextEvent()!.Value.Stopped);
        var second = model.PrepareNextInstruction();
        Assert.IsFalse(model.TryIndicateCompletionReady(in first));
        Assert.IsFalse(model.TryRequestCancellation(in first));
        Assert.AreEqual(2UL, second.Sequence);
        Assert.IsTrue(model.TryIndicateCompletionReady(in second));
        Assert.IsTrue(model.AdvanceNextEvent()!.Value.Stopped);
        Assert.AreEqual(2UL, model.IssuedInstructions);
        Assert.AreEqual(2UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void CalendarCallbackFailureKeepsThePreparedCommandAndCanResumeAtReadiness()
    {
        var machine = Machine();
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        var cause = new InvalidOperationException("Synthetic device failure");
        model.Timeline.ScheduleAt(new(50), () => throw cause);
        model.ScheduleCompletionAt(command, new(100));
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(100)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(new HardwareInstant(50), model.Timeline.Now);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsTrue(model.HasPendingInstruction);
        Assert.IsFalse(machine.Cpu.ExecutionProhibited);
        Assert.IsFalse(model.AdvanceNextEvent()!.Value.Stopped);
        Assert.AreEqual(123UL, machine.Cpu.GetA().Value);
    }

    [TestMethod]
    public void FailureAfterReadinessDoesNotLoseTheSignalOrReexecuteItsCallback()
    {
        var model = Machine().HardwareModel;
        var command = model.PrepareNextInstruction();
        int signals = 0;
        model.Timeline.ScheduleAt(new(100), () =>
        {
            signals++;
            Assert.IsTrue(model.TryIndicateCompletionReady(in command));
            throw new InvalidOperationException("Synthetic failure after signal");
        });
        Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(100)));
        Assert.IsNotNull(model.AdvanceNextEvent());
        Assert.AreEqual(1, signals);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void DeviceCallbackAfterCompletionFailsWithTheCpuAlreadyAccounted()
    {
        var model = Machine().HardwareModel;
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        model.Timeline.ScheduleAt(new(100), () => throw new InvalidOperationException("Synthetic post-completion failure"));
        Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(100)));
        Assert.IsFalse(model.HasPendingInstruction);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.LastOutcome!.Value.Status);
        Assert.IsNull(model.AdvanceNextEvent());
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void ObserverFailurePreservesCompletionAndRejectsNestedExecutionAndClockAdvance()
    {
        var machine = Machine();
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        var cause = new InvalidOperationException("Synthetic observer failure");
        model.InstructionCompleted = _ =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceNextEvent());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.PrepareNextInstruction());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Cpu.Step());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Timeline.AdvanceTo(model.Timeline.Now));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Scheduler.AdvanceTo(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Simulation.Clock.Advance(1));
            throw cause;
        };
        model.ScheduleCompletionAt(command, new(100));
        var failure = Assert.ThrowsExactly<HardwareExecutionObserverException>(() => model.AdvanceTo(new(100)));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(HardwareInstructionStatus.Completed, failure.Outcome.Status);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.IsFalse(model.IsDriving);
        Assert.IsFalse(machine.Cpu.ExecutionProhibited);
        Assert.IsFalse(machine.Simulation.Scheduler.AdvancementProhibited);
        Assert.IsNull(model.AdvanceNextEvent());
    }

    [TestMethod]
    public void ExtracodeMayScheduleButCannotAdvanceEitherCalendarWhilePublishingCpuState()
    {
        var machine = Machine("*50 1, stop");
        var model = machine.HardwareModel;
        machine.Cpu.ExtracodeDispatch = _ =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Timeline.AdvanceTo(new(200)));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Scheduler.AdvanceTo(1));
            Assert.ThrowsExactly<InvalidOperationException>(() => machine.Simulation.Clock.Advance(1));
            model.Timeline.ScheduleAt(new(200), () => { });
            return true;
        };
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(new HardwareInstant(100), model.Timeline.Now);
        Assert.AreEqual(new HardwareInstant(200), model.Timeline.NextEventTime);
    }

    [TestMethod]
    public void CancellationFromAnExecutingHandlerCannotDiscardAnAlreadyPublishingCommand()
    {
        var machine = Machine("*50 1, stop");
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        machine.Cpu.ExtracodeDispatch = _ =>
        {
            Assert.IsFalse(model.TryRequestCancellation(in command));
            machine.Cpu.SetA(777);
            return true;
        };
        model.ScheduleCompletionAt(command, new(100));
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(777UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(0UL, model.CancelledInstructions);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetCancelsCpuWorkWhilePreservingOtherEventsAndCanRestart(bool insideCallback)
    {
        var machine = Machine("vtm 7(1), stop");
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        int devices = 0;
        model.Timeline.ScheduleAt(new(75), () => devices++);
        if (insideCallback) model.Timeline.ScheduleAt(new(50), machine.ResetCpu);
        else machine.ResetCpu();
        var cancelled = model.AdvanceTo(new(100));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, cancelled!.Value.Status);
        Assert.AreEqual(new HardwareInstant(insideCallback ? 50UL : 0UL), cancelled.Value.Time);
        Assert.AreEqual(1, devices);
        Assert.AreEqual(0u, machine.Cpu.GetM(1));
        Assert.AreEqual(1UL, model.CancelledInstructions);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsFalse(model.TryIndicateCompletionReady(in command));
        var restarted = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(restarted, new(200));
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(7u, machine.Cpu.GetM(1));
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void CancellationSignalWinsOverReadinessAndRemovesTheUnusedDeadline()
    {
        var machine = Machine("atx 10, stop");
        machine.Cpu.SetA(999);
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(200));
        model.Timeline.ScheduleAt(new(100), () =>
        {
            Assert.IsTrue(model.TryIndicateCompletionReady(in command));
            Assert.IsTrue(model.TryRequestCancellation(in command));
        });
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(123UL, machine.Memory.Read(8).Value);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsNull(model.Timeline.NextEventTime);
    }

    [TestMethod]
    public void SelfModificationChangesTheNextHalfButNotTheAlreadyPreparedCommand()
    {
        var machine = Machine("vtm 3(1), vtm 4(2)");
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        model.Timeline.ScheduleAt(new(50), () => machine.Memory.Write(1,
            new(Besm6.Asm.Assembler.Asm("vtm 7(1), vtm 11(2)"))));
        model.ScheduleCompletionAt(command, new(100));
        model.AdvanceTo(new(100));
        Assert.AreEqual(3u, machine.Cpu.GetM(1));
        var right = model.PrepareNextInstruction();
        Assert.IsTrue(right.RightHalf);
        model.ScheduleCompletionAt(right, new(200));
        model.AdvanceTo(new(200));
        Assert.AreEqual(9u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    public void ArithmeticStageCanProvidePermissionBeforeSharedCpuPublication()
    {
        var machine = Machine("a+x 10, stop");
        var one = new Word48(Convert.ToUInt64("4050000000000000", 8));
        var small = new Word48((24UL << 41) | (1UL << 39));
        machine.Cpu.SetA(one.Value);
        machine.Memory.Write(8, small);
        var model = machine.HardwareModel;
        var unit = machine.CreateArithmeticUnitStages(new(100));
        var command = model.PrepareNextInstruction();
        Assert.IsTrue(unit.TryReceiveCommand(1));
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(PreparedArithmeticOperation.Add(small, 0), true));
        Assert.IsTrue(unit.TryStartOperation());
        // Synthetic completion instant: this verifies the integration boundary,
        // not an operand-dependent historical addition duration.
        model.Timeline.ScheduleAt(new(500), () =>
        {
            Assert.AreEqual(one, machine.Cpu.GetA());
            unit.CompleteOperation();
            Assert.IsTrue(unit.TryReadOutput(out var result));
            Assert.AreEqual(one.Value | 1UL, result.A.Value);
            Assert.IsTrue(model.TryIndicateCompletionReady(in command));
        });
        model.AdvanceTo(new(499));
        Assert.AreEqual(one, machine.Cpu.GetA());
        model.AdvanceNextEvent();
        Assert.IsTrue(unit.TryReadOutput(out var output));
        Assert.AreEqual(output.A, machine.Cpu.GetA());
        Assert.AreEqual(output.Y, machine.Cpu.GetY());
    }

    [TestMethod]
    public void SupervisorFetchFaultIsLatchedUntilTheExplicitCompletionBoundary()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new(ControlUnitFlags.None);
        machine.Cpu.Supervisor.Mode = SupervisorMode.Mathematical;
        machine.Cpu.StartAt(8); // Closed RP0 causes a fetch protection fault.
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        Assert.IsNull(command.Instruction);
        Assert.AreEqual(0UL, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(8u, machine.Cpu.GetK());
        model.ScheduleCompletionAt(command, new(100));
        Assert.IsNull(model.AdvanceTo(new(99)));
        Assert.AreEqual(0UL, machine.Cpu.Supervisor.InternalInterrupts);
        var outcome = model.AdvanceNextEvent();
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, outcome!.Value.Status);
        Assert.AreEqual(1UL << 13, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(0x140u, machine.Cpu.GetK());
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void GuestExceptionKeepsItsInterceptionAndStackCorrectionWithoutAnInventedTick()
    {
        var machine = Machine("a/x (17), stop");
        machine.Cpu.SetA(1);
        machine.Cpu.SetM(15, 9);
        machine.Cpu.InterceptCount = 1; machine.Cpu.InterceptAddr = 4;
        machine.Memory.Write(8, Word48.Zero);
        int observed = 0;
        machine.Cpu.InstructionExecuted = _ => observed++;
        var model = machine.HardwareModel;
        var outcomes = new List<HardwareInstructionStatus>();
        model.InstructionCompleted = result => outcomes.Add(result.Status);
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        var failure = Assert.ThrowsExactly<ProcessorException>(() => model.AdvanceNextEvent());
        Assert.AreEqual("Division by zero", failure.Message);
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, model.LastOutcome!.Value.Status);
        Assert.IsTrue(machine.Cpu.Intercept(failure.Message));
        machine.Cpu.StackCorrection();
        machine.Cpu.CanonPost(machine.Cpu.GetK(), machine.Cpu.RightInstruction);
        Assert.AreEqual(9u, machine.Cpu.GetM(15));
        Assert.AreEqual(1, observed);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.IsFalse(model.HasPendingInstruction);
        CollectionAssert.AreEqual(new[] { HardwareInstructionStatus.GuestFault }, outcomes);
    }

    [TestMethod]
    public void ModelDoesNotDeliverEventsForAnotherOwnersPendingCpuCommand()
    {
        var machine = Machine();
        var model = machine.HardwareModel;
        var foreign = machine.Cpu.PrepareInstruction();
        int events = 0;
        model.Timeline.ScheduleAt(new(0), () => events++);
        Assert.ThrowsExactly<InvalidOperationException>(() => model.PrepareNextInstruction());
        Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceNextEvent());
        Assert.AreEqual(0, events);
        machine.Cpu.CancelInstruction(in foreign);
        var own = model.PrepareNextInstruction();
        Assert.AreEqual(1, events);
        Assert.IsTrue(model.TryRequestCancellation(in own));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
    }

    [TestMethod]
    public void BackwardAdvancementLeavesReadinessPendingAndDoesNotExecuteCpu()
    {
        var model = Machine().HardwareModel;
        model.Timeline.AdvanceTo(new(100));
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(200));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.AdvanceTo(new(99)));
        Assert.IsTrue(model.HasPendingInstruction);
        Assert.AreEqual(new HardwareInstant(100), model.Timeline.Now);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(new HardwareInstant(200), model.AdvanceNextEvent()!.Value.Time);
    }

    [TestMethod]
    public void AFailedObserverPreservesTheUnderlyingGuestFailureAsASeparateCause()
    {
        var machine = Machine("a/x 10, stop");
        machine.Memory.Write(8, Word48.Zero);
        var model = machine.HardwareModel;
        var cause = new InvalidOperationException("Synthetic fault observer failure");
        model.InstructionCompleted = _ => throw cause;
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        var failure = Assert.ThrowsExactly<HardwareExecutionObserverException>(() => model.AdvanceNextEvent());
        Assert.AreSame(cause, failure.InnerException);
        Assert.IsInstanceOfType<ProcessorException>(failure.ExecutionFailure);
        Assert.AreEqual("Division by zero", failure.ExecutionFailure!.Message);
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, failure.Outcome.Status);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsFalse(model.HasPendingInstruction);
        Assert.IsFalse(model.IsDriving);
    }

    [TestMethod]
    public void HandleReportsTheNormalizedFetchedAddress()
    {
        var machine = Machine();
        machine.Cpu.K = 0x8001;
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        Assert.AreEqual(1u, command.Address);
        Assert.AreEqual(1u, machine.Cpu.GetK());
        Assert.IsTrue(model.TryRequestCancellation(in command));
        model.AdvanceNextEvent();
    }

    [TestMethod]
    public void WholeProgramAndRepeatedRunPreserveSharedStateAndKeepCalendarCounters()
    {
        var functional = new MachineCore();
        var hardware = new MachineCore();
        var program = new[]
        {
            new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), utm 2(1)")),
            new Word48(Besm6.Asm.Assembler.Asm("ita 1, atx 10")),
            new Word48(Besm6.Asm.Assembler.Asm("stop, stop")),
        };
        functional.LoadProgram(program, 1); hardware.LoadProgram(program, 1);
        var model = hardware.HardwareModel;
        var outcomes = new List<HardwareInstructionOutcome>();
        model.InstructionCompleted = outcomes.Add;
        for (int run = 0; run < 2; run++)
        {
            bool stopped = false;
            for (int step = 0; step < 5; step++)
            {
                bool expected = functional.Step();
                var command = model.PrepareNextInstruction();
                model.ScheduleCompletionAt(command, model.Timeline.Now + new HardwareDuration(100));
                stopped = model.AdvanceNextEvent()!.Value.Stopped;
                Assert.AreEqual(expected, stopped);
                Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(functional.Cpu.State)),
                    JsonSerializer.Serialize(new ProcessorSnapshot(hardware.Cpu.State)));
                for (uint address = 1; address < 16; address++)
                    Assert.AreEqual(functional.Memory.Read(address), hardware.Memory.Read(address));
            }
            Assert.IsTrue(stopped);
            Assert.AreEqual((ulong)(run + 1) * 5, model.CompletedInstructions);
            Assert.AreEqual(new HardwareInstant((ulong)(run + 1) * 500), model.Timeline.Now);
            functional.ResetCpu(); hardware.ResetCpu();
        }
        Assert.AreEqual(10, outcomes.Count);
        for (int index = 0; index < outcomes.Count; index++)
        {
            Assert.AreEqual((ulong)index + 1, outcomes[index].Instruction.Sequence);
            Assert.AreEqual(new HardwareInstant(((ulong)index + 1) * 100), outcomes[index].Time);
        }
        Assert.AreEqual(10UL, functional.Clock.Tick);
        Assert.AreEqual(0UL, hardware.Clock.Tick);
    }

    [TestMethod]
    [DataRow("xta 10, stop")]
    [DataRow("atx 10, stop")]
    [DataRow("a+x 10, stop")]
    [DataRow("a*x 10, stop")]
    [DataRow("a/x 10, stop")]
    [DataRow("utc 3, vtm 2(1)")]
    [DataRow("wtc 10, ita 1")]
    [DataRow("vjm 10(2), stop")]
    public void CompletedCommandsHaveTheSameGuestStateAndDiagnosticsAsFunctionalSteps(string program)
    {
        var functional = Machine(program);
        var hardware = Machine(program);
        var traces = new[] { new List<string>(), new List<string>() };
        foreach (var machine in new[] { functional, hardware })
        {
            machine.Cpu.SetA(Word48.FromDouble(1.25).Value);
            machine.Cpu.SetY(0x123456789);
            machine.Memory.Write(8, Word48.FromDouble(0.5));
            var trace = traces[ReferenceEquals(machine, functional) ? 0 : 1];
            machine.Cpu.InstructionTrace = record => trace.Add(JsonSerializer.Serialize(record));
        }
        bool expected = functional.Step();
        var model = hardware.HardwareModel;
        var command = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(command, new(100));
        var outcome = model.AdvanceNextEvent();
        Assert.AreEqual(expected, outcome!.Value.Stopped);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(functional.Cpu.State)),
            JsonSerializer.Serialize(new ProcessorSnapshot(hardware.Cpu.State)));
        CollectionAssert.AreEqual(traces[0], traces[1]);
        for (uint address = 1; address < 32; address++)
            Assert.AreEqual(functional.Memory.Read(address), hardware.Memory.Read(address));
        Assert.AreEqual(1UL, functional.Clock.Tick);
        Assert.AreEqual(0UL, hardware.Clock.Tick);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }
}
