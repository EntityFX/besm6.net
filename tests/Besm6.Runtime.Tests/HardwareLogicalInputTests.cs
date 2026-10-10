using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareLogicalInputTests
{
    [TestMethod]
    [DataRow("aax 10", 0x88UL, 0UL)]
    [DataRow("aex 10", 0x66UL, 0xAAUL)]
    [DataRow("aox 10", 0xEEUL, 0UL)]
    public void LogicalOperandIsSampledOnceAndPublishedOnlyAtCompletion(string opcode, ulong expectedA, ulong expectedY)
    {
        var machine = new MachineCore();
        machine.LoadProgram([new(Besm6.Asm.Assembler.Asm(opcode + ", stop"))], 1);
        machine.Memory.Write(8, new(0xCC));
        machine.Cpu.SetA(0xAA); machine.Cpu.SetY(0x123);
        machine.Cpu.SetR((uint)RFlags.OvfDisable);
        uint beforeR = machine.Cpu.GetR();
        var model = machine.HardwareModel;
        var command = model.PrepareNextInstruction();
        model.BindArithmeticInstruction(command, new HardwareDuration(100), true,
            new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 2));
        model.CreateArithmeticController(new(100)).GrantCommandPermission();
        model.AdvanceTo(new(300)); // Explicit buffer phase; SPOP calculates, but does not publish.
        Assert.AreEqual(0xAAUL, machine.Cpu.GetA().Value);
        Assert.AreEqual(0x123UL, machine.Cpu.GetY().Value);
        Assert.AreEqual(beforeR, machine.Cpu.GetR());
        machine.Memory.Write(8, new(0xF0));
        model.ScheduleBoundArithmeticCompletionAt(command, new(800)); // Synthetic IZOP until diagrams are specified.
        model.AdvanceTo(new(799));
        Assert.AreEqual(0xAAUL, machine.Cpu.GetA().Value);
        model.AdvanceTo(new(800));
        Assert.AreEqual(expectedA, machine.Cpu.GetA().Value);
        Assert.AreEqual(expectedY, machine.Cpu.GetY().Value);
        Assert.AreEqual(0xF0UL, machine.Memory.Read(8).Value);
        Assert.AreEqual(1UL, model.CompletedInstructions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }
}
