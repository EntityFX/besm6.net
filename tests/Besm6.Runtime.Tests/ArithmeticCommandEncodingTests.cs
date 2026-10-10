using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class ArithmeticCommandEncodingTests
{
    // Independent complete words: TO-3 table1.1 and §3.8, BRCh2 or immediate65.
    [TestMethod]
    [DataRow("a+x 10", Opcode.APlusX, 0x080Au)]
    [DataRow("a-x 10", Opcode.AMinusX, 0x0A0Au)]
    [DataRow("x-a 10", Opcode.XMinusA, 0x0C0Au)]
    [DataRow("amx 10", Opcode.Amx, 0x0E0Au)]
    [DataRow("avx 10", Opcode.Avx, 0x180Au)]
    [DataRow("a/x 10", Opcode.ADivX, 0x1C0Au)]
    [DataRow("a/x (17)", Opcode.ADivX, 0x1C0Au)]
    [DataRow("a*x 10", Opcode.AMulX, 0x1E0Au)]
    [DataRow("e+x 10", Opcode.EPlusX, 0x280Au)]
    [DataRow("e-x 10", Opcode.EMinusX, 0x2A0Au)]
    [DataRow("e+n 101", Opcode.EPlusN, 0x3941u)]
    [DataRow("e-n 101", Opcode.EMinusN, 0x3B41u)]
    [DataRow("aax 10", Opcode.Aax, 0x120Au)]
    [DataRow("aex 10", Opcode.Aex, 0x140Au)]
    [DataRow("aox 10", Opcode.Aox, 0x1A0Au)]
    public void AutomaticallyIssuedWordsMatchIndependentBitsAndSerialExecution(string instruction, Opcode opcode, uint expected)
    {
        foreach (bool right in new[] { false, true })
        foreach (uint mode in new[] { 0u, 1u, 2u, 3u, (uint)RFlags.OvfDisable })
        {
            var serial = Machine(instruction, right, mode); var staged = Machine(instruction, right, mode);
            var serialTrace = new List<string>(); var stagedTrace = new List<string>();
            Observe(serial.Cpu, serialTrace); Observe(staged.Cpu, stagedTrace);
            serial.Cpu.Step();
            var model = staged.HardwareModel; var cpu = model.PrepareNextInstruction();
            ArithmeticOperandBuffer? buffer = opcode is Opcode.EPlusN or Opcode.EMinusN ? null :
                new(ArithmeticOperandBufferKind.ReadNumbers, 2);
            var handle = model.BindArithmeticInstruction(cpu, new HardwareDuration(100), true, buffer);
            Assert.AreEqual(expected, model.LastIssuedArithmeticCommand!.Value.Raw);
            var controller = model.CreateArithmeticController(new(100)); controller.GrantCommandPermission();
            if (buffer is null) Assert.IsTrue(controller.TryAcceptDirectOperand(handle));
            model.ScheduleBoundArithmeticCompletionAt(cpu, new(800));
            model.AdvanceTo(new(800));
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.Cpu.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(staged.Cpu.State)), instruction);
            Assert.AreEqual(serial.Cpu.State.StackCorrection, staged.Cpu.State.StackCorrection);
            Assert.AreEqual(serial.Memory.Read(8), staged.Memory.Read(8));
            CollectionAssert.AreEqual(serialTrace, stagedTrace);
            Assert.AreEqual(1UL, model.CompletedInstructions);
            Assert.AreEqual(0UL, staged.Clock.Tick);
        }
    }

    private static MachineCore Machine(string instruction = "a+x 10", bool right = false, uint mode = 0)
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(instruction + ", " + instruction))], 1);
        machine.Memory.Write(8, Word48.FromDouble(0.75));
        machine.Cpu.SetA(Word48.FromDouble(1.25).Value); machine.Cpu.SetY(0x123456789);
        machine.Cpu.SetM(15, 9); machine.Cpu.SetR(mode);
        machine.Cpu.State.ApplyC = true; machine.Cpu.State.C = 0;
        machine.Cpu.State.IsRightHalf = right;
        return machine;
    }

    private static void Observe(Processor cpu, List<string> trace)
    {
        cpu.InstructionTrace = record => trace.Add(JsonSerializer.Serialize(record));
        cpu.InstructionExecuted = opcode => trace.Add(opcode.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ImmediateIssueUsesSharedCIndexAndAddressWrappingWithoutAdvancing(bool right)
    {
        var machine = Machine("e+n 77777(3)", right);
        machine.Cpu.SetM(3, 2); machine.Cpu.State.C = 64;
        var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        string before = JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State));
        var handle = model.BindArithmeticInstruction(cpu, new HardwareDuration(100), false);
        Assert.AreEqual(0x3941u, model.LastIssuedArithmeticCommand!.Value.Raw); // (32767+64+2) mod32768 =65.
        Assert.AreEqual(before, JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
        var controller = model.CreateArithmeticController(new(100)); controller.GrantCommandPermission();
        Assert.IsTrue(controller.TryAcceptDirectOperand(handle));
        model.ScheduleBoundArithmeticCompletionAt(cpu, new(800)); model.AdvanceTo(new(800));
        Assert.AreEqual(Word48.FromDouble(2.5), machine.Cpu.GetA());
        Assert.IsFalse(machine.Cpu.ApplyC);
        Assert.AreEqual(65u, machine.Cpu.State.EffectiveAddress);
    }

    [TestMethod]
    public void BufferGroupsEncodeWithoutChangingGuestAddressOrSelectingAnotherInstruction()
    {
        Assert.AreEqual(0x0807u, ArithmeticCommandEncoding.Encode(Opcode.APlusX, 0,
            new(ArithmeticOperandBufferKind.WriteResults, 7)).Raw);
        Assert.AreEqual(0x0817u, ArithmeticCommandEncoding.Encode(Opcode.APlusX, 0,
            new(ArithmeticOperandBufferKind.SpecialRegisters, 7)).Raw);
        Assert.AreEqual(0x0827u, ArithmeticCommandEncoding.Encode(Opcode.APlusX, 0,
            new(ArithmeticOperandBufferKind.ConsoleDevices, 7)).Raw);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticOperandBuffer((ArithmeticOperandBufferKind)24, 0));
        Assert.ThrowsExactly<ArgumentException>(() => ArithmeticCommandEncoding.Encode(Opcode.Xta, 0, new ArithmeticOperandBuffer()));
        Assert.ThrowsExactly<ArgumentException>(() => ArithmeticCommandEncoding.Encode(Opcode.EPlusN, 128, null));
    }

    [TestMethod]
    public void MissingOrExtraneousBufferIsRejectedBeforeCpuOrCalendarChanges()
    {
        foreach (string instruction in new[] { "a+x 10", "e+n 101" })
        {
            var machine = Machine(instruction); var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
            string before = JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State));
            ArithmeticOperandBuffer? wrong = instruction.StartsWith("a+x", StringComparison.Ordinal) ? null :
                new(ArithmeticOperandBufferKind.ReadNumbers, 0);
            Assert.ThrowsExactly<ArgumentException>(() => model.BindArithmeticInstruction(cpu, new HardwareDuration(100), true, wrong));
            Assert.AreEqual(before, JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State)));
            Assert.IsNull(model.Timeline.NextEventTime);
            Assert.IsNull(model.LastIssuedArithmeticCommand);
            Assert.IsTrue(model.HasPendingInstruction);
        }
    }

    [TestMethod]
    [DataRow(0x0A08u)] // Wrong physical operation: subtract instead of add.
    [DataRow(0x0818u)] // Mixed BRCh/special flags have no invented priority.
    public void InvalidPhysicalWordCannotBindAnotherSemanticsOrMixedBuffer(uint raw)
    {
        var machine = Machine(); var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        Assert.ThrowsExactly<ArgumentException>(() => model.BindArithmeticInstruction(cpu, new ArithmeticCommandWord(raw), new(100), true));
        Assert.AreEqual(1u, machine.Cpu.GetK()); Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.IsNull(model.Timeline.NextEventTime); Assert.IsNull(model.LastIssuedArithmeticCommand);
    }

    [TestMethod]
    public void IncorrectImmediateWordIsRejectedBeforeAcceptanceAndChangedInputCanBeCancelled()
    {
        var machine = Machine("e+n 101"); var model = machine.HardwareModel; var cpu = model.PrepareNextInstruction();
        Assert.ThrowsExactly<ArgumentException>(() => model.BindArithmeticInstruction(cpu, new ArithmeticCommandWord(0x3942), new(100), true));
        var handle = model.BindArithmeticInstruction(cpu, new HardwareDuration(100), true);
        var controller = model.CreateArithmeticController(new(100)); controller.GrantCommandPermission();
        machine.Cpu.State.C = 1;
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.TryAcceptDirectOperand(handle));
        Assert.AreEqual(1u, machine.Cpu.GetK()); Assert.IsFalse(machine.Cpu.RightInstruction);
        Assert.IsTrue(model.HasPendingInstruction);
        Assert.IsNull(controller.ActiveCommand); Assert.IsNull(controller.PreparedCommand);
        Assert.IsTrue(model.TryRequestCancellation(in cpu));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, model.AdvanceNextEvent()!.Value.Status);
        Assert.AreEqual(Word48.FromDouble(1.25), machine.Cpu.GetA());
    }
}
