using Besm6.Processor.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Isa;
using Besm6.Processor.Cpu;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;


[TestClass]
public sealed class InstructionExecutedTests
{
    [TestMethod]
    public void LightweightAndFullTrace_AgreeOnHalvesAndStop()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Assembler.Legacy.Assembler.Asm("vtm 1(1), stop")));
        var cpu = new Processor(memory);
        cpu.K = 1;
        var light = new List<Opcode>();
        var full = new List<Opcode>();
        cpu.InstructionExecuted = light.Add;
        cpu.InstructionTrace = record => full.Add(record.Instruction.Opcode);
        Assert.IsFalse(cpu.Step());
        Assert.IsTrue(cpu.Step());
        cpu.CanonPost(cpu.K, cpu.RightInstruction);
        CollectionAssert.AreEqual(new[] { Opcode.Vtm, Opcode.Stop }, light);
        CollectionAssert.AreEqual(full, light);
    }

    [TestMethod]
    public void InterceptAndThrowingStop_AreCountedExactlyOnce()
    {
        foreach (string message in new[] { "Division by zero", "" })
        {
            var memory = new CoreMemory();
            memory.Write(1, new Word48(Assembler.Legacy.Assembler.Asm("*50 1")));
            var cpu = new Processor(memory) { InterceptCount = 1, InterceptAddr = 2 };
            cpu.K = 1;
            var light = new List<Opcode>();
            var full = new List<Opcode>();
            cpu.InstructionExecuted = light.Add;
            cpu.InstructionTrace = record => full.Add(record.Instruction.Opcode);
            cpu.ExtracodeDispatch = _ => throw new ProcessorException(message);
            ProcessorException exception = Assert.ThrowsExactly<ProcessorException>(() => cpu.Step());
            if (message.Length > 0) Assert.IsTrue(cpu.Intercept(exception.Message));
            cpu.CanonPost(cpu.K, cpu.RightInstruction);
            cpu.CanonPost(cpu.K, cpu.RightInstruction);
            Assert.HasCount(1, light);
            CollectionAssert.AreEqual(full, light);
        }
    }

    [TestMethod]
    public void LightweightLoop_DoesNotAllocatePerInstruction()
    {
        var memory = new CoreMemory();
        memory.Write(1, new Word48(Assembler.Legacy.Assembler.Asm("uj 1")));
        var cpu = new Processor(memory);
        cpu.K = 1;
        int count = 0;
        cpu.InstructionExecuted = _ => count++;
        for (int i = 0; i < 10_000; i++) cpu.Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) cpu.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0L, allocated);
        Assert.AreEqual(20_000, count);
    }

    [TestMethod]
    public void SelfModification_IsFetchedAgainIncludingRightHalf()
    {
        var memory = new CoreMemory();
        ulong changed = Assembler.Legacy.Assembler.Asm("atx 1, vtm 7(2)");
        memory.Write(1, new Word48(Assembler.Legacy.Assembler.Asm("atx 1, vtm 3(2)")));
        var cpu = new Processor(memory);
        cpu.K = 1;
        cpu.SetA(changed);
        cpu.Step();
        cpu.Step();
        Assert.AreEqual(7u, cpu.GetM(2));
        Assert.AreEqual(changed, memory.Read(1).Value);
    }
}
