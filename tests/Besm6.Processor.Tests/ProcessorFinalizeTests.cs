using Besm6.Core;

namespace Besm6.Tests
{
    [TestClass]
    public sealed class ProcessorFinalizeTests
    {
        [TestMethod]
        public void Step_AdvancesLeftToRightAndRightToNextWord()
        {
            var memory = new CoreMemory();
            memory.Write(1, EncodeWord(
                new DecodedInstruction(0, Opcode.Vtm, 0, InstructionFormat.Long),
                new DecodedInstruction(0, Opcode.Vtm, 0, InstructionFormat.Long)));
            var cpu = new Processor(memory);

            cpu.Step();
            Assert.AreEqual(1u, cpu.K);
            Assert.IsTrue(cpu.RightInstruction);

            cpu.Step();
            Assert.AreEqual(2u, cpu.K);
            Assert.IsFalse(cpu.RightInstruction);
        }

        [TestMethod]
        public void ModificationRegister_IsAppliedOnceAndThenCleared()
        {
            var memory = new CoreMemory();
            memory.Write(1, EncodeWord(
                new DecodedInstruction(0, Opcode.Utc, 5, InstructionFormat.Long),
                new DecodedInstruction(1, Opcode.Vtm, 2, InstructionFormat.Long)));
            var cpu = new Processor(memory);

            cpu.Step();
            Assert.IsTrue(cpu.ApplyC);
            Assert.AreEqual(5u, cpu.C);

            cpu.Step();
            Assert.AreEqual(7u, cpu.GetM(1));
            Assert.IsFalse(cpu.ApplyC);
        }

        [TestMethod]
        public void Stop_UsesTheSingleNormalHalfAdvance()
        {
            var memory = new CoreMemory();
            memory.Write(1, EncodeWord(
                new DecodedInstruction(0, Opcode.Stop, 0, InstructionFormat.Long),
                new DecodedInstruction(0, Opcode.Vtm, 0, InstructionFormat.Long)));
            var cpu = new Processor(memory);

            Assert.IsTrue(cpu.Step());
            Assert.AreEqual(1u, cpu.K);
            Assert.IsTrue(cpu.RightInstruction);
        }

        [TestMethod]
        public void ArithmeticFailure_LeavesOneStackCorrectionForCaller()
        {
            var memory = new CoreMemory();
            memory.Write(1, EncodeWord(
                new DecodedInstruction(15, Opcode.ADivX, 0, InstructionFormat.Short),
                new DecodedInstruction(0, Opcode.Vtm, 0, InstructionFormat.Long)));
            memory.Write(9, Word48.Zero);
            var cpu = new Processor(memory);
            cpu.SetM(15, 10);
            cpu.SetA(1);

            Assert.ThrowsExactly<ProcessorException>(() => cpu.Step());
            Assert.AreEqual(9u, cpu.GetM(15));

            cpu.StackCorrection();
            Assert.AreEqual(10u, cpu.GetM(15));
            cpu.StackCorrection();
            Assert.AreEqual(10u, cpu.GetM(15));
        }

        private static Word48 EncodeWord(DecodedInstruction left, DecodedInstruction right) =>
            new(InstructionCodec.EncodeWord(left, right));
    }
}
