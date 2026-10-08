namespace Besm6.Tests;

[TestClass]
public sealed class ProcessorFetchOptimizationTests
{
    private sealed class ObservedCoreMemory : CoreMemory, IMemory
    {
        internal int Reads;

        Word48 IMemory.Read(uint address)
        {
            if (++Reads == 2)
                Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), vtm 7(2)")));
            return Read(address);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DerivedCoreMemoryRetainsInterfaceFetchSideEffects(bool cached)
    {
        var memory = new ObservedCoreMemory();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), vtm 2(2)")));
        var cpu = new Processor(memory) { InstructionCacheEnabled = cached };
        Assert.IsFalse(cpu.CanExecuteUnobservedBlock);
        cpu.SetK(1);
        Assert.IsFalse(cpu.Step());
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(2, memory.Reads);
        Assert.AreEqual(1u, cpu.GetM(1));
        Assert.AreEqual(7u, cpu.GetM(2));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PlainMemoryFetchErrorsPrecedeHalfAdvanceAndRegisterChanges(bool cached)
    {
        var cpu = new Processor(new CoreMemory(8)) { InstructionCacheEnabled = cached };
        cpu.SetA(123); cpu.SetY(456);
        cpu.SetK(0);
        Assert.AreEqual("Jump to zero",
            Assert.ThrowsExactly<ProcessorException>(() => cpu.Step()).Message);
        Assert.IsFalse(cpu.RightInstruction);
        cpu.SetK(9);
        Assert.AreEqual("Memory access violation at address 0x00009 (Bank 1)",
            Assert.ThrowsExactly<IndexOutOfRangeException>(() => cpu.Step()).Message);
        Assert.AreEqual(9u, cpu.GetK());
        Assert.IsFalse(cpu.RightInstruction);
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(456UL, cpu.GetY().Value);
    }
}
