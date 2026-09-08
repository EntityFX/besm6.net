using Besm6.Core;

namespace Besm6.Tests
{
    [TestClass]
    public sealed class ProcessorTraceContractTests
    {
        [TestMethod]
        public void InstructionTrace_ReportsPreAndPostStateForExecutedHalf()
        {
            var memory = new CoreMemory();
            var instruction = new DecodedInstruction(0, Opcode.Xta, 2, InstructionFormat.Short);
            ulong word = (ulong)InstructionCodec.EncodeHalf(instruction) << 24;
            memory.Write(1, new Word48(word));
            memory.Write(2, new Word48(5));
            var cpu = new Processor(memory);
            InstructionTraceRecord? observed = null;
            cpu.InstructionTrace = record => observed = record;

            bool stopped = cpu.Step();

            Assert.IsFalse(stopped);
            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual(1u, observed.Value.Before.K);
            Assert.IsFalse(observed.Value.Before.IsRightHalf);
            Assert.AreEqual(Word48.Zero, observed.Value.Before.A);
            Assert.AreEqual(1u, observed.Value.After.K);
            Assert.IsTrue(observed.Value.After.IsRightHalf);
            Assert.AreEqual(new Word48(5), observed.Value.After.A);
            Assert.AreEqual(instruction, observed.Value.Instruction);
            Assert.AreEqual(word, observed.Value.RawWord.Value);
        }

        [TestMethod]
        public void RegisterTrace_ReportsChangedAccumulator()
        {
            var memory = new CoreMemory();
            var instruction = new DecodedInstruction(0, Opcode.Xta, 2, InstructionFormat.Short);
            memory.Write(1, new Word48((ulong)InstructionCodec.EncodeHalf(instruction) << 24));
            memory.Write(2, new Word48(5));
            var cpu = new Processor(memory);
            var records = new List<RegisterTraceRecord>();
            cpu.RegisterTrace = records.Add;

            cpu.Step();

            CollectionAssert.Contains(records, new RegisterTraceRecord("A", 5));
        }

        [TestMethod]
        public void ExtracodeDispatch_ReceivesCompleteCall()
        {
            var memory = new CoreMemory();
            ushort rawAddress = Convert.ToUInt16("1234", 8);
            var instruction = new DecodedInstruction(
                2,
                (Opcode)(int)Extracode.E50,
                rawAddress,
                InstructionFormat.Short);
            memory.Write(1, new Word48((ulong)InstructionCodec.EncodeHalf(instruction) << 24));
            var cpu = new Processor(memory);
            cpu.SetM(2, 7);
            ExtracodeCall? observed = null;
            cpu.ExtracodeDispatch = call => { observed = call; return true; };

            cpu.Step();

            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual(
                new ExtracodeCall(Extracode.E50, rawAddress + 7u, 2, rawAddress, false),
                observed.Value);
        }
    }
}
