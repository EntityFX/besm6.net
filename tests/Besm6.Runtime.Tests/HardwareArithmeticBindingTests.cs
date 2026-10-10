using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareArithmeticBindingTests
{
    private static Word48 One => Word48.FromDouble(1);
    private static MachineCore Machine(string operation = "a+x 10")
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(operation + ", stop"))], 1);
        machine.Memory.Write(8, One);
        machine.Cpu.SetA(One.Value);
        return machine;
    }

    private static (HardwareProcessorModel Model, ArithmeticStageController Controller,
        HardwareInstructionHandle Cpu, ArithmeticCommandHandle Arithmetic) Bind(MachineCore machine)
    {
        var model = machine.HardwareModel;
        var cpu = model.PrepareNextInstruction();
        var arithmetic = model.BindArithmeticInstruction(cpu, 0, new(100), true);
        var controller = model.CreateArithmeticController(new(100));
        controller.GrantCommandPermission();
        return (model, controller, cpu, arithmetic);
    }

    [TestMethod]
    public void BindingCapturesAndPublishesWithoutUserWiringOrReread()
    {
        var machine = Machine(); var (model, controller, cpu, _) = Bind(machine);
        Assert.ThrowsExactly<InvalidOperationException>(() => model.CaptureArithmeticOperand(cpu));
        Assert.IsFalse(model.TryIndicateCompletionReady(in cpu));
        Assert.IsFalse(model.TryIndicateArithmeticCompletion(cpu, new(Word48.FromDouble(99), default, false, false)));
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.TryEnqueue(0,
            () => default, true, out _));
        var transitions = new List<ArithmeticStageTransitionKind>();
        controller.Transitioned = t => transitions.Add(t.Kind);
        model.Timeline.ScheduleAt(new(400), () => machine.Memory.Write(8, Word48.FromDouble(99)));
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation);
        Assert.IsNull(model.AdvanceTo(new(799)));
        Assert.AreEqual(One, machine.Cpu.GetA());
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(Word48.FromDouble(2), machine.Cpu.GetA());
        CollectionAssert.AreEqual(new[] { ArithmeticStageTransitionKind.OperandAccepted,
            ArithmeticStageTransitionKind.Started, ArithmeticStageTransitionKind.Completed }, transitions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void ObserverFailureCannotDisconnectCompletionFromCpu()
    {
        var machine = Machine(); var (model, controller, _, _) = Bind(machine);
        controller.Transitioned = t =>
        {
            if (t.Kind == ArithmeticStageTransitionKind.Completed) throw new ApplicationException("observer");
        };
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation);
        var error = Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(800)));
        Assert.IsInstanceOfType<ArithmeticStageObserverException>(error.InnerException);
        Assert.AreEqual(One, machine.Cpu.GetA());
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(Word48.FromDouble(2), machine.Cpu.GetA());
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void DivisionFaultUsesItsCauseAndCpuCompletionOutsideCalendar()
    {
        var machine = Machine("a/x 10"); machine.Memory.Write(8, Word48.Zero);
        var (model, controller, _, _) = Bind(machine);
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation);
        var error = Assert.ThrowsExactly<ProcessorException>(() => model.AdvanceTo(new(800)));
        Assert.AreEqual("Division by zero", error.Message);
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, model.LastOutcome!.Value.Status);
        Assert.AreEqual(One, machine.Cpu.GetA());
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsFalse(model.HasPendingInstruction);
    }

    [TestMethod]
    [DataRow(0UL)]
    [DataRow(299UL)]
    [DataRow(300UL)]
    [DataRow(550UL)]
    public void CancellationDropsOnlyOwnedWorkAndRetainsMachineState(ulong time)
    {
        var machine = Machine("a/x 10"); machine.Memory.Write(8, Word48.Zero);
        var (model, _, cpu, _) = Bind(machine);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        model.AdvanceTo(new(time));
        bool delivered = false;
        model.Timeline.ScheduleAt(new(900), () => delivered = true);
        Assert.IsTrue(model.TryRequestCancellation(in cpu));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceTo(new(900))!.Value.Status);
        Assert.IsTrue(delivered);
        Assert.AreEqual(One, machine.Cpu.GetA());
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(900UL, model.Timeline.Now.Nanoseconds);
        Assert.IsNull(model.Timeline.NextEventTime);
        Assert.AreEqual(time >= 300, machine.Cpu.RightInstruction);
        machine.Cpu.StartAt(1); machine.Memory.Write(8, One);
        var next = model.PrepareNextInstruction();
        model.BindArithmeticInstruction(next, 0, new(100), true);
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.Timeline.ScheduleAt(new(1600), model.CreateArithmeticController(new(100)).CompleteOperation);
        model.AdvanceTo(new(1600));
        Assert.AreEqual(One, machine.Cpu.GetA());
        Assert.AreEqual(1UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetInvalidatesBoundWorkAndResynchronizesRegisters(bool accepted)
    {
        var machine = Machine(); var (model, _, _, _) = Bind(machine);
        if (accepted) model.AdvanceTo(new(300));
        machine.ResetCpu();
        bool unrelated = false;
        model.Timeline.ScheduleAt(new(1000), () => unrelated = true);
        var cancelled = model.AdvanceTo(new(1000));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, cancelled!.Value.Status);
        Assert.IsTrue(unrelated);
        machine.Cpu.StartAt(1); machine.Cpu.SetA(Word48.FromDouble(4).Value);
        var cpu = model.PrepareNextInstruction();
        model.BindArithmeticInstruction(cpu, 0, new(100), true);
        var controller = model.CreateArithmeticController(new(100)); controller.GrantCommandPermission();
        model.Timeline.ScheduleAt(new(1700), controller.CompleteOperation);
        model.AdvanceTo(new(1700));
        Assert.AreEqual(Word48.FromDouble(5), machine.Cpu.GetA());
        Assert.AreEqual(1UL, model.CancelledInstructions);
    }

    [TestMethod]
    public void FunctionalInstructionBetweenBindingsSuppliesCurrentRegisters()
    {
        var machine = Machine();
        machine.Memory.Write(1, new(Besm6.Asm.Assembler.Asm("a+x 10, xta 20")));
        machine.Memory.Write(2, new(Besm6.Asm.Assembler.Asm("a+x 10, stop")));
        machine.Memory.Write(16, Word48.FromDouble(4));
        var (model, controller, _, _) = Bind(machine);
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation); model.AdvanceTo(new(800));
        Assert.AreEqual(Word48.FromDouble(2), machine.Cpu.GetA());
        machine.Step(); Assert.AreEqual(Word48.FromDouble(4), machine.Cpu.GetA());
        var next = model.PrepareNextInstruction(); model.BindArithmeticInstruction(next, 0, new(100), true);
        controller.GrantCommandPermission();
        model.Timeline.ScheduleAt(new(1600), controller.CompleteOperation); model.AdvanceTo(new(1600));
        Assert.AreEqual(Word48.FromDouble(5), machine.Cpu.GetA());
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(2UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void OperandReadFailureReleasesCpuWithoutStartingArithmetic()
    {
        var memory = new FailingMemory(); var cpu = new Processor(memory);
        memory.Write(1, new(Besm6.Asm.Assembler.Asm("a+x 10, stop"))); cpu.SetA(One.Value);
        var model = new HardwareProcessorModel(cpu); var command = model.PrepareNextInstruction();
        model.BindArithmeticInstruction(command, 0, new(100), true);
        var controller = model.CreateArithmeticController(new(100)); controller.GrantCommandPermission();
        var error = Assert.ThrowsExactly<ApplicationException>(() => model.AdvanceTo(new(300)));
        Assert.AreSame(memory.Failure, error);
        Assert.AreEqual(ArithmeticStageTransitionKind.OperandRejected, controller.LastTransition!.Value.Kind);
        Assert.IsNull(controller.ActiveCommand);
        Assert.IsNull(controller.PreparedCommand);
        Assert.AreEqual(HardwareInstructionStatus.HostFailure, model.LastOutcome!.Value.Status);
        Assert.AreEqual(1, memory.Reads);
        Assert.AreEqual(One, cpu.GetA());
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    private sealed class FailingMemory : IMemory
    {
        private readonly CoreMemory _memory = new();
        internal readonly ApplicationException Failure = new("operand read");
        internal int Reads;
        public int Size => _memory.Size;
        public Word48 Read(uint address)
        {
            if (address == 8) { Reads++; throw Failure; }
            return _memory.Read(address);
        }
        public void Write(uint address, Word48 word) => _memory.Write(address, word);
    }

    [TestMethod]
    public void WaitingOperandAndPermissionRemainSeparatePrerequisites()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var cpu = model.PrepareNextInstruction();
        var arithmetic = model.BindArithmeticInstruction(cpu, 0, new(100), false);
        var controller = model.CreateArithmeticController(new(100));
        model.AdvanceTo(new(400));
        Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.IsTrue(controller.TryReleaseBufferWait(arithmetic));
        model.AdvanceTo(new(550));
        Assert.IsFalse(machine.Cpu.RightInstruction);
        controller.GrantCommandPermission();
        model.AdvanceNextEvent();
        Assert.IsTrue(machine.Cpu.RightInstruction);
        Assert.AreEqual(550UL, model.CreateArithmeticUnitStages(new(100)).StartedAt!.Value.Nanoseconds);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        model.AdvanceNextEvent();
        Assert.AreEqual(Word48.FromDouble(2), machine.Cpu.GetA());
        Assert.AreEqual(800UL, model.CreateArithmeticUnitStages(new(100)).CompletedAt!.Value.Nanoseconds);
    }

    [TestMethod]
    public void StaleRawCalendarTransferAfterResetDoesNotReadOrStartArithmetic()
    {
        var machine = Machine(); var (model, controller, cpu, _) = Bind(machine);
        var completion = model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        machine.ResetCpu();
        model.Timeline.AdvanceTo(new(800));
        Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
        Assert.IsNull(controller.ActiveCommand);
        Assert.AreEqual(ArithmeticStageTransitionKind.OperandRejected, controller.LastTransition!.Value.Kind);
        Assert.IsFalse(model.Timeline.Cancel(completion));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }
}
