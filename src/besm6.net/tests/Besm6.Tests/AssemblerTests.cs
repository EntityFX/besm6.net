using Besm6.Asm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    public class DisassemblerTests
    {
        // ─── ToOctal ────────────────────────────────────────────────────────

        [TestMethod]
        public void ToOctal_Zero()
        {
            Assert.AreEqual("0", Disassembler.ToOctal(0));
        }

        [TestMethod]
        public void ToOctal_MultiDigit()
        {
            Assert.AreEqual("7", Disassembler.ToOctal(7));
            Assert.AreEqual("10", Disassembler.ToOctal(8));       // 8 dec = 10 oct
            Assert.AreEqual("71", Disassembler.ToOctal(57));      // 57 dec = 71 oct
            Assert.AreEqual("1000", Disassembler.ToOctal(512));   // 512 dec = 1000 oct
        }

        // ─── DisasmHalf ─────────────────────────────────────────────────────

        [TestMethod]
        public void DisasmHalf_Short_NoAddr()
        {
            // stx = opcode 1, addr=0, reg=0 → только мнемоника.
            Assert.AreEqual("stx", Disassembler.DisasmHalf(1L << 12));
        }

        [TestMethod]
        public void DisasmHalf_Short_WithAddr()
        {
            // xta (opcode 8) 10 → addr 0o10 = 8.
            long hw = (8L << 12) | 8L;
            Assert.AreEqual("xta 10", Disassembler.DisasmHalf(hw));
        }

        [TestMethod]
        public void DisasmHalf_Short_WithAddrAndReg()
        {
            // xta 10(2) → opcode 8, addr 8, reg 2.
            long hw = (2L << 20) | (8L << 12) | 8L;
            Assert.AreEqual("xta 10(2)", Disassembler.DisasmHalf(hw));
        }

        [TestMethod]
        public void DisasmHalf_Long_Stop()
        {
            // stop = LongMadlen[11] → opcode (11<<3)|0x80 = 0o330 = 0xD8.
            long hw = (0xD8L << 12);
            Assert.AreEqual("stop", Disassembler.DisasmHalf(hw));
        }

        [TestMethod]
        public void DisasmHalf_TopShortOpcode()
        {
            // opcode 0o77 (63) → ShortMadlen[63] = "*77".
            long hw = (63L << 12);
            Assert.AreEqual("*77", Disassembler.DisasmHalf(hw));
        }

        [TestMethod]
        public void DisasmHalf_RegOnly_Spacing()
        {
            // адрес 0, рег != 0 → "xta (2)" (пробел, без "0"), как в C++.
            long hw = (2L << 20) | (8L << 12);
            Assert.AreEqual("xta (2)", Disassembler.DisasmHalf(hw));
        }

        [TestMethod]
        public void DisasmHalf_NegativeAddress()
        {
            // длинная, addr = 0o77700 → "-100" (двухкомплементарный, как в C++).
            long hw = 0x87FC0; // long *20 + addr 0x7FC0
            Assert.AreEqual("*20 -100", Disassembler.DisasmHalf(hw));
        }

        // ─── DisasmWord ─────────────────────────────────────────────────────

        [TestMethod]
        public void DisasmWord_LeftOnly()
        {
            ulong word = Assembler.Asm("stx");
            Assert.AreEqual("stx", Disassembler.DisasmWord((long)word));
        }

        [TestMethod]
        public void DisasmWord_TwoHalves()
        {
            // "xta 10, stx 20" → "xta 10,stx 20" (без пробела после запятой).
            ulong word = Assembler.Asm("xta 10, stx 20");
            Assert.AreEqual("xta 10,stx 20", Disassembler.DisasmWord((long)word));
        }

        [TestMethod]
        public void Asm_Disasm_Asm_RoundTrip()
        {
            // Madlen-мнемоника → слово → дизассемблер → слово. Должно совпасть.
            string mnemonic = "xta 10(2)";
            ulong w1 = Assembler.Asm(mnemonic);
            string dis = Disassembler.DisasmWord((long)w1);
            ulong w2 = Assembler.Asm(dis);
            Assert.AreEqual(w1, w2, $"round-trip через дизассемблер дал '{dis}'");
        }

        [TestMethod]
        public void DisasmRange_WithAddresses()
        {
            long[] words = { 1L << 12, (8L << 12) | 8L }; // stx, xta 10
            string result = Disassembler.DisasmRange(words, 0, 2);
            StringAssert.Contains(result, "stx");
            StringAssert.Contains(result, "xta 10");
            StringAssert.Contains(result, "00001"); // адрес 1 → "00001"
        }
    }
    
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