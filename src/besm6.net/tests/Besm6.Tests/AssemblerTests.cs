using Besm6.Asm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    public class AssemblerTests
    {
        [TestMethod]
        public void Asm_ShortOpcode_NoAddress()
        {
            ulong word = Assembler.Asm("stx");
            // stx = opcode 1, reg=0, addr=0 → left half = 1 << 12
            Assert.AreEqual(1UL << 12, word >> 24);
        }

        [TestMethod]
        public void Asm_MnemonicWithAddress()
        {
            // "xta 10" → opcode=8(xta), addr="10" octal=8, reg=0
            ulong word = Assembler.Asm("xta 10");
            ulong left = word >> 24;
            Assert.AreEqual(8UL, (left >> 12) & 0x7FUL);   // opcode mask
            Assert.AreEqual(8UL, left & 0xFFFUL);           // addr mask, "10" oct = 8 dec
        }

        [TestMethod]
        public void Asm_MnemonicWithRegister()
        {
            // "xta 10(2)" → opcode=8, addr="10" oct=8, reg=2
            ulong word = Assembler.Asm("xta 10(2)");
            ulong left = word >> 24;
            Assert.AreEqual(8UL, (left >> 12) & 0x7FUL);
            Assert.AreEqual(8UL, left & 0xFFFUL);
            Assert.AreEqual(2UL, (left >> 20) & 7UL);
        }

        [TestMethod]
        public void Asm_OctalForm()
        {
            // "0 1 10" = reg=0, opcode=1, addr="10" oct=8
            ulong word = Assembler.Asm("0 1 10");
            ulong left = word >> 24;
            Assert.AreEqual(0UL, (left >> 20) & 7UL);
            Assert.AreEqual(1UL, (left >> 12) & 0x7FUL);
            Assert.AreEqual(8UL, left & 0xFFFUL);
        }

        [TestMethod]
        public void Asm_TwoHalfWords()
        {
            // "xta 10, stx 20" → left: opcode=8, addr=8; right: opcode=1, addr=16
            ulong word = Assembler.Asm("xta 10, stx 20");
            ulong left = (word >> 24) & 0xFFFFFFUL;
            ulong right = word & 0xFFFFFFUL;
            Assert.AreEqual(8UL, (left >> 12) & 0x7FUL);   // xta
            Assert.AreEqual(8UL, left & 0xFFFUL);            // "10" oct = 8
            Assert.AreEqual(1UL, (right >> 12) & 0x7FUL);  // stx
            Assert.AreEqual(16UL, right & 0xFFFUL);          // "20" oct = 16
        }

        [TestMethod]
        public void Asm_OpcodeTable_Lookup()
        {
            Assert.IsTrue(OpcodeTable.TryGetOpcode("stx", out int op));
            Assert.AreEqual(1, op);

            Assert.IsTrue(OpcodeTable.TryGetOpcode("xta", out op));
            Assert.AreEqual(8, op);

            Assert.IsTrue(OpcodeTable.TryGetOpcode("stop", out op));
            Assert.AreEqual(0x80 | (11 << 3), op); // stop = long index 11

            Assert.IsFalse(OpcodeTable.TryGetOpcode("bogus", out _));
        }

        [TestMethod]
        public void Asm_DisasmRoundTrip()
        {
            ulong word = Assembler.Asm("xta 10(2)");
            string dis = Disassembler.DisasmWord((long)word);
            StringAssert.Contains(dis, "xta");
        }
    }
}