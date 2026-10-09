namespace Besm6.Tests;

[TestClass]
public sealed class ProcessorStartTests
{
    [TestMethod]
    public void StartAtNormalizesAddressSelectsLeftAndPreservesOtherRegisters()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("utc 21, stop")));
        memory.Write(3, new Word48(Besm6.Asm.Assembler.Asm("vtm 7(2), stop")));
        var cpu = new Processor(memory);
        cpu.SetM(2, 1);
        cpu.Step(); // next command is the right half
        cpu.SetA(123);
        cpu.SetY(456);
        cpu.SetR(0x23);
        cpu.StartAt(0x8003);
        Assert.AreEqual(3u, cpu.GetK());
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(456UL, cpu.GetY().Value);
        Assert.AreEqual(0x23UL, cpu.GetR());
        Assert.AreEqual(17u, cpu.C);
        Assert.IsTrue(cpu.ApplyC);
        Assert.AreEqual(1u, cpu.GetM(2));
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(24u, cpu.GetM(2));
        Assert.IsTrue(cpu.Step());
    }
}
