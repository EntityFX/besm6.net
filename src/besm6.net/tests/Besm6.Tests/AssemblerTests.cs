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
            long word = Assembler.Asm("stx");
            // stx = opcode 1, reg=0, addr=0 → left half = 1 << 12
            Assert.AreEqual(1L << 12, word >> 24);
        }

        [TestMethod]
        public void Asm_MnemonicWithAddress()
        {
            // "xta 10" → opcode=8(xta), addr="10" octal=8, reg=0
            long word = Assembler.Asm("xta 10");
            long left = word >> 24;
            Assert.AreEqual(8L, (left >> 12) & 0x7FL);   // opcode mask
            Assert.AreEqual(8L, left & 0xFFFL);           // addr mask, "10" oct = 8 dec
        }

        [TestMethod]
        public void Asm_MnemonicWithRegister()
        {
            // "xta 10(2)" → opcode=8, addr="10" oct=8, reg=2
            long word = Assembler.Asm("xta 10(2)");
            long left = word >> 24;
            Assert.AreEqual(8L, (left >> 12) & 0x7FL);
            Assert.AreEqual(8L, left & 0xFFFL);
            Assert.AreEqual(2L, (left >> 20) & 7L);
        }

        [TestMethod]
        public void Asm_OctalForm()
        {
            // "0 1 10" = reg=0, opcode=1, addr="10" oct=8
            long word = Assembler.Asm("0 1 10");
            long left = word >> 24;
            Assert.AreEqual(0L, (left >> 20) & 7L);
            Assert.AreEqual(1L, (left >> 12) & 0x7FL);
            Assert.AreEqual(8L, left & 0xFFFL);
        }

        [TestMethod]
        public void Asm_TwoHalfWords()
        {
            // "xta 10, stx 20" → left: opcode=8, addr=8; right: opcode=1, addr=16
            long word = Assembler.Asm("xta 10, stx 20");
            long left = (word >> 24) & 0xFFFFFFL;
            long right = word & 0xFFFFFFL;
            Assert.AreEqual(8L, (left >> 12) & 0x7FL);   // xta
            Assert.AreEqual(8L, left & 0xFFFL);            // "10" oct = 8
            Assert.AreEqual(1L, (right >> 12) & 0x7FL);  // stx
            Assert.AreEqual(16L, right & 0xFFFL);          // "20" oct = 16
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
            long word = Assembler.Asm("xta 10(2)");
            string dis = Disassembler.DisasmWord(word);
            StringAssert.Contains(dis, "xta");
        }
    }
}