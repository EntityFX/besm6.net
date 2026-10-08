namespace Besm6.Tests;

[TestClass]
public sealed class ExecutionFrameOptimizationTests
{
    [TestMethod]
    public void SubroutineReturnAndExtracodeContext_PreserveBothInstructionHalves()
    {
        foreach (bool right in new[] { false, true })
        {
            var memory = new CoreMemory();
            var cpu = new Processor(memory);
            memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("vjm 10(2), vjm 10(2)")));
            cpu.State.K = 1; cpu.State.IsRightHalf = right;
            cpu.SetA(123); cpu.SetY(456);
            cpu.Step();
            Assert.AreEqual(2u, cpu.GetM(2));
            Assert.AreEqual(8u, cpu.GetK());
            Assert.IsFalse(cpu.RightInstruction);
            Assert.AreEqual(123UL, cpu.GetA().Value);
            Assert.AreEqual(456UL, cpu.GetY().Value);

            memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("*50 3, *50 3")));
            cpu.State.K = 1; cpu.State.IsRightHalf = right;
            ExtracodeCall? received = null;
            cpu.ExtracodeDispatch = call =>
            {
                received = call;
                cpu.SetA(321); cpu.SetY(654);
                return true;
            };
            cpu.Step();
            Assert.AreEqual(right, received!.Value.IsRightHalf);
            Assert.AreEqual(2u, cpu.GetK());
            Assert.IsFalse(cpu.RightInstruction);
            Assert.AreEqual(321UL, cpu.GetA().Value);
            Assert.AreEqual(654UL, cpu.GetY().Value);
        }
    }

    private sealed class ObservedMemory : IMemory
    {
        private readonly CoreMemory _memory = new();
        internal Action? OnReadOperand;
        internal Action? OnWriteOperand;
        public int Size => _memory.Size;
        public Word48 Read(uint address)
        {
            if (address == 2) OnReadOperand?.Invoke();
            return _memory.Read(address);
        }
        public void Write(uint address, Word48 word)
        {
            _memory.Write(address, word);
            if (address == 2) OnWriteOperand?.Invoke();
        }
    }

    [TestMethod]
    public void AddressModification_PreservesRegisterCommitWithCustomMemorySideEffects()
    {
        var memory = new ObservedMemory();
        var cpu = new Processor(memory);
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm("wtc 2, stop")));
        memory.Write(2, new Word48(7));
        cpu.SetA(123); cpu.SetY(456);
        memory.OnReadOperand = () => { cpu.SetA(999); cpu.SetY(888); };
        cpu.Step();
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(456UL, cpu.GetY().Value);
        Assert.AreEqual(7u, cpu.State.C);
        Assert.IsTrue(cpu.State.ApplyC);
    }

    [TestMethod]
    [DataRow("xta 2", 77UL, 456UL)]
    [DataRow("atx 2", 123UL, 456UL)]
    [DataRow("aex 2", 54UL, 123UL)]
    [DataRow("xtr 2", 123UL, 456UL)]
    public void CustomMemoryRegisterChangesRetainSnapshotCommit(string instruction, ulong expectedA, ulong expectedY)
    {
        var memory = new ObservedMemory();
        var cpu = new Processor(memory);
        memory.Write(1, new Word48(Besm6.Asm.Assembler.Asm(instruction + ", stop")));
        memory.Write(2, new Word48(77));
        cpu.SetA(123); cpu.SetY(456);
        memory.OnReadOperand = memory.OnWriteOperand = () => { cpu.SetA(999); cpu.SetY(888); };
        cpu.Step();
        Assert.AreEqual(expectedA, cpu.GetA().Value);
        Assert.AreEqual(expectedY, cpu.GetY().Value);
    }

    [TestMethod]
    [DataRow("yta 100")]
    [DataRow("anx 2")]
    [DataRow("asn 100")]
    [DataRow("a+x 2")]
    public void PlainAndCustomMemoryAgreeWhenAluChangesStateDuringHandler(string instruction)
    {
        var plain = new Processor(new CoreMemory());
        var observedMemory = new ObservedMemory();
        var observed = new Processor(observedMemory);
        // Use processor stores so the exact same setup reaches both implementations.
        foreach (var cpu in new[] { plain, observed })
        {
            cpu.MemStore(1, Besm6.Asm.Assembler.Asm(instruction + ", stop"));
            cpu.MemStore(2, Word48.FromDouble(0.5).Value);
            cpu.SetA(Word48.FromDouble(1.25).Value);
            cpu.SetY(0x123456789);
            cpu.SetR((uint)RFlags.Mult);
            cpu.Step();
        }
        Assert.AreEqual(observed.GetA(), plain.GetA());
        Assert.AreEqual(observed.GetY(), plain.GetY());
        Assert.AreEqual(observed.GetR(), plain.GetR());
        Assert.AreEqual(observed.GetK(), plain.GetK());
        Assert.AreEqual(observed.RightInstruction, plain.RightInstruction);
    }
}
