namespace Besm6.Tests;

[TestClass]
public sealed class InstructionCacheTests
{
    private sealed class ChangingFetchMemory : IMemory
    {
        public int Reads { get; private set; }
        public int Size => 32768;
        public Word48 Read(uint address) => new(Besm6.Asm.Assembler.Asm(
            ++Reads <= 2 ? "uj 1" : "vtm 7(1), stop"));
        public void Write(uint address, Word48 word) => throw new InvalidOperationException();
    }

    [TestMethod]
    public void CacheHitStillFetchesMemoryAndSeesChangesWithoutWriteNotification()
    {
        var memory = new ChangingFetchMemory();
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        cpu.SetK(1);
        Assert.IsFalse(cpu.Step());
        Assert.IsFalse(cpu.Step());
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(7u, cpu.GetM(1));
        Assert.IsTrue(cpu.Step());
        Assert.AreEqual(4, memory.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GuestWriteReplacesAlreadyCachedRightHalf(bool cached)
    {
        var memory = new CoreMemory();
        ulong original = Besm6.Asm.Assembler.Asm("atx 1, vtm 3(2)");
        memory.Write(1, new Word48(original));
        var cpu = new Processor(memory) { InstructionCacheEnabled = cached };
        cpu.SetK(1);
        cpu.SetA(original);
        cpu.Step();
        cpu.Step(); // Warm both halves before modifying the instruction word.
        Assert.AreEqual(3u, cpu.GetM(2));
        cpu.SetK(1);
        cpu.SetA(Besm6.Asm.Assembler.Asm("atx 1, vtm 7(2)"));
        cpu.Step();
        cpu.Step();
        Assert.AreEqual(7u, cpu.GetM(2));
    }

    [TestMethod]
    public void ExternalWriteAndResetReplaceCachedZeroInstruction()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(0));
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        cpu.SetK(1);
        cpu.SetA(123);
        cpu.Step();
        Assert.AreEqual(123UL, cpu.GetA().Value);
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 7(3), stop")));
        cpu.Reset();
        cpu.SetK(1);
        cpu.Step();
        Assert.AreEqual(7u, cpu.GetM(3));
        Assert.IsTrue(cpu.Step());
    }

    [TestMethod]
    public void TraceUsesCurrentInstructionAfterExternalWrite()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 1(2), uj 1")));
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        var records = new List<InstructionTraceRecord>();
        cpu.InstructionTrace = records.Add;
        cpu.SetK(1);
        cpu.Step();
        cpu.Step();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 7(2), stop")));
        cpu.Step();
        Assert.AreEqual((ushort)1, records[0].Instruction.Address);
        Assert.AreEqual((ushort)7, records[2].Instruction.Address);
        Assert.AreEqual(7u, cpu.GetM(2));
    }

    [TestMethod]
    public void CacheReplacementsPreserveAddressesAcrossManyInstructionValues()
    {
        var memory = new CoreMemory();
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        var jump = new DecodedInstruction(0, Opcode.Uj, 1, InstructionFormat.Long);
        cpu.SetK(1);
        for (ushort address = 0; address < 10_000; address++)
        {
            var load = new DecodedInstruction(1, Opcode.Vtm, address, InstructionFormat.Long);
            memory.Write(1, new Word48(InstructionCodec.EncodeWord(load, jump)));
            cpu.Step();
            Assert.AreEqual((uint)address, cpu.GetM(1));
            cpu.Step();
        }
        memory.Write(1, new Word48(InstructionCodec.EncodeWord(
            new DecodedInstruction(1, Opcode.Vtm, 0, InstructionFormat.Long), jump)));
        cpu.Step();
        Assert.AreEqual(0u, cpu.GetM(1));
    }

    [TestMethod]
    public void FetchWatchStillFiresBeforeExecutionWithWarmCache()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), uj 1")));
        memory.Write(2, new Word48(Besm6.Asm.Assembler.Asm("stop")));
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        cpu.SetK(1);
        cpu.Step();
        cpu.Step();
        cpu.SetM(1, 0);
        cpu.ArmDebugWatch(1, false, 0, 1, 2);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(2u, cpu.GetK());
        Assert.AreEqual(0u, cpu.GetM(1));
        Assert.IsTrue(cpu.Step());
    }

    [TestMethod]
    [DataRow(1u, "atx 2, uj 1")]
    [DataRow(2u, "xta 2, uj 1")]
    public void MemoryWatchStillPreventsAccessWithWarmCache(uint mode, string instruction)
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm(instruction)));
        memory.Write(3, new Word48(Besm6.Asm.Assembler.Asm("stop")));
        var cpu = new Processor(memory) { InstructionCacheEnabled = true };
        cpu.SetK(1);
        cpu.Step();
        cpu.Step();
        memory.Write(2, new Word48(123));
        cpu.SetA(456);
        cpu.ArmDebugWatch(1, false, mode, 2, 3);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(3u, cpu.GetK());
        Assert.AreEqual(123UL, memory.Read(2).Value);
        Assert.AreEqual(456UL, cpu.GetA().Value);
        Assert.IsTrue(cpu.Step());
    }
}
