using Besm6.Runtime.Execution;
using Besm6.Runtime.Modeling;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Memory;
using Besm6.Runtime.Timing.Arithmetic;
using Besm6.Processor.Memory;
using Besm6.Processor.Cpu;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;


[TestClass]
public sealed class HardwareArithmeticPublicationTests
{
    private static Word48 One => new(Convert.ToUInt64("4050000000000000", 8));
    private static Word48 Two => new(Convert.ToUInt64("4110000000000000", 8));
    private static MachineCore Machine(string instruction = "a+x 10")
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Assembler.Legacy.Assembler.Asm(instruction + ", stop"))], 1);
        machine.Memory.Write(8, One);
        machine.Cpu.SetA(One.Value);
        return machine;
    }

    [TestMethod]
    public void ControllerAcceptsOnceAndCpuPublishesItsOutputWithoutReadingChangedOperand()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var controller = model.CreateArithmeticController(new(100));
        var command = model.PrepareNextInstruction();
        controller.TryEnqueue(0, () => model.CaptureArithmeticOperand(command)!.Value, true, out _);
        controller.GrantCommandPermission();
        controller.Transitioned = transition =>
        {
            if (transition.Kind == ArithmeticStageTransitionKind.Completed)
                Assert.IsTrue(model.TryIndicateArithmeticCompletion(command, transition.Output));
        };
        model.Timeline.ScheduleAt(new(500), () => machine.Memory.Write(8, Word48.FromDouble(100)));
        model.Timeline.ScheduleAt(new(800), controller.CompleteOperation);
        model.AdvanceTo(new(799));
        Assert.AreEqual(One, machine.Cpu.A);
        Assert.IsFalse(model.TryIndicateCompletionReady(in command));
        var outcome = model.AdvanceNextEvent();
        Assert.AreEqual(HardwareInstructionStatus.Completed, outcome!.Value.Status);
        Assert.AreEqual(Two, machine.Cpu.A);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void InvalidDivisorCauseIsDeliveredOutsideTheCalendar()
    {
        var machine = Machine("a/x 10"); machine.Memory.Write(8, Word48.Zero);
        var model = machine.HardwareModel; var command = model.PrepareNextInstruction();
        model.Timeline.ScheduleAt(new(100), () =>
        {
            var operation = model.CaptureArithmeticOperand(command)!.Value;
            var cause = Assert.ThrowsExactly<ProcessorException>(() => operation.Evaluate(machine.Cpu.A, machine.Cpu.Y));
            Assert.IsTrue(model.TryIndicateArithmeticCompletion(command, null, cause));
        });
        var failure = Assert.ThrowsExactly<ProcessorException>(() => model.AdvanceNextEvent());
        Assert.AreEqual("Division by zero", failure.Message);
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, model.LastOutcome!.Value.Status);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.AreEqual(One, machine.Cpu.A);
    }

    [TestMethod]
    public void ResetInvalidatesCapturedOutputAndRestartUsesNewOperand()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var stale = model.PrepareNextInstruction();
        model.CaptureArithmeticOperand(stale);
        machine.ResetCpu();
        Assert.IsFalse(model.TryIndicateArithmeticCompletion(stale, new(Two, Word48.Zero, false, false)));
        model.AdvanceNextEvent();
        machine.Cpu.SetA(One.Value); machine.Cpu.StartAt(1);
        machine.Memory.Write(8, Two);
        var command = model.PrepareNextInstruction();
        var operation = model.CaptureArithmeticOperand(command)!.Value;
        var expected = operation.Evaluate(machine.Cpu.A, machine.Cpu.Y);
        model.TryIndicateArithmeticCompletion(command, expected);
        model.AdvanceNextEvent();
        Assert.AreEqual(Word48.FromDouble(3), machine.Cpu.A);
    }

    private sealed class ObservedMemory : IMemory
    {
        private readonly CoreMemory _memory = new();
        internal Action? Reading;
        public int Size => _memory.Size;
        public Word48 Read(uint address) { if (address == 8) Reading?.Invoke(); return _memory.Read(address); }
        public void Write(uint address, Word48 word) => _memory.Write(address, word);
    }

    [TestMethod]
    public void OperandReaderCannotReenterItsExecutionOwnerOrAdvanceCalendars()
    {
        var memory = new ObservedMemory(); var cpu = new Processor(memory);
        memory.Write(1, new(Assembler.Legacy.Assembler.Asm("a+x 10, stop")));
        memory.Write(8, One); cpu.SetA(One.Value);
        var model = new HardwareProcessorModel(cpu);
        var clock = new SimulationClock();
        var transitions = new List<bool>();
        model.ExecutionStateChanged = active => { transitions.Add(active); clock.AdvancementProhibited = active; };
        var command = model.PrepareNextInstruction();
        transitions.Clear();
        memory.Reading = () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceNextEvent());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.PrepareNextInstruction());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Timeline.AdvanceTo(new(0)));
            Assert.ThrowsExactly<InvalidOperationException>(() => clock.AdvanceTo(1));
            Assert.IsFalse(model.TryRequestCancellation(in command));
            Assert.IsFalse(model.TryIndicateCompletionReady(in command));
            Assert.IsFalse(model.TryIndicateArithmeticCompletion(command, new(Two, Word48.Zero, false, false)));
        };
        var operation = model.CaptureArithmeticOperand(command)!.Value;
        CollectionAssert.AreEqual(new[] { true, false }, transitions);
        Assert.IsFalse(clock.AdvancementProhibited);
        Assert.IsFalse(model.Timeline.AdvancementProhibited);
        Assert.IsTrue(model.HasPendingInstruction);
        model.TryIndicateArithmeticCompletion(command, operation.Evaluate(cpu.A, cpu.Y));
        model.AdvanceNextEvent();
        Assert.AreEqual(Two, cpu.A);
    }

    [TestMethod]
    public void FunctionalCalendarCannotAcceptOrSupplyAnArithmeticPhase()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        machine.Scheduler.Schedule(0, () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => model.CaptureArithmeticOperand(command));
            Assert.IsFalse(model.TryIndicateArithmeticCompletion(command, new(Two, Word48.Zero, false, false)));
        });
        machine.Scheduler.AdvanceTo(0);
        Assert.AreEqual(1u, machine.Cpu.K);
        Assert.IsFalse(machine.Cpu.RightInstruction);
        var operation = model.CaptureArithmeticOperand(command)!.Value;
        model.TryIndicateArithmeticCompletion(command, operation.Evaluate(machine.Cpu.A, machine.Cpu.Y));
        model.AdvanceNextEvent();
        Assert.AreEqual(Two, machine.Cpu.A);
    }
}
