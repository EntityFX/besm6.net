using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class ArithmeticCommandWordTests
{
    // Independent interface patterns from TO-3 §3.8, sheets38–40.
    [TestMethod]
    [DataRow(0u, "BufferRead")]
    [DataRow(0x80u, "BufferWrite")]
    [DataRow(0x100u, "Immediate")]
    [DataRow(0x180u, "Immediate")] // Bit8 is unused for NAK.
    [DataRow(0x8000u, "IndexRegister")]
    [DataRow(0x8180u, "IndexRegister")]
    [DataRow(0x10000u, "ExternalDevice")]
    [DataRow(0x18000u, "ExternalDevice")]
    public void InterfaceSourcePatterns(uint raw, string expected)
    {
        Assert.AreEqual(expected, new ArithmeticCommandWord(raw).Source.ToString());
    }

    [TestMethod]
    public void OperandAndOperationFieldsRemainIndependent()
    {
        var immediate = new ArithmeticCommandWord(0x55AA);
        Assert.AreEqual(ArithmeticCommandSource.Immediate, immediate.Source);
        Assert.AreEqual((byte)42, immediate.OperationCode);
        Assert.AreEqual((byte)42, immediate.ImmediateOperand);
        var index = new ArithmeticCommandWord(0xFFFF);
        Assert.AreEqual(ArithmeticCommandSource.IndexRegister, index.Source);
        Assert.AreEqual((ushort)32767, index.IndexOperand);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticCommandWord(0x20000));
    }

    [TestMethod]
    public void BufferFlagsAndControlWriteAreNotCollapsedIntoGuessedPriority()
    {
        var read = new ArithmeticCommandWord(0x3F);
        Assert.AreEqual(ArithmeticCommandSource.BufferRead, read.Source);
        Assert.AreEqual((byte)7, read.BufferClassFlags);
        Assert.AreEqual((byte)7, read.BufferNumber);
        var write = new ArithmeticCommandWord(0xC5);
        Assert.AreEqual(ArithmeticCommandSource.BufferWrite, write.Source);
        Assert.IsTrue(write.WritesControlRegisters);
        Assert.AreEqual((byte)5, write.ControlFlags);
        Assert.IsFalse(new ArithmeticCommandWord(0x85).WritesControlRegisters);
    }

    [TestMethod]
    public void ExternalAddressDirectionAndControlSuppressionAreSeparate()
    {
        var command = new ArithmeticCommandWord(0x18DA5);
        Assert.AreEqual(ArithmeticCommandSource.ExternalDevice, command.Source);
        Assert.IsTrue(command.ExternalRead);
        Assert.IsTrue(command.SuppressesExternalControl);
        Assert.AreEqual((ushort)0x5A5, command.ExternalAddress);
        Assert.IsFalse(new ArithmeticCommandWord(0x105A5).ExternalRead);
    }

    [TestMethod]
    [DataRow("e+n 101", 2.0)]
    [DataRow("e-n 101", 0.5)]
    public void ImmediateOperandWaitsForExplicitAcceptanceWithoutBufferDelay(string opcode, double expected)
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(opcode + ", stop"))], 1);
        machine.Cpu.SetA(Word48.FromDouble(1).Value);
        var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        uint code = opcode.StartsWith("e+n", StringComparison.Ordinal) ? 0x3941u : 0x3B41u;
        var command = model.BindArithmeticInstruction(cpu, new ArithmeticCommandWord(code), new(100), true);
        var controller = model.CreateArithmeticController(new(100));
        Assert.IsFalse(controller.TryAcceptDirectOperand(command)); // RPK has not arrived.
        controller.GrantCommandPermission();
        Assert.IsNull(model.Timeline.NextEventTime);
        model.AdvanceTo(new(1000));
        Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.IsFalse(controller.TryReleaseBufferWait(command));
        Assert.IsTrue(controller.TryAcceptDirectOperand(command));
        Assert.IsFalse(controller.TryAcceptDirectOperand(command));
        Assert.IsTrue(machine.Cpu.RightInstruction);
        model.AdvanceNextEvent();
        Assert.AreEqual(new HardwareInstant(1000), model.CreateArithmeticUnitStages(new(100)).StartedAt);
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(1400));
        model.AdvanceNextEvent();
        Assert.AreEqual(Word48.FromDouble(expected), machine.Cpu.GetA());
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(0x80u)]
    [DataRow(0x8000u)]
    [DataRow(0x10000u)]
    public void UnsupportedOrWrongSourceCannotAdvanceSharedCpu(uint raw)
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm("e+n 101, stop"))], 1);
        var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        Assert.ThrowsExactly<ArgumentException>(() => model.BindArithmeticInstruction(cpu,
            new ArithmeticCommandWord(raw), new(100), true));
        Assert.AreEqual(1u, machine.Cpu.GetK());
        Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.IsTrue(model.HasPendingInstruction);
        Assert.IsNull(model.Timeline.NextEventTime);
        Assert.IsTrue(model.TryRequestCancellation(in cpu));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
    }
}
