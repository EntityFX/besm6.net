using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwarePhysicalArithmeticBindingTests
{
    private static MachineCore Machine(string code = "aax 10", bool right = false, bool large = false, bool suppress = false)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.SetPanel(1, MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm(code + ", " + code)), false, false));
        machine.MappedMemory.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(Word48.FromDouble(0.75), true, true));
        machine.Cpu.StartAt(1); machine.Cpu.State.IsRightHalf = right;
        machine.Cpu.SetA(large ? (127UL << 41) | (1UL << 39) : Word48.FromDouble(1.25).Value);
        machine.Cpu.SetY(123); machine.Cpu.SetM(15, 9);
        machine.Cpu.SetR(suppress ? (uint)RFlags.OvfDisable : 0);
        return machine;
    }

    private static (HardwareProcessorModel Model, HardwareInstructionHandle Cpu, ArithmeticStageController Controller)
        Bind(MachineCore machine, ArithmeticErrorMode mode, bool automatic = false, bool permission = true)
    {
        var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        ArithmeticOperandBuffer? buffer = cpu.Instruction!.Value.Opcode is Opcode.EPlusN or Opcode.EMinusN ? null :
            new(ArithmeticOperandBufferKind.ReadNumbers, 0);
        var arithmetic = model.BindArithmeticInstruction(cpu, new HardwareDuration(100), true, buffer, automatic,
            new ArithmeticErrorPolicy(mode));
        var controller = model.CreateArithmeticController(new(100), new(mode));
        if (permission) controller.GrantCommandPermission();
        if (buffer is null) Assert.IsTrue(controller.TryAcceptDirectOperand(arithmetic));
        return (model, cpu, controller);
    }

    [TestMethod]
    [DataRow("a+x 10")]
    [DataRow("a/x 10")]
    [DataRow("a*x 10")]
    [DataRow("e+n 101")]
    [DataRow("aax 10")]
    [DataRow("aex 10")]
    [DataRow("apx 10")]
    [DataRow("aux 10")]
    public void HealthyPhysicalPoliciesUseSameCpuSemanticsForBothHalves(string code)
    {
        foreach (ArithmeticErrorMode mode in Enum.GetValues<ArithmeticErrorMode>())
        foreach (bool right in new[] { false, true })
        {
            var serial = Machine(code, right); var staged = Machine(code, right);
            serial.Cpu.Step(); var (model, cpu, _) = Bind(staged, mode);
            model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
            Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(800))!.Value.Status);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.Cpu.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(staged.Cpu.State)), code + mode + right);
            Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
            Assert.AreEqual(1UL, model.CompletedInstructions);
            Assert.AreEqual(0UL, staged.Clock.Tick);
        }
    }

    [TestMethod]
    [DataRow(0, false)] [DataRow(0, true)]
    [DataRow(1, false)] [DataRow(1, true)]
    [DataRow(2, false)] [DataRow(2, true)]
    [DataRow(3, false)] [DataRow(3, true)]
    public void PhysicalOverflowPolicyPublishesExactResultWithoutRepeatedDubnaAvost(int mode, bool legacySuppress)
    {
        var serial = Machine("e+n 101", large: true, suppress: true);
        var staged = Machine("e+n 101", large: true, suppress: legacySuppress);
        serial.Cpu.Step(); var (model, cpu, _) = Bind(staged, (ArithmeticErrorMode)mode);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(800))!.Value.Status);
        Assert.AreEqual(serial.Cpu.GetA(), staged.Cpu.GetA()); Assert.AreEqual(serial.Cpu.GetY(), staged.Cpu.GetY());
        Assert.AreEqual(serial.Cpu.GetK(), staged.Cpu.GetK()); Assert.AreEqual(serial.Cpu.RightInstruction, staged.Cpu.RightInstruction);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.AreEqual(mode == 3 ? 0UL : 0x300000UL, model.PendingArithmeticInterruption);
        Assert.AreEqual(mode == 3 ? 0UL : 0x300000UL, staged.Cpu.Supervisor!.InternalInterrupts);
        Assert.AreEqual(1U, staged.Cpu.GetK()); // No synchronous guest avost has entered the UU vector.
        Assert.AreEqual(0UL, staged.Clock.Tick);
    }

    [TestMethod]
    [DataRow(100UL)] [DataRow(300UL)] [DataRow(550UL)]
    public void HostCancellationRemovesOwnedEventsButPreservesRaisedPhysicalIndications(ulong time)
    {
        var machine = Machine(); var (model, cpu, controller) = Bind(machine, ArithmeticErrorMode.Stop, automatic: true);
        var unit = model.CreateArithmeticUnitStages(new(100), new(ArithmeticErrorMode.Stop));
        unit.Errors!.CheckInput(new(0), 10);
        bool external = false; model.Timeline.ScheduleAt(new(900), () => external = true);
        model.AdvanceTo(new(time));
        Assert.IsTrue(model.TryRequestCancellation(cpu));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceTo(new(time))!.Value.Status);
        Assert.IsNull(controller.ActiveCommand); Assert.IsNull(controller.PreparedCommand);
        Assert.IsNull(controller.NextCompletionTime); Assert.IsFalse(unit.Errors.OperationActive);
        Assert.AreEqual(time == 550 ? ArithmeticErrorSignals.InputControl | ArithmeticErrorSignals.Interruption : ArithmeticErrorSignals.None,
            unit.Errors.Signals);
        model.AdvanceTo(new(900)); Assert.IsTrue(external);
        Assert.AreEqual(0UL, model.ReceivedArithmeticInterruptions);
        Assert.AreEqual(1UL, model.CancelledInstructions);
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public void InvalidDivisorWithDeliveredCauseRetiresFaultWithoutInventingNumericResult(int mode)
    {
        var machine = Machine("a/x (17)");
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(Word48.Zero, true, true));
        var beforeA = machine.Cpu.GetA(); var beforeY = machine.Cpu.GetY();
        var (model, cpu, _) = Bind(machine, (ArithmeticErrorMode)mode);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        Assert.AreEqual(HardwareInstructionStatus.GuestFault, model.AdvanceTo(new(800))!.Value.Status);
        Assert.AreEqual(beforeA, machine.Cpu.GetA()); Assert.AreEqual(beforeY, machine.Cpu.GetY());
        Assert.AreEqual(8U, machine.Cpu.GetM(15));
        Assert.AreEqual(0UL, model.CompletedInstructions); Assert.AreEqual(0UL, model.CancelledInstructions);
        Assert.IsFalse(model.HasPendingInstruction); Assert.IsFalse(machine.Cpu.LastStepCompleted);
        Assert.AreEqual(0x700000UL, model.PendingArithmeticInterruption);
        Assert.AreEqual(1U, machine.Cpu.GetK());
        Assert.IsTrue(model.AcceptArithmeticInterruption(1, 0x100));
        Assert.AreEqual(0x140U, machine.Cpu.GetK());
        var unit = model.CreateArithmeticUnitStages(new(100), new((ArithmeticErrorMode)mode));
        model.AdvanceTo(new(900));
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.SynchronizeIdleRegisters(beforeA, beforeY));
    }

    [TestMethod]
    public void InvalidDivisorWithSuppressedSignalsRemainsExplicitlyUnsupportedAndCancelable()
    {
        var machine = Machine("a/x 10");
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(Word48.Zero, true, true));
        var beforeA = machine.Cpu.GetA(); var beforeY = machine.Cpu.GetY();
        var (model, cpu, _) = Bind(machine, ArithmeticErrorMode.InputControlOnly);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
        var failure = Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(800)));
        Assert.IsInstanceOfType<ArithmeticStageObserverException>(failure.InnerException);
        Assert.IsInstanceOfType<NotSupportedException>(failure.InnerException!.InnerException);
        Assert.AreEqual(beforeA, machine.Cpu.GetA()); Assert.AreEqual(beforeY, machine.Cpu.GetY());
        Assert.IsTrue(model.HasPendingInstruction); Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        Assert.IsTrue(model.TryRequestCancellation(cpu));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        var unit = model.CreateArithmeticUnitStages(new(100), new(ArithmeticErrorMode.InputControlOnly));
        Assert.IsFalse(unit.TryReadOutput(out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => unit.SynchronizeIdleRegisters(beforeA, beforeY));
    }

    [TestMethod]
    public void CpuResetCannotPublishStalePhysicalAutomaticResult()
    {
        var machine = Machine(); var (model, _, controller) = Bind(machine, ArithmeticErrorMode.ContinueAndReport, automatic: true);
        model.AdvanceTo(new(300)); machine.ResetCpu();
        model.Timeline.AdvanceTo(new(700));
        Assert.AreEqual(0UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.IsNull(controller.ActiveCommand); Assert.IsNull(model.Timeline.NextEventTime);
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void ConfigurationMismatchDoesNotAdvanceOrConsumeTheCpuLease()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var cpu = model.PrepareNextInstruction();
        model.CreateArithmeticUnitStages(new(100), new(ArithmeticErrorMode.Stop));
        Assert.ThrowsExactly<InvalidOperationException>(() => model.BindArithmeticInstruction(cpu, new HardwareDuration(100), true,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0)));
        Assert.IsTrue(model.HasPendingInstruction); Assert.AreEqual(1U, machine.Cpu.GetK());
        Assert.IsNull(model.Timeline.NextEventTime);
        var dubna = new MachineCore(); dubna.LoadProgram([new(Besm6.Asm.Assembler.Asm("aax 10, stop"))], 1);
        var legacy = dubna.HardwareModel; var command = legacy.PrepareNextInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => legacy.BindArithmeticInstruction(command, new HardwareDuration(100), true,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0), errorPolicy: new ArithmeticErrorPolicy(ArithmeticErrorMode.Stop)));
        Assert.IsTrue(legacy.HasPendingInstruction); Assert.IsNull(legacy.Timeline.NextEventTime);
    }
}
