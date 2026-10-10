using Besm6.Runtime.Timing;
using Besm6.Runtime.Modeling;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticStageControllerTests
{
    private static Word48 One => new(Convert.ToUInt64("4050000000000000", 8));
    private static Word48 Two => new(Convert.ToUInt64("4110000000000000", 8));
    private static Word48 Four => new(Convert.ToUInt64("4150000000000000", 8));
    private static (HardwareTimeline Time, ArithmeticUnitStages Unit, ArithmeticStageController Controller) Create()
    {
        var time = new HardwareTimeline();
        var unit = new ArithmeticUnitStages(time, new(100), One, Word48.Zero);
        return (time, unit, new(time, unit));
    }
    private static ArithmeticCommandHandle Enqueue(ArithmeticStageController controller, uint code = 0,
        bool ready = true)
    {
        Assert.IsTrue(controller.TryEnqueue(code, () => PreparedArithmeticOperation.Add(One, 0), ready, out var handle));
        return handle;
    }

    [TestMethod]
    public void ReadyBufferAcceptsAtThreeCyclesThenStartsWithoutHostExecution()
    {
        var (time, unit, controller) = Create();
        var log = new List<ArithmeticStageTransition>();
        controller.Transitioned = log.Add;
        var handle = Enqueue(controller);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(299));
        Assert.AreEqual(1, controller.QueuedCommands);
        Assert.IsNull(controller.ActiveCommand);
        time.AdvanceTo(new(300));
        CollectionAssert.AreEqual(new[] { ArithmeticStageTransitionKind.OperandAccepted, ArithmeticStageTransitionKind.Started },
            log.Select(x => x.Kind).ToArray());
        Assert.IsTrue(log.All(x => x.Command == handle && x.Time == new HardwareInstant(300)));
        Assert.AreEqual(handle, controller.ActiveCommand);
        Assert.IsFalse(unit.TryReadOutput(out _));
        time.AdvanceTo(new(800)); // Explicit synthetic IZOP; not an instruction cost.
        controller.CompleteOperation();
        Assert.AreEqual(Two, log[^1].Output!.Value.A);
        Assert.AreEqual(new HardwareInstant(800), log[^1].Time);
    }

    [TestMethod]
    public void WaitingBufferUsesOneAndHalfCyclesFromRelease()
    {
        var (time, _, controller) = Create();
        var handle = Enqueue(controller, ready: false);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(1000));
        Assert.IsNull(time.NextEventTime);
        Assert.IsTrue(controller.TryReleaseBufferWait(handle));
        Assert.IsFalse(controller.TryReleaseBufferWait(handle));
        time.AdvanceTo(new(1149));
        Assert.IsNull(controller.ActiveCommand);
        time.AdvanceTo(new(1150));
        Assert.AreEqual(handle, controller.ActiveCommand);
    }

    [TestMethod]
    public void LatePermissionDoesNotRestartAnElapsedTransfer()
    {
        var (time, _, controller) = Create();
        var handle = Enqueue(controller);
        time.AdvanceTo(new(500));
        Assert.AreEqual(1, controller.QueuedCommands);
        controller.GrantCommandPermission();
        Assert.AreEqual(handle, controller.PreparedCommand);
        Assert.IsNull(controller.ActiveCommand);
        time.AdvanceTo(time.Now);
        Assert.AreEqual(handle, controller.ActiveCommand);
        Assert.AreEqual(new HardwareInstant(500), controller.LastTransition!.Value.Time);
    }

    [TestMethod]
    public void OperandIsSampledExactlyOnceAtAcceptance()
    {
        var (time, _, controller) = Create();
        Word48 operand = Two;
        int samples = 0;
        controller.TryEnqueue(1, () => { samples++; return PreparedArithmeticOperation.Add(operand, 0); }, true, out _);
        controller.GrantCommandPermission();
        time.ScheduleAt(new(100), () => operand = One);
        time.ScheduleAt(new(500), () => operand = Two);
        time.AdvanceTo(new(800));
        controller.Resume();
        controller.CompleteOperation();
        Assert.AreEqual(1, samples);
        Assert.AreEqual(Two, controller.LastTransition!.Value.Output!.Value.A);
    }

    [TestMethod]
    public void SuccessorCanAcceptBeforeCompletionButStartsWithPredecessorOutput()
    {
        var (time, _, controller) = Create();
        Enqueue(controller, 1);
        controller.GrantCommandPermission();
        controller.TryEnqueue(2, () => PreparedArithmeticOperation.Multiply(Two, 0), true, out var second);
        time.AdvanceTo(new(300));
        controller.GrantCommandPermission();
        time.AdvanceTo(new(600));
        Assert.AreEqual(second, controller.PreparedCommand);
        time.AdvanceTo(new(800));
        controller.CompleteOperation();
        Assert.AreEqual(Two, controller.LastTransition!.Value.Output!.Value.A);
        Assert.IsNull(controller.ActiveCommand);
        time.AdvanceTo(time.Now);
        Assert.AreEqual(second, controller.ActiveCommand);
        time.AdvanceTo(new(1000));
        controller.CompleteOperation();
        Assert.AreEqual(Four, controller.LastTransition!.Value.Output!.Value.A);
    }

    [TestMethod]
    public void UnselectedOperandReadyBeforeSelectionUsesReadyBufferPhase()
    {
        var (time, _, controller) = Create();
        Enqueue(controller);
        var second = Enqueue(controller, 1, false);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(100));
        Assert.IsTrue(controller.TryReleaseBufferWait(second));
        time.AdvanceTo(new(300));
        controller.GrantCommandPermission();
        time.AdvanceTo(new(599));
        Assert.IsNull(controller.PreparedCommand);
        time.AdvanceTo(new(600));
        Assert.AreEqual(second, controller.PreparedCommand);
    }

    [TestMethod]
    public void FullBakRejectsWithoutSamplingAndPvrFreesExactlyOneSlot()
    {
        var (time, unit, controller) = Create();
        for (uint i = 0; i < 4; i++) Enqueue(controller, i);
        int sampled = 0;
        Assert.IsFalse(controller.TryEnqueue(4, () => { sampled++; return default; }, true, out var rejected));
        Assert.AreEqual(default, rejected);
        Assert.AreEqual(0, sampled);
        Assert.AreEqual(4, unit.QueuedCommands);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(300));
        Assert.AreEqual(3, unit.QueuedCommands);
        Enqueue(controller, 4);
        Assert.AreEqual(4, unit.QueuedCommands);
    }

    [TestMethod]
    public void DuplicateCodesHaveDistinctIdentitiesAndForeignSignalsAreRejected()
    {
        var (_, _, controller) = Create();
        var first = Enqueue(controller, ready: false);
        var second = Enqueue(controller, ready: false);
        Assert.AreNotEqual(first, second);
        Assert.IsFalse(controller.TryReleaseBufferWait(default));
        Assert.IsFalse(Create().Controller.TryReleaseBufferWait(first));
    }

    [TestMethod]
    public void GeneralClearCancelsOnlyOwnedEventsAndInvalidatesOldHandles()
    {
        var (time, unit, controller) = Create();
        var stale = Enqueue(controller);
        bool unrelated = false;
        time.ScheduleAt(new(400), () => unrelated = true);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(299));
        controller.ApplyGeneralClearSignal();
        time.AdvanceTo(new(500));
        Assert.IsTrue(unrelated);
        Assert.AreEqual(0, unit.QueuedCommands);
        Assert.IsNull(controller.ActiveCommand);
        Assert.IsFalse(controller.TryReleaseBufferWait(stale));
        var next = Enqueue(controller, ready: false);
        Assert.AreNotEqual(stale, next);
        Assert.IsTrue(controller.TryReleaseBufferWait(next));
    }

    [TestMethod]
    public void SamplerFailureLeavesCommandAndPrerequisitesAvailableForRetry()
    {
        var (time, unit, controller) = Create();
        bool fail = true;
        var cause = new InvalidOperationException("Synthetic sampler failure");
        controller.TryEnqueue(0, () => fail ? throw cause : PreparedArithmeticOperation.Add(One, 0), true, out var command);
        controller.GrantCommandPermission();
        Assert.AreSame(cause, Assert.ThrowsExactly<HardwareCallbackException>(() => time.AdvanceTo(new(300))).InnerException);
        Assert.AreEqual(1, unit.QueuedCommands);
        Assert.IsTrue(unit.CommandPermission);
        fail = false;
        controller.Resume();
        time.AdvanceTo(time.Now);
        Assert.AreEqual(command, controller.ActiveCommand);
    }

    [TestMethod]
    public void AcceptanceObserverFailurePreservesScheduledStartAndCause()
    {
        var (time, _, controller) = Create();
        var cause = new Exception("Synthetic observer failure");
        controller.Transitioned = x => { if (x.Kind == ArithmeticStageTransitionKind.OperandAccepted) throw cause; };
        var command = Enqueue(controller);
        controller.GrantCommandPermission();
        var outer = Assert.ThrowsExactly<HardwareCallbackException>(() => time.AdvanceTo(new(300)));
        var failure = (ArithmeticStageObserverException)outer.InnerException!;
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(command, controller.PreparedCommand);
        controller.Transitioned = null;
        time.AdvanceTo(time.Now);
        Assert.AreEqual(command, controller.ActiveCommand);
    }

    [TestMethod]
    public void CompletionObserverFailurePreservesOutputAndSuccessorStart()
    {
        var (time, _, controller) = Create();
        Enqueue(controller);
        var next = Enqueue(controller, 1);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(300));
        controller.GrantCommandPermission();
        time.AdvanceTo(new(600));
        var cause = new Exception("Synthetic completion observer failure");
        controller.Transitioned = _ => throw cause;
        var failure = Assert.ThrowsExactly<ArithmeticStageObserverException>(() => controller.CompleteOperation());
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(Two, failure.Transition.Output!.Value.A);
        controller.Transitioned = null;
        time.AdvanceTo(time.Now);
        Assert.AreEqual(next, controller.ActiveCommand);
    }

    [TestMethod]
    public void NestedControlMutationIsRejectedDuringOperandAndObserverCallbacks()
    {
        var (time, _, controller) = Create();
        controller.TryEnqueue(0, () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(controller.ApplyGeneralClearSignal);
            return PreparedArithmeticOperation.Add(One, 0);
        }, true, out _);
        controller.Transitioned = _ => Assert.ThrowsExactly<InvalidOperationException>(controller.Resume);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(300));
        controller.CompleteOperation();
        Assert.AreEqual(Two, controller.LastTransition!.Value.Output!.Value.A);
    }

    [TestMethod]
    public void InvalidAdmissionAndCompletionDoNotChangeTheQueue()
    {
        var (time, _, controller) = Create();
        Assert.ThrowsExactly<ArgumentNullException>(() => controller.TryEnqueue(0, null!, true, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.TryEnqueue(0x20000, () => default, true, out _));
        Assert.ThrowsExactly<InvalidOperationException>(controller.CompleteOperation);
        Assert.AreEqual(0, controller.QueuedCommands);
        Assert.IsNull(time.NextEventTime);
    }

    [TestMethod]
    public void TransferOverflowDoesNotAdmitCommandOrConsumeWaitRelease()
    {
        var (time, _, controller) = Create();
        time.AdvanceTo(new(ulong.MaxValue - 100));
        Assert.ThrowsExactly<OverflowException>(() => Enqueue(controller));
        Assert.AreEqual(0, controller.QueuedCommands);
        var command = Enqueue(controller, ready: false);
        Assert.ThrowsExactly<OverflowException>(() => controller.TryReleaseBufferWait(command));
        Assert.IsNull(time.NextEventTime);
        Assert.AreEqual(1, controller.QueuedCommands);
    }

    [TestMethod]
    public void DivisorCheckAndInvalidCauseStillPrecedeIzop()
    {
        var (time, unit, controller) = Create();
        controller.TryEnqueue(1, () => PreparedArithmeticOperation.Divide(Word48.Zero, 0), true, out _);
        controller.GrantCommandPermission();
        time.AdvanceTo(new(549));
        Assert.ThrowsExactly<InvalidOperationException>(controller.CompleteOperation);
        Assert.IsNotNull(controller.ActiveCommand);
        time.AdvanceTo(new(550));
        Assert.AreEqual(new HardwareInstant(550), unit.DivisorCheckedAt);
        Assert.ThrowsExactly<InvalidOperationException>(controller.CompleteOperation);
        time.AdvanceTo(new(600));
        controller.CompleteOperation();
        Assert.IsNull(controller.LastTransition!.Value.Output);
        Assert.AreEqual(ArithmeticUnitFaultKind.InvalidDivisor, controller.LastTransition.Value.Fault!.Value.Kind);
    }

    [TestMethod]
    public void ControllerBelongsToTheModelAndCanSupplyCpuCompletionPermission()
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm("xta 10, a+x 10")),
            new(Besm6.Asm.Assembler.Asm("stop, stop"))], 1);
        machine.Memory.Write(8, One); // Assembler addresses are octal.
        machine.Cpu.StartAt(1);
        machine.Step();
        var model = machine.HardwareModel;
        var controller = model.CreateArithmeticController(new(100));
        Assert.AreSame(controller, model.CreateArithmeticController(new(100)));
        Assert.ThrowsExactly<InvalidOperationException>(() => model.CreateArithmeticController(new(110)));
        var command = model.PrepareNextInstruction();
        controller.Transitioned = x =>
        {
            if (x.Kind == ArithmeticStageTransitionKind.Completed)
            {
                Assert.AreEqual(Two, x.Output!.Value.A);
                Assert.IsTrue(model.TryIndicateCompletionReady(in command));
            }
        };
        controller.TryEnqueue(0, () => PreparedArithmeticOperation.Add(machine.Memory.Read(8), machine.Cpu.GetR()), true, out _);
        controller.GrantCommandPermission();
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation);
        model.AdvanceNextEvent();
        Assert.AreEqual(One, machine.Cpu.GetA());
        var outcome = model.AdvanceNextEvent();
        Assert.AreEqual(HardwareInstructionStatus.Completed, outcome!.Value.Status);
        Assert.AreEqual(Two, machine.Cpu.GetA());
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void DifferentCalendarsAndAnAlreadyOwnedPipelineAreRejected()
    {
        var (time, unit, _) = Create();
        Assert.ThrowsExactly<ArgumentException>(() => new ArithmeticStageController(new(), unit));
        Assert.ThrowsExactly<InvalidOperationException>(() => new ArithmeticStageController(time, unit));
        var standalone = new ArithmeticUnitStages(time, new(100), One, Word48.Zero);
        standalone.TryReceiveCommand(0);
        Assert.ThrowsExactly<InvalidOperationException>(() => new ArithmeticStageController(time, standalone));
        standalone.ApplyGeneralClearSignal();
        var controller = new ArithmeticStageController(time, standalone);
        controller.ApplyGeneralClearSignal();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ArithmeticStageController(time, standalone));
    }

    [TestMethod]
    public void PermissionAndTransferAtSameInstantHaveIdenticalAcceptanceTime()
    {
        foreach (bool permissionFirst in new[] { false, true })
        {
            var (time, _, controller) = Create();
            if (permissionFirst) time.ScheduleAt(new(300), controller.GrantCommandPermission);
            Enqueue(controller);
            if (!permissionFirst) time.ScheduleAt(new(300), controller.GrantCommandPermission);
            time.AdvanceTo(new(300));
            Assert.AreEqual(new HardwareInstant(300), controller.LastTransition!.Value.Time);
            Assert.AreEqual(ArithmeticStageTransitionKind.Started, controller.LastTransition.Value.Kind);
        }
    }

    [TestMethod]
    public void StartedObserverFailureLeavesOperationAvailableForCompletion()
    {
        var (time, _, controller) = Create();
        Enqueue(controller);
        var cause = new Exception("Synthetic started observer failure");
        controller.Transitioned = x => { if (x.Kind == ArithmeticStageTransitionKind.Started) throw cause; };
        controller.GrantCommandPermission();
        var outer = Assert.ThrowsExactly<HardwareCallbackException>(() => time.AdvanceTo(new(300)));
        Assert.AreSame(cause, outer.InnerException!.InnerException);
        Assert.IsNotNull(controller.ActiveCommand);
        controller.Transitioned = null;
        controller.CompleteOperation();
        Assert.AreEqual(Two, controller.LastTransition!.Value.Output!.Value.A);
    }

    [TestMethod]
    public void GeneralClearAlsoCancelsStartQueuedAfterCommittedAcceptance()
    {
        var (time, unit, controller) = Create();
        Enqueue(controller);
        controller.GrantCommandPermission();
        // Registered before acceptance enqueues SPOP at this instant.
        time.ScheduleAt(new(300), controller.ApplyGeneralClearSignal);
        time.AdvanceTo(new(400));
        Assert.IsNull(controller.ActiveCommand);
        Assert.IsNull(controller.PreparedCommand);
        Assert.AreEqual(0, unit.QueuedCommands);
        Assert.IsNull(unit.StartedAt);
    }
}
