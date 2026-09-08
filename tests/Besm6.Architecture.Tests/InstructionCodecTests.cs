namespace Besm6.Architecture.Tests
{
    [TestClass]
    public sealed class InstructionCodecTests
    {
        [TestMethod]
        public void ShortInstruction_UsesDocumentedBitLayout()
        {
            var instruction = new DecodedInstruction(3, Opcode.Xta, 0x123, InstructionFormat.Short);

            uint encoded = InstructionCodec.EncodeHalf(instruction);

            Assert.AreEqual(0x308123u, encoded);
            Assert.AreEqual(instruction, InstructionCodec.DecodeHalf(encoded));
        }

        [TestMethod]
        public void ExtendedShortAddress_RoundTrips()
        {
            var instruction = new DecodedInstruction(3, Opcode.Xta, 0x7FFB, InstructionFormat.Short);

            uint encoded = InstructionCodec.EncodeHalf(instruction);

            Assert.AreEqual(0x348FFBu, encoded);
            Assert.AreEqual(instruction, InstructionCodec.DecodeHalf(encoded));
        }

        [TestMethod]
        public void LongInstruction_RoundTrips()
        {
            var instruction = new DecodedInstruction(15, Opcode.Vlm, 0x4321, InstructionFormat.Long);

            uint encoded = InstructionCodec.EncodeHalf(instruction);

            Assert.AreEqual(0xFFC321u, encoded);
            Assert.AreEqual(instruction, InstructionCodec.DecodeHalf(encoded));
        }

        [TestMethod]
        public void WordCodec_PreservesBothHalves()
        {
            var left = new DecodedInstruction(0, Opcode.Xta, 8, InstructionFormat.Short);
            var right = new DecodedInstruction(2, Opcode.Uj, 0x1234, InstructionFormat.Long);

            ulong encoded = InstructionCodec.EncodeWord(left, right);
            var decoded = InstructionCodec.DecodeWord(encoded);

            Assert.AreEqual(0x0080082C1234UL, encoded);
            Assert.AreEqual(left, decoded.Left);
            Assert.AreEqual(right, decoded.Right);
        }

        [TestMethod]
        public void EncodeHalf_RejectsUnrepresentableShortAddress()
        {
            var instruction = new DecodedInstruction(0, Opcode.Xta, 0x1000, InstructionFormat.Short);

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InstructionCodec.EncodeHalf(instruction));
        }

        [TestMethod]
        public void EncodeHalf_RejectsRegisterOutsideArchitectureRange()
        {
            var instruction = new DecodedInstruction(16, Opcode.Xta, 0, InstructionFormat.Short);

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InstructionCodec.EncodeHalf(instruction));
        }
    }
}
