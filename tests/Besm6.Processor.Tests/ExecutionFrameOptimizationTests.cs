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
        public int Size => _memory.Size;
        public Word48 Read(uint address)
        {
            if (address == 2) OnReadOperand?.Invoke();
            return _memory.Read(address);
        }
        public void Write(uint address, Word48 word) => _memory.Write(address, word);
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
}
