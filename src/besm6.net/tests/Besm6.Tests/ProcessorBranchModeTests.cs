using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// P0: истинностные таблицы условных переходов по/пе (0260/0270) для всех
    /// RAU-режимов + малые control-flow тесты пио/пино/э36/цикл.
    /// Референс: ref/processor.cpp L748-783 (по/пе), L811-840 (пио/пино/э36/цикл).
    /// ВАЖНО: в этом тулчейне C#-литералы с ведущим нулём НЕ октальны,
    /// поэтому все восьмиричные значения — через строковый O(...).
    /// </summary>
    [TestClass]
    [TestCategory("Architecture")]
    public sealed class ProcessorBranchModeTests
    {
        private sealed class LinearMemory : IMemory
        {
            private readonly Word48[] _words = new Word48[32768];
            public Word48 Read(uint address) => _words[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
            public int Size => _words.Length;
        }

        private LinearMemory _memory = null!;
        private Processor _cpu = null!;

        [TestInitialize]
        public void Setup()
        {
            _memory = new LinearMemory();
            _cpu = new Processor(_memory);
        }

        private static ulong Asm(string source) => Besm6.Asm.Assembler.Asm(source);
        private static uint O(string octal) => Convert.ToUInt32(octal, 8);
        private void StoreWord(string address, string source) => _memory.Write(O(address), new Word48(Asm(source)));

        // ─── по (0260/uza) ──────────────────────────────────────────────────
        // ref/processor.cpp L748-764: RMR=ACC; переход, ЕСЛИ:
        //   аддитивный      — BIT41 СБРОШЕН;
        //   мультипликативный — BIT48 УСТАНОВЛЕН;
        //   логический      — ACC == 0;
        //   режим неизвестен — НЕ переход (break).
        [TestMethod]
        [DataRow((int)RauFlags.Add, (long)1, true)]                 // ADD: BIT41 clear → jump
        [DataRow((int)RauFlags.Add, 1L << 40, false)]               // ADD: BIT41 set → no jump
        [DataRow((int)RauFlags.Mult, 1L << 47, true)]               // MUL: BIT48 set → jump
        [DataRow((int)RauFlags.Mult, 1L << 40, false)]              // MUL: BIT48 clear → no jump
        [DataRow((int)RauFlags.Log, 0L, true)]                      // LOG: ACC==0 → jump
        [DataRow((int)RauFlags.Log, 12345L, false)]                 // LOG: ACC!=0 → no jump
        [DataRow(0, 1L << 47, false)]                               // no mode → no jump
        public void Po_BranchTruthTable(int mode, long acc, bool expectBranch) =>
            CheckBranch("по 3000", (RauFlags)mode, (ulong)acc, expectBranch);

        // ─── пе (0270/u1a) ──────────────────────────────────────────────────
        // ref/processor.cpp L766-783: RMR=ACC; переход, ЕСЛИ:
        //   аддитивный      — BIT41 УСТАНОВЛЕН;
        //   мультипликативный — BIT48 СБРОШЕН;
        //   логический      — ACC != 0;
        //   режим неизвестен — переход (fall-thru).
        [TestMethod]
        [DataRow((int)RauFlags.Add, 1L << 40, true)]                // ADD: BIT41 set → jump
        [DataRow((int)RauFlags.Add, 1L, false)]                     // ADD: BIT41 clear → no jump
        [DataRow((int)RauFlags.Mult, 1L << 40, true)]               // MUL: BIT48 clear → jump
        [DataRow((int)RauFlags.Mult, 1L << 47, false)]              // MUL: BIT48 set → no jump
        [DataRow((int)RauFlags.Log, 12345L, true)]                  // LOG: ACC!=0 → jump
        [DataRow((int)RauFlags.Log, 0L, false)]                     // LOG: ACC==0 → no jump
        [DataRow(0, 0L, true)]                                      // no mode → jump
        public void Pe_BranchTruthTable(int mode, long acc, bool expectBranch) =>
            CheckBranch("пе 3000", (RauFlags)mode, (ulong)acc, expectBranch);

        private void CheckBranch(string branchMnemonic, RauFlags mode, ulong acc, bool expectBranch)
        {
            const uint pcStart = 0x0008; // 0010 oct = 8 dec
            uint target = O("3000");

            StoreWord("10", branchMnemonic + ", стоп");
            _cpu.SetAcc(acc);
            _cpu.SetRau((ulong)mode);
            _cpu.SetPc(pcStart);

            _cpu.Step();

            // RMR = ACC — всегда, до решения о переходе (ref L750/L768).
            Assert.AreEqual(acc, _cpu.GetRmr().Value, "RMR обязан получить ACC до ветвления");

            // Ветвление только читает состояние: ACC и RAU-режим обязаны не измениться (ref L748-783).
            Assert.AreEqual(acc, _cpu.GetAcc().Value, "по/пе не меняют ACC");
            Assert.AreEqual((uint)mode & (uint)RauFlags.Mode, _cpu.GetRau() & (uint)RauFlags.Mode,
                "по/пе не меняют RAU-режим");

            if (expectBranch)
            {
                Assert.AreEqual(target, _cpu.GetPc(), "переход должен попасть в Aex");
                Assert.IsFalse(_cpu.OnRightInstruction, "цель перехода — LEFT-половина");
            }
            else
            {
                Assert.AreEqual(pcStart, _cpu.GetPc(), "без перехода PC не меняется");
                Assert.IsTrue(_cpu.OnRightInstruction, "без перехода продолжается RIGHT-половина того же слова");
            }
        }

        // ─── пио (0340/vzm) и пино (0350/v1m) ───────────────────────────────
        // ref/processor.cpp L811-822: переход на ADDR (raw, без M[reg]) при
        // M[reg]==0 (пио) / M[reg]!=0 (пино); цель — LEFT-половина.
        [TestMethod]
        [DataRow("пио", 0u, true)]
        [DataRow("пио", 5u, false)]
        [DataRow("пино", 0u, false)]
        [DataRow("пино", 5u, true)]
        public void PioPino_BranchOnRegisterZeroOrNonZero(string mnemonic, uint regValue, bool expectBranch)
        {
            const uint pcStart = 0x0008; // 0010 oct = 8 dec
            uint target = O("3000");

            StoreWord("10", mnemonic + " 3000(2)");
            _cpu.SetM(2, regValue);
            _cpu.SetPc(pcStart);

            _cpu.Step();

            if (expectBranch)
            {
                Assert.AreEqual(target, _cpu.GetPc(), mnemonic);
                Assert.IsFalse(_cpu.OnRightInstruction, mnemonic);
            }
            else
            {
                Assert.AreEqual(pcStart, _cpu.GetPc(), mnemonic);
                Assert.IsTrue(_cpu.OnRightInstruction, mnemonic);
            }
        }

        // ─── э36 (0360) — «как пио, но с выталкиванием БРЗ» ────────────────
        [TestMethod]
        public void E36_BranchWhenRegisterZero_SameAsPio()
        {
            const uint pcStart = 0x0008; // 0010 oct = 8 dec
            uint target = O("3000");

            StoreWord("10", "втбрз 3000(3)");
            _cpu.SetM(3, 0);
            _cpu.SetPc(pcStart);

            _cpu.Step();

            Assert.AreEqual(target, _cpu.GetPc());
            Assert.IsFalse(_cpu.OnRightInstruction);
        }

        // ─── цикл (0370/vlm) ────────────────────────────────────────────────
        // ref/processor.cpp L832-840: M[reg]==0 → нет перехода и счётчик НЕ
        // меняется; иначе M[reg]++, переход на ADDR (LEFT).
        [TestMethod]
        public void Vlm_ZeroCounter_NoBranch_NoIncrement()
        {
            const uint pcStart = 0x0008; // 0010 oct = 8 dec

            StoreWord("10", "цикл 3000(4)");
            _cpu.SetM(4, 0);
            _cpu.SetPc(pcStart);

            _cpu.Step();

            Assert.AreEqual(pcStart, _cpu.GetPc());
            Assert.IsTrue(_cpu.OnRightInstruction);
            Assert.AreEqual(0u, _cpu.GetM(4), "цикл при M[reg]==0 не трогает счётчик");
        }

        [TestMethod]
        public void Vlm_NonZeroCounter_IncrementsAndBranchesLeft()
        {
            const uint pcStart = 0x0008; // 0010 oct = 8 dec
            uint target = O("3000");

            StoreWord("10", "цикл 3000(4)");
            _cpu.SetM(4, 5);
            _cpu.SetPc(pcStart);

            _cpu.Step();

            Assert.AreEqual(6u, _cpu.GetM(4));
            Assert.AreEqual(target, _cpu.GetPc());
            Assert.IsFalse(_cpu.OnRightInstruction);
        }
    }
}


