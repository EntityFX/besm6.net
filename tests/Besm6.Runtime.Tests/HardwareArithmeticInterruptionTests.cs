using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareArithmeticInterruptionTests
{
    private static readonly Word48 Large = new((127UL << 41) | (1UL << 39));

    private static (MachineCore Machine, HardwareProcessorModel Model, ArithmeticUnitStages Unit) Machine(
        ArithmeticErrorMode mode = ArithmeticErrorMode.ContinueAndReport)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.Cpu.StartAt(1); machine.Cpu.SetA(Large.Value); machine.Cpu.SetY(123);
        var code = new Word48(Besm6.Asm.Assembler.Asm("xta, stop"));
        machine.MappedMemory!.PhysicalMemory.WriteRaw(1, MemoryWord50.Form(code, false, false));
        machine.MappedMemory.PhysicalMemory.WriteRaw(0x140, MemoryWord50.Form(code, false, false));
        var model = machine.HardwareModel;
        return (machine, model, model.CreateArithmeticUnitStages(new(100), new(mode)));
    }

    private static void Start(ArithmeticUnitStages unit, PreparedArithmeticOperation operation)
    {
        Assert.IsTrue(unit.TryReceiveCommand(0x3800)); // Synthetic control fixture, not a new opcode decoder.
        unit.GrantCommandPermission();
        Assert.IsTrue(unit.TryAcceptOperand(operation, true));
        Assert.IsTrue(unit.TryStartOperation());
    }

    [TestMethod]
    [DataRow(0, 0, 0x300000UL)]
    [DataRow(1, 0, 0x300000UL)]
    [DataRow(2, 0, 0x300000UL)]
    [DataRow(3, 0, 0UL)]
    [DataRow(0, 1, 0x700000UL)]
    [DataRow(1, 1, 0x700000UL)]
    [DataRow(2, 1, 0x700000UL)]
    [DataRow(3, 1, 0UL)]
    [DataRow(0, 2, 0x10000BUL)]
    [DataRow(1, 2, 0x10000BUL)]
    [DataRow(2, 2, 0x10000BUL)]
    [DataRow(3, 2, 0x10000BUL)]
    public void AllFourPoliciesDeliverIndependentRegisterBitsOnlyAtIzop(int mode, int causeKind, ulong expected)
    {
        var (machine, model, unit) = Machine((ArithmeticErrorMode)mode);
        if (causeKind == 2) unit.Errors!.CheckInput(new(0), 11);
        Start(unit, causeKind == 1 ? PreparedArithmeticOperation.Divide(Word48.Zero, 0) :
            PreparedArithmeticOperation.AddExponent(1, (uint)RFlags.OvfDisable));
        model.AdvanceTo(new(1000)); // Explicit synthetic IZOP position, not an inferred duration.
        Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
        Assert.AreEqual(0UL, machine.Cpu.Supervisor!.InternalInterrupts);
        Assert.AreEqual(1U, machine.Cpu.GetK());
        unit.CompleteOperation();
        Assert.AreEqual(expected, model.PendingArithmeticInterruption);
        Assert.AreEqual(expected, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(expected == 0 ? 0UL : 1UL, model.ReceivedArithmeticInterruptions);
        Assert.AreEqual(Large, machine.Cpu.GetA()); Assert.AreEqual(123UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0UL, model.CompletedInstructions);
        if (expected == 0)
        {
            Assert.IsFalse(model.AcceptArithmeticInterruption(42, 0));
            return;
        }
        Assert.AreEqual(new HardwareInstant(1000), model.LastArithmeticInterruption!.Value.Time);
        Assert.ThrowsExactly<InvalidOperationException>(() => model.PrepareNextInstruction());
        Assert.AreEqual(0UL, model.IssuedInstructions);
        Assert.IsTrue(model.AcceptArithmeticInterruption(42, SupervisorControl.SavedRightHalf));
        Assert.AreEqual(0x140U, machine.Cpu.GetK()); Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.AreEqual(42U, machine.Cpu.Supervisor.ReadModifier(27));
        Assert.AreEqual(SupervisorControl.SavedRightHalf, machine.Cpu.Supervisor.SavedFlags & 0x300);
        Assert.AreEqual(expected, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
        Assert.IsFalse(model.AcceptArithmeticInterruption(77, 0));
        Assert.AreEqual(42U, machine.Cpu.Supervisor.ReadModifier(27));
    }

    [TestMethod]
    [DataRow(0U)]
    [DataRow(0x100U)]
    [DataRow(0x200U)]
    [DataRow(0x300U)]
    public void ExplicitUuPositionIsUsedWithoutInferringItFromCurrentK(uint flags)
    {
        var (machine, model, unit) = Machine();
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        Assert.IsTrue(model.AcceptArithmeticInterruption(0x1802AU, flags));
        Assert.AreEqual(42U, machine.Cpu.Supervisor!.ReadModifier(27));
        Assert.AreEqual(flags, machine.Cpu.Supervisor.SavedFlags & 0x300);
        Assert.AreEqual(SupervisorMode.Interrupt, machine.Cpu.Supervisor.Mode);
    }

    [TestMethod]
    public void LocalSignalReleaseAndGuestRegisterClearDoNotLosePendingDelivery()
    {
        var (machine, model, unit) = Machine();
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        model.AdvanceTo(new(1100));
        Assert.AreEqual(ArithmeticErrorSignals.None, unit.Errors!.Signals);
        Assert.AreEqual(0x300000UL, machine.Cpu.Supervisor!.InternalInterrupts);
        machine.Cpu.Supervisor.WriteRegister(0x1F, Word48.Zero);
        Assert.AreEqual(0UL, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(0x300000UL, model.PendingArithmeticInterruption);
        Assert.IsTrue(model.AcceptArithmeticInterruption(18, 0));
        Assert.AreEqual(0x300000UL, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(1UL, model.ReceivedArithmeticInterruptions);
    }

    [TestMethod]
    public void CpuResetAndGeneralClearOfAuPreserveReceivedCause()
    {
        var (machine, model, unit) = Machine(ArithmeticErrorMode.Stop);
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        unit.ApplyGeneralClearSignal(); machine.ResetCpu();
        Assert.AreEqual(0UL, machine.Cpu.Supervisor!.InternalInterrupts);
        Assert.AreEqual(0x300000UL, model.PendingArithmeticInterruption);
        Assert.AreEqual(new HardwareInstant(1000), model.LastArithmeticInterruption!.Value.Time);
        machine.Cpu.Supervisor.Status = new((ControlUnitFlags)0x403);
        Assert.IsTrue(model.AcceptArithmeticInterruption(19, 0));
        Assert.AreEqual(0x300000UL, machine.Cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(19U, machine.Cpu.Supervisor.ReadModifier(27));
    }

    [TestMethod]
    [DataRow(false, 0x004U)]
    [DataRow(false, 0x008U)]
    [DataRow(true, 0x004U)]
    [DataRow(true, 0x008U)]
    public void SharedStopPolicyPreservesPendingCauseOnHaltAndPermitsRetry(bool blockStop, uint stopFlag)
    {
        var (machine, model, unit) = Machine(); var supervisor = machine.Cpu.Supervisor!;
        supervisor.Status = new((ControlUnitFlags)(0x403 | stopFlag));
        supervisor.WriteRegister(blockStop ? 0x41U : 0x40U, Word48.Zero);
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        if (!blockStop)
        {
            Assert.ThrowsExactly<SupervisorHaltException>(() => model.AcceptArithmeticInterruption(20, 0));
            Assert.AreEqual(1U, machine.Cpu.GetK());
            Assert.AreEqual(0x300000UL, model.PendingArithmeticInterruption);
            Assert.AreEqual(0x300000UL, supervisor.InternalInterrupts);
            supervisor.Status = new((ControlUnitFlags)0x403);
        }
        Assert.IsTrue(model.AcceptArithmeticInterruption(20, 0));
        Assert.AreEqual(20U, supervisor.ReadModifier(27));
        Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
    }

    [TestMethod]
    public void CallbackCannotEnterUuOrCpuAndItsFailureDoesNotEraseTheLatchedCause()
    {
        var (machine, model, unit) = Machine();
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.Timeline.ScheduleAt(new(1000), () =>
        {
            unit.CompleteOperation();
            Assert.AreEqual(0x300000UL, machine.Cpu.Supervisor!.InternalInterrupts);
            Assert.AreEqual(1U, machine.Cpu.GetK());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.AcceptArithmeticInterruption(21, 0));
            Assert.ThrowsExactly<InvalidOperationException>(() => model.PrepareNextInstruction());
            throw new InvalidOperationException("device observer failed after IZOP");
        });
        Assert.ThrowsExactly<HardwareCallbackException>(() => model.AdvanceTo(new(1000)));
        Assert.AreEqual(1UL, model.ReceivedArithmeticInterruptions);
        Assert.IsTrue(model.AcceptArithmeticInterruption(21, 0));
        Assert.AreEqual(21U, machine.Cpu.Supervisor!.ReadModifier(27));
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void PendingCpuLeaseCannotBeReplacedByInterruptAcceptance()
    {
        var (machine, model, unit) = Machine();
        var pending = model.PrepareNextInstruction();
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        Assert.ThrowsExactly<InvalidOperationException>(() => model.AcceptArithmeticInterruption(22, 0));
        Assert.AreEqual(0x300000UL, model.PendingArithmeticInterruption);
        Assert.IsTrue(model.HasPendingInstruction);
        Assert.IsTrue(model.TryRequestCancellation(pending));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.IsTrue(model.AcceptArithmeticInterruption(22, 0));
        Assert.AreEqual(1UL, model.CancelledInstructions);
    }

    [TestMethod]
    public void HealthyCompletionAtSameInstantCannotRedeliverPreviousSample()
    {
        var (machine, model, unit) = Machine(ArithmeticErrorMode.Stop);
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        Assert.IsTrue(model.AcceptArithmeticInterruption(23, 0));
        unit.Errors!.ReleaseSingleOperation();
        Start(unit, PreparedArithmeticOperation.AddExponent(0, 0));
        unit.CompleteOperation(); // Synthetic signal at the same calendar instant.
        Assert.AreEqual(1UL, model.ReceivedArithmeticInterruptions);
        Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
        Assert.IsFalse(model.AcceptArithmeticInterruption(24, 0));
        Assert.AreEqual(23U, machine.Cpu.Supervisor!.ReadModifier(27));
    }

    [TestMethod]
    public void InvalidSavedFlagsAndMissingSupervisorAreHostConfigurationErrors()
    {
        var (_, model, unit) = Machine();
        Start(unit, PreparedArithmeticOperation.AddExponent(1, 0));
        model.AdvanceTo(new(1000)); unit.CompleteOperation();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.AcceptArithmeticInterruption(1, 1));
        Assert.AreEqual(0x300000UL, model.PendingArithmeticInterruption);
        Assert.ThrowsExactly<InvalidOperationException>(() => new MachineCore().HardwareModel.AcceptArithmeticInterruption(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ArithmeticInterruptionPort.Encode(new(new(0), (ArithmeticErrorSignals)16, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ArithmeticInterruptionPort.Encode(new(new(0), ArithmeticErrorSignals.Interruption, 16)));
    }
}
