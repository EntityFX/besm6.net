using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticAutomaticCompletionTests
{
    private static MachineCore Machine(string operation = "aax 10")
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(operation + ", stop"))], 1);
        machine.Memory.Write(8, new(0xCC)); machine.Cpu.SetA(0xAA); machine.Cpu.SetY(123);
        return machine;
    }

    private static (HardwareProcessorModel Model, HardwareInstructionHandle Command,
        ArithmeticStageController Controller, ArithmeticCommandHandle Arithmetic) Bind(MachineCore machine,
        ulong cycle = 100, bool ready = true)
    {
        var model = machine.HardwareModel; var command = model.PrepareNextInstruction();
        var arithmetic = model.BindArithmeticInstruction(command, new HardwareDuration(cycle), ready,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 2), automaticCompletion: true);
        return (model, command, model.CreateArithmeticController(new(cycle)), arithmetic);
    }

    [TestMethod]
    [DataRow("aax 10", 100UL, 0x88UL, 7UL)]
    [DataRow("aox 10", 100UL, 0xEEUL, 7UL)]
    [DataRow("aax 10", 110UL, 0x88UL, 7UL)]
    [DataRow("aox 10", 110UL, 0xEEUL, 7UL)]
    [DataRow("apx 10", 100UL, 0xA00000000000UL, 56UL)]
    [DataRow("apx 10", 110UL, 0xA00000000000UL, 56UL)]
    [DataRow("aux 10", 100UL, 0UL, 56UL)]
    [DataRow("aux 10", 110UL, 0UL, 56UL)]
    public void FixedCyclesAreCountedFromActualStartAndCpuRetiresBeforeLaterSameTimeEvents(
        string operation, ulong cycle, ulong expected, ulong endCycles)
    {
        var machine = Machine(operation); var (model, command, controller, _) = Bind(machine, cycle);
        var transitions = new List<ArithmeticStageTransition>(); controller.Transitioned = transitions.Add;
        controller.GrantCommandPermission();
        model.AdvanceTo(new(cycle * 3));
        Assert.AreEqual(new HardwareInstant(cycle * endCycles), controller.NextCompletionTime);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.CompleteOperation());
        Assert.ThrowsExactly<InvalidOperationException>(() => model.ScheduleBoundArithmeticCompletionAt(command, new(cycle * (endCycles - 1))));
        machine.HardwareTimeline.ScheduleAt(new(cycle * endCycles), () =>
        {
            Assert.AreEqual(expected, machine.Cpu.GetA().Value);
            Assert.AreEqual(1UL, model.CompletedInstructions);
        });
        model.AdvanceTo(new(cycle * endCycles - 1));
        Assert.AreEqual(0xAAUL, machine.Cpu.GetA().Value);
        Assert.AreEqual(123UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        var completed = model.AdvanceNextEvent();
        Assert.AreEqual(HardwareInstructionStatus.Completed, completed!.Value.Status);
        Assert.AreEqual(new HardwareInstant(cycle * endCycles), completed.Value.Time);
        Assert.AreEqual(0UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        CollectionAssert.AreEqual(new[] { ArithmeticStageTransitionKind.OperandAccepted,
            ArithmeticStageTransitionKind.Started, ArithmeticStageTransitionKind.Completed }, transitions.Select(t => t.Kind).ToArray());
        Assert.AreEqual(new HardwareInstant(cycle * 3), transitions[1].Time);
        Assert.AreEqual(new HardwareInstant(cycle * endCycles), transitions[2].Time);
        Assert.IsNull(controller.NextCompletionTime);
        Assert.IsNull(model.Timeline.NextEventTime);
    }

    [TestMethod]
    [DataRow(false, 900UL)]
    [DataRow(true, 1050UL)]
    public void LatePermissionOrBufferArrivalMovesStartRatherThanReusingIssueDeadline(bool waitForBuffer, ulong expected)
    {
        var machine = Machine(); var (model, _, controller, arithmetic) = Bind(machine, ready: !waitForBuffer);
        model.AdvanceTo(new(500));
        Assert.IsNull(controller.NextCompletionTime);
        controller.GrantCommandPermission();
        if (waitForBuffer) Assert.IsTrue(controller.TryReleaseBufferWait(arithmetic));
        model.AdvanceTo(new(expected - 1));
        Assert.AreEqual(new HardwareInstant(expected), controller.NextCompletionTime);
        Assert.AreEqual(0xAAUL, machine.Cpu.GetA().Value);
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
    }

    [TestMethod]
    public void CancellationAndResetRemoveOwnedDeadlineAndAllowAnotherRequest()
    {
        foreach (bool reset in new[] { false, true })
        {
            var machine = Machine(); var (model, command, controller, _) = Bind(machine);
            controller.GrantCommandPermission(); model.AdvanceTo(new(300));
            if (reset) machine.ResetCpu(); else Assert.IsTrue(model.TryRequestCancellation(in command));
            Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceTo(new(300))!.Value.Status);
            Assert.IsNull(controller.NextCompletionTime);
            Assert.IsNull(model.Timeline.NextEventTime);
            machine.Cpu.StartAt(1); machine.Cpu.SetA(0xAA);
            var (_, _, nextController, _) = Bind(machine);
            nextController.GrantCommandPermission();
            Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(1000))!.Value.Status);
            Assert.AreEqual(0x88UL, machine.Cpu.GetA().Value);
            Assert.AreEqual(1UL, model.CompletedInstructions);
            Assert.AreEqual(1UL, model.CancelledInstructions);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ObserverFailureCannotLoseOwnedCompletionOrPublishTwice(bool failAtStart)
    {
        var failureAt = failAtStart ? ArithmeticStageTransitionKind.Started : ArithmeticStageTransitionKind.Completed;
        var machine = Machine(); var (model, _, controller, _) = Bind(machine);
        int failures = 0;
        controller.Transitioned = t =>
        {
            if (t.Kind == failureAt && failures++ == 0) throw new InvalidOperationException("observer failed");
        };
        controller.GrantCommandPermission();
        Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(700)));
        controller.Transitioned = null;
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(700))!.Value.Status);
        Assert.AreEqual(0x88UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.IsNull(model.AdvanceTo(new(800)));
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StaleCalendarSignalDoesNotCompleteAuAfterCpuCancellationOrReset(bool reset)
    {
        var machine = Machine(); var (model, command, controller, _) = Bind(machine);
        var unit = machine.CreateArithmeticUnitStages(new(100));
        controller.GrantCommandPermission(); model.AdvanceTo(new(300));
        if (reset) machine.ResetCpu(); else Assert.IsTrue(model.TryRequestCancellation(in command));
        model.Timeline.AdvanceTo(new(700)); // Deliberately bypass driver retirement.
        Assert.IsNull(unit.CompletedAt);
        Assert.IsNotNull(controller.ActiveCommand);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(reset ? 0UL : 0xAAUL, machine.Cpu.GetA().Value);
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.IsNull(controller.ActiveCommand);
        Assert.IsNull(model.Timeline.NextEventTime);
    }

    [TestMethod]
    public void DependentLogicalCommandsUseOneCpuAndPrecedingPublishedResult()
    {
        var machine = Machine();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm("aax 10, aox 11"))], 1);
        machine.Memory.Write(9, new(0x11));
        var (model, _, firstController, _) = Bind(machine);
        firstController.GrantCommandPermission();
        Assert.AreEqual(new HardwareInstant(700), model.AdvanceTo(new(700))!.Value.Time);
        var (_, _, secondController, _) = Bind(machine);
        Assert.AreSame(firstController, secondController);
        secondController.GrantCommandPermission();
        Assert.AreEqual(new HardwareInstant(1400), model.AdvanceTo(new(1400))!.Value.Time);
        Assert.AreEqual(0x99UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(0UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(2UL, model.CompletedInstructions);
        Assert.AreEqual(2U, machine.Cpu.GetK());
        Assert.IsFalse(machine.Cpu.State.IsRightHalf);
    }

    [TestMethod]
    [DataRow("apx 10", 0xAAUL, 0xCCUL, 0xA00000000000UL)]
    [DataRow("aux 10", 0xA00000000000UL, 0xCCUL, 0x88UL)]
    [DataRow("apx 10", 0xFFFFFFFFFFFFUL, 0UL, 0UL)]
    [DataRow("aux 10", 0xFFFFFFFFFFFFUL, 0UL, 0UL)]
    [DataRow("apx 10", 0x123456789ABCUL, 0xFFFFFFFFFFFFUL, 0x123456789ABCUL)]
    [DataRow("aux 10", 0x123456789ABCUL, 0xFFFFFFFFFFFFUL, 0x123456789ABCUL)]
    public void PackingUsesAcceptedMaskAndPublishesIndependentBitVectorOnlyAtCompletion(
        string operation, ulong accumulator, ulong mask, ulong expected)
    {
        // ТО-3 §4.24/4.25: selected bits are packed at the high end, or distributed by mask.
        var machine = Machine(operation);
        machine.Cpu.SetA(accumulator); machine.Memory.Write(8, new(mask));
        var (model, _, controller, _) = Bind(machine);
        controller.GrantCommandPermission(); model.AdvanceTo(new(300));
        machine.Memory.Write(8, new(0)); // PVR has already captured the operand; do not reread at IZOP.
        model.AdvanceTo(new(5599));
        Assert.AreEqual(accumulator, machine.Cpu.GetA().Value);
        Assert.AreEqual(123UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(expected, machine.Cpu.GetA().Value);
        Assert.AreEqual(0UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(new HardwareInstant(5600), model.Timeline.Now);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void VariableOperationCannotAcquireFixedTimingOrChangeCpuState()
    {
        var machine = Machine("aex 10"); var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        Assert.ThrowsExactly<ArgumentException>(() => model.BindArithmeticInstruction(command, new HardwareDuration(100), true,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 2), automaticCompletion: true));
        Assert.AreEqual(1U, machine.Cpu.GetK());
        Assert.IsFalse(machine.Cpu.State.IsRightHalf);
        Assert.AreEqual(0xAAUL, machine.Cpu.GetA().Value);
        Assert.IsNull(model.Timeline.NextEventTime);
        Assert.IsNull(model.LastIssuedArithmeticCommand);
    }

    [TestMethod]
    public void DeadlineOverflowKeepsPreparedOperationCancelableWithoutStartingAu()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        model.AdvanceTo(new(ulong.MaxValue - 600));
        var (_, command, controller, _) = Bind(machine);
        controller.GrantCommandPermission();
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(ulong.MaxValue - 300)));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        Assert.IsNotNull(controller.PreparedCommand);
        Assert.IsNull(controller.ActiveCommand);
        Assert.IsNull(controller.NextCompletionTime);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsTrue(model.TryRequestCancellation(in command));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.IsNull(model.Timeline.NextEventTime);
    }
}
