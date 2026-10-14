using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.State;
using Besm6.Processor.Memory;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing.Arithmetic;
using Besm6.Runtime.Timing.Calendar;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticReadRegistersTests
{
    private static HardwareBufferedRead Reply(ulong raw = 7) => new(default, 16, new(raw),
        MemoryWord50.Form(new(raw), true, true), MemoryFaultSource.PhysicalMemory, new(900), null);

    [TestMethod]
    public void FourRegistersRetainRawWordsButReadinessRequiresAReservation()
    {
        var registers = new ArithmeticReadRegisters();
        for (byte slot = 0; slot < 4; slot++)
        {
            registers.Reserve(slot, (ulong)slot + 1);
            Assert.IsFalse(registers.Inspect(slot).Ready);
            var reply = Reply((ulong)slot + 9);
            registers.Receive(slot, (ulong)slot + 1, reply);
            Assert.AreEqual(reply.RawWord, registers.Inspect(slot).Word);
            Assert.IsTrue(registers.Inspect(slot).Ready);
            Assert.AreEqual(reply, registers.Accept(slot, (ulong)slot + 1));
            Assert.IsFalse(registers.Inspect(slot).Ready);
            Assert.AreEqual(0UL, registers.Inspect(slot).Owner);
            Assert.AreEqual(reply.RawWord, registers.Inspect(slot).Word);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => registers.Reserve(4, 1));
    }

    [TestMethod]
    public void CancelledOrStaleTransferCannotOverwriteAReusedSlot()
    {
        var registers = new ArithmeticReadRegisters();
        registers.Reserve(2, 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => registers.Accept(2, 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => registers.Reserve(2, 2));
        registers.Release(2, 1); registers.Reserve(2, 2);
        Assert.ThrowsExactly<InvalidOperationException>(() => registers.Receive(2, 1, Reply()));
        registers.Release(2, 1);
        registers.Receive(2, 2, Reply());
        Assert.ThrowsExactly<InvalidOperationException>(() => registers.Receive(2, 2, Reply(8)));
        Assert.AreEqual(Reply(), registers.Accept(2, 2));
        Assert.ThrowsExactly<InvalidOperationException>(() => registers.Accept(2, 2));
    }

    [TestMethod]
    public void ActualMemoryReplyOccupiesSelectedRegisterUntilPvrAndIsNotReadAgain()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
        var instruction = model.CurrentInstruction!.Value;
        var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 3));
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.RequestBoundArithmeticOperand(instruction, command);
        model.AdvanceTo(new(2900)); // Fetch and operand share bank zero: cycle starts at 2000 ns.
        Assert.IsTrue(model.ReadRegisters.Inspect(3).Ready);
        Assert.AreEqual(MemoryWord50.Form(new(0xCC), true, true), model.ReadRegisters.Inspect(3).Word);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(0xF0), true, true));
        model.AdvanceTo(new(3050));
        Assert.IsFalse(model.ReadRegisters.Inspect(3).Ready);
        model.ScheduleBoundArithmeticCompletionAt(instruction, new(3500));
        model.AdvanceTo(new(3500));
        Assert.AreEqual(0x88UL, machine.Cpu.A.Value);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ResetCancelsWaitingRegisterOwnershipAndLateReplyCannotReachRestartedCpu()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
        var instruction = model.CurrentInstruction!.Value;
        var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 1));
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.RequestBoundArithmeticOperand(instruction, command);
        machine.ResetCpu();
        model.AdvanceTo(new(4000));
        Assert.AreEqual(0UL, model.ReadRegisters.Inspect(1).Owner);
        Assert.IsFalse(model.ReadRegisters.Inspect(1).Ready);
        Assert.AreEqual(0UL, machine.Cpu.A.Value);
        Assert.AreEqual(0UL, model.CompletedInstructions);
    }

    [TestMethod]
    public void PhysicalInputControlSamplesRawArrivalWithExplicitSourceCode()
    {
        var machine = Machine(); var model = machine.HardwareModel;
        var policy = new ArithmeticErrorPolicy(ArithmeticErrorMode.ContinueAndReport);
        model.StartTimedInstructionFetch(Configuration, new(100)); model.AdvanceTo(new(900));
        var instruction = model.CurrentInstruction!.Value;
        var command = model.BindArithmeticInstruction(instruction, new HardwareDuration(100), false,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 2), errorPolicy: policy);
        var controller = model.CreateArithmeticController(new(100), policy);
        controller.GrantCommandPermission();
        Assert.ThrowsExactly<ArgumentException>(() => model.RequestBoundArithmeticOperand(instruction, command));
        Assert.AreEqual(0UL, model.ReadRegisters.Inspect(2).Owner);
        model.RequestBoundArithmeticOperand(instruction, command, 10);
        model.AdvanceTo(new(3050));
        Assert.AreEqual(new HardwareInstant(3050), controller.Errors!.InputCheckedAt);
        Assert.AreEqual(ArithmeticErrorSignals.None, controller.Errors.Signals);
        model.ScheduleBoundArithmeticCompletionAt(instruction, new(3500)); model.AdvanceTo(new(3500));
        Assert.AreEqual(0x88UL, machine.Cpu.A.Value);
        Assert.AreEqual(0UL, model.PendingArithmeticInterruption);
    }

    private static MramPortConfiguration Configuration => new(new(2000), new(900), new(1500),
        MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);

    private static MachineCore Machine()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8,
            MemoryWord50.Form(new(Assembler.Legacy.Assembler.Asm("aax 20, stop")), false, false));
        machine.MappedMemory.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(0xCC), true, true));
        machine.Cpu.StartAt(8); machine.Cpu.SetA(0xAA);
        return machine;
    }

    [TestMethod]
    public void AutomaticReadRegisterMatchesBakReceiveRingIncludingImmediateCommands()
    {
        var machine = Machine();
        foreach (var (address, code) in new[] { (8U, "aax 20, e+n 100"), (9U, "aox 20, aex 20"), (10U, "aax 20, stop") })
            machine.MappedMemory!.PhysicalMemory.WriteRaw(address, MemoryWord50.Form(new(Assembler.Legacy.Assembler.Asm(code)), false, false));
        var model = machine.HardwareModel;
        model.ConfigureAutomaticExecution(new(Configuration, new(100), new(100),
            new Dictionary<Opcode, HardwareDuration> { [Opcode.Aax] = new(100), [Opcode.Aox] = new(100),
                [Opcode.Aex] = new(100), [Opcode.EPlusN] = new(100), [Opcode.Stop] = new(100) },
            new Dictionary<Opcode, HardwareDuration> { [Opcode.Aex] = new(1200), [Opcode.EPlusN] = new(1200) }));
        foreach (byte slot in new byte[] { 0, 1, 2, 3, 0 })
        {
            model.AdvanceAutomaticTo(new(100000), 1);
            Assert.AreEqual((byte)((slot + 1) % 4), model.CreateArithmeticController(new(100)).NextCommandRegister);
            if (slot != 1) Assert.AreEqual(slot, model.LastIssuedArithmeticCommand!.Value.BufferNumber);
        }
    }
}
