using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// P1: PrepareStack-wiring — все 20 инструкций, использующих PrepareStack
    /// (src/besm6.net/Core/InstructionExecutor.cs), обязаны делать pre-decrement
    /// M[17 oct] ТОЛЬКО при addr==0 и reg==17 oct. Отказ одного из call-sites
    /// (упущенный вызов PrepareStack) виден именно здесь, а не в сценариях.
    /// Референс: ref/processor.cpp (prepare-stack в case 004..030).
    /// Исключительный путь (дел (17) + StackCorrection) — в
    /// ProcessorStateRegressionTests.StackCorrection_RestoresPreparedStackAfterArithmeticException.
    /// </summary>
    [TestClass]
    [TestCategory("Architecture")]
    public sealed class ProcessorStackSemanticsTests
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
        private void StoreData(string address, ulong value) => _memory.Write(O(address), new Word48(value));

        private const uint Pc = 0x0008;      // 0010 oct = 8 dec
        private const uint StackTop = 0x0401; // 2001 oct = 1025 dec
        private const uint OperandAddr = 0x0400; // 2000 oct = 1024 dec

        /// <summary>
        /// Каждая из 20 PrepareStack-инструкций: `<mnem> (17)` (addr=0, reg=17 oct)
        /// обязана сделать M[17 oct] := M[17 oct] - 1 ДО чтения операнда.
        /// Операнд — единица: безопасен для всех (для дел — делитель ≠ 0).
        /// </summary>
        [DataTestMethod]
        [DataRow("сл")]   [DataRow("вч")]   [DataRow("вчоб")] [DataRow("вчаб")]
        [DataRow("сч")]   [DataRow("и")]    [DataRow("нтж")]  [DataRow("слц")]
        [DataRow("знак")] [DataRow("или")]  [DataRow("дел")]  [DataRow("умн")]
        [DataRow("сбр")]  [DataRow("рзб")]  [DataRow("чед")]  [DataRow("нед")]
        [DataRow("слп")]  [DataRow("вчп")]  [DataRow("сд")]   [DataRow("рж")]
        public void PrepareStack_AllOpcodes_PreDecrementStackOnAddr0Reg17(string mnemonic)
        {
            StoreWord("10", mnemonic + " (17), стоп");
            // Каноническое плавающее 1.0 — валидный операнд для дел (сырое 1 = «деление на ноль»
            // по определению нуля БЭСМ-6); для остальных инструкций — просто число.
            StoreData("2000", Besm6Math.DoubleToBesm6(1.0));
            _cpu.SetM(15, StackTop);
            _cpu.SetAcc(0UL);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(OperandAddr, _cpu.GetM(15),
                mnemonic + " (17): M[17 oct] обязан декрементироваться до чтения операнда");
        }

        /// <summary>
        /// Контра-пример: addr != 0 (регистр 17) — стека НЕ трогают.
        /// </summary>
        [DataTestMethod]
        [DataRow("сл")]
        [DataRow("дел")]
        [DataRow("сч")]
        public void PrepareStack_NonZeroAddress_StackUntouched(string mnemonic)
        {
            StoreWord("10", mnemonic + " 2000(17), стоп");
            StoreData("2000", Besm6Math.DoubleToBesm6(1.0)); // валидный делитель для дел
            _cpu.SetM(15, StackTop);
            _cpu.SetAcc(0UL);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(StackTop, _cpu.GetM(15),
                mnemonic + " 2000(17): addr != 0 — декремента стека быть не должно");
        }

        // ─── зп (000/atx): special (17) — запись на стек и INCREMENT ───────
        /// <summary>
        /// зп (17): Aex = M[17 oct]; запись ACC по Aex; при addr==0 и reg==17 oct
        /// M[17 oct]++ (в отличие от всех PrepareStack-инструкций).
        /// </summary>
        [TestMethod]
        public void Zp_StackForm_StoresAtTop_AndIncrements()
        {
            ulong acc = 0x1000200030004UL; // 44 бита (48-битное слово)

            StoreWord("10", "зп (17), стоп");
            _cpu.SetM(15, OperandAddr); // стек-указатель = 2000 oct
            _cpu.SetAcc(acc);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(acc, _memory.Read(OperandAddr).Value, "зп (17) записывает ACC в M[17 oct]");
            Assert.AreEqual(StackTop, _cpu.GetM(15), "зп (17) при addr==0 инкрементирует M[17 oct]");
        }

        // ─── зпм (001/stx): запись, pop, RAU=Logical ───────────────────────
        [TestMethod]
        public void Zpm_StoresAcc_PopsStack_SetsLogical()
        {
            ulong acc = 0x11111111111111UL;
            ulong operand = 0x22222222222222UL;

            StoreWord("10", "зпм 3000, стоп");
            StoreData("2000", operand);
            _cpu.SetM(15, StackTop); // 2001 oct
            _cpu.SetAcc(acc);
            _cpu.SetRau((ulong)RauFlags.Mult);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(acc, _memory.Read(0x0780).Value, "зпм пишет ACC по Aex"); // 3000 oct = 0x780
            Assert.AreEqual(OperandAddr, _cpu.GetM(15), "зпм делает pre-decrement M[17 oct]");
            Assert.AreEqual(operand, _cpu.GetAcc().Value, "зпм загружает ACC со стека");
            Assert.AreEqual((uint)RauFlags.Log, _cpu.GetRau() & (uint)RauFlags.Mode);
        }

        // ─── уим (041/sti): rg==15 vs rg!=15 ───────────────────────────────
        [TestMethod]
        public void Uim_TargetIs15_SkipsStack_UseAccAsReturnAddress()
        {
            // уим 17(0): aex = 15 dec → rg == 15 → pop ПРопускается;
            // ad := (ACC) = 0100 oct; ACC := MemLoad(ad); M[15] := ad (адрес возврата).
            ulong acc = 64UL; // 0100 oct — «адрес возврата»
            ulong memory = 0x44444444444444UL;

            StoreWord("10", "уим 17(0), стоп");
            StoreData("100", memory); // 0100 oct = 64 dec = ad
            _cpu.SetM(15, StackTop);
            _cpu.SetAcc(acc);
            _cpu.SetPc(Pc);

            _cpu.Step();

            // Различение веток: ACC прочитан по ad (не со стека — там 0).
            Assert.AreEqual(memory, _cpu.GetAcc().Value, "уим 17(0): rg==15 → ACC := MemLoad(ad)");
            Assert.AreEqual(acc, _cpu.GetM(15), "уим 17(0) кладёт адрес возврата в M[15]");
        }

        [TestMethod]
        public void Uim_TargetNot15_PopsStack_First()
        {
            // уим 14(0): aex = 12 dec → rg == 12 → pre-decrement M[15],
            // ACC := MemLoad(new M[15]), M[12] := ad (= старое ACC).
            ulong acc = 64UL; // 0100 oct
            ulong operand = 0x33333333333333UL;

            StoreWord("10", "уим 14(0), стоп");
            StoreData("2000", operand);
            _cpu.SetM(15, StackTop);
            _cpu.SetAcc(acc);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(OperandAddr, _cpu.GetM(15), "уим 14(0) делает pre-decrement M[15]");
            Assert.AreEqual(acc, _cpu.GetM(12), "уим 14(0) кладёт адрес возврата в M[12]");
            Assert.AreEqual(operand, _cpu.GetAcc().Value, "уим 14(0) берёт ACC из нового M[15]");
        }
    }
}


