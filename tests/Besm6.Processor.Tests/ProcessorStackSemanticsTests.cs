using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Besm6.Tests
{
    /// <summary>
    /// P1: PrepareStack-wiring — все 20 инструкций, использующих PrepareStack
    /// (src/Besm6.Processor/Core/InstructionExecutor.cs), обязаны делать pre-decrement
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

        private const uint K = 0x0008;      // 0010 oct = 8 dec
        private const uint StackTop = 0x0401; // 2001 oct = 1025 dec
        private const uint OperandAddr = 0x0400; // 2000 oct = 1024 dec

        /// <summary>
        /// Каждая из 20 PrepareStack-инструкций: `<mnem> (17)` (addr=0, reg=17 oct)
        /// обязана сделать M[17 oct] := M[17 oct] - 1 ДО чтения операнда.
        /// Операнд — единица: безопасен для всех (для дел — делитель ≠ 0).
        /// </summary>
        [TestMethod]
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
            _cpu.SetA(0UL);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(OperandAddr, _cpu.GetM(15),
                mnemonic + " (17): M[17 oct] обязан декрементироваться до чтения операнда");
        }

        /// <summary>
        /// Контра-пример: addr != 0 (регистр 17) — стека НЕ трогают.
        /// </summary>
        [TestMethod]
        [DataRow("сл")]
        [DataRow("дел")]
        [DataRow("сч")]
        public void PrepareStack_NonZeroAddress_StackUntouched(string mnemonic)
        {
            StoreWord("10", mnemonic + " 2000(17), стоп");
            StoreData("4001", Besm6Math.DoubleToBesm6(1.0)); // 4001 oct = 2000 oct + M[15](2001 oct) = aex
            _cpu.SetM(15, StackTop);
            _cpu.SetA(0UL);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(StackTop, _cpu.GetM(15),
                mnemonic + " 2000(17): addr != 0 — декремента стека быть не должно");
        }

        // ─── зп (000/atx): special (17) — запись на стек и INCREMENT ───────
        /// <summary>
        /// зп (17): Aex = M[17 oct]; запись A по Aex; при addr==0 и reg==17 oct
        /// M[17 oct]++ (в отличие от всех PrepareStack-инструкций).
        /// </summary>
        [TestMethod]
        public void Atx_StackForm_StoresAtTop_AndIncrements()
        {
            ulong a = 0x200030004UL; // 48-bit word, safe 9-digit literal

            StoreWord("10", "зп (17), стоп");
            _cpu.SetM(15, OperandAddr); // стек-указатель = 2000 oct
            _cpu.SetA(a);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(a, _memory.Read(OperandAddr).Value, "зп (17) записывает A в M[17 oct]");
            Assert.AreEqual(StackTop, _cpu.GetM(15), "зп (17) при addr==0 инкрементирует M[17 oct]");
        }

        // ─── зпм (001/stx): запись, pop, R=Logical ───────────────────────
        [TestMethod]
        public void Stx_StoresA_PopsStack_SetsLogical()
        {
            ulong a = 0x200030004UL;
            ulong operand = 0x400050006UL;

            StoreWord("10", "зпм 3000, стоп");
            StoreData("2000", operand);
            _cpu.SetM(15, StackTop); // 2001 oct
            _cpu.SetA(a);
            _cpu.SetR((ulong)RFlags.Mult);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(a, _memory.Read(0x0600).Value, "zpm writes A at Aex"); // 3000 oct = 1536 dec = 0x0600
            Assert.AreEqual(OperandAddr, _cpu.GetM(15), "зпм делает pre-decrement M[17 oct]");
            Assert.AreEqual(operand, _cpu.GetA().Value, "зпм загружает A со стека");
            Assert.AreEqual((uint)RFlags.Log, _cpu.GetR() & (uint)RFlags.Mode);
        }

        // ─── уим (041/sti): rg==15 vs rg!=15 ───────────────────────────────
        [TestMethod]
        public void Sti_TargetIs15_SkipsStack_UseAAsReturnAddress()
        {
            // уим 17(0): aex = 15 dec → rg == 15 → pop ПРопускается;
            // ad := (A) = 0100 oct; A := MemLoad(ad); M[15] := ad (адрес возврата).
            ulong a = 64UL; // 0100 oct — «адрес возврата»
            ulong memory = 0x400050006UL;

            StoreWord("10", "уим 17(0), стоп");
            StoreData("100", memory); // 0100 oct = 64 dec = ad
            _cpu.SetM(15, StackTop);
            _cpu.SetA(a);
            _cpu.SetK(K);

            _cpu.Step();

            // Различение веток: A прочитан по ad (не со стека — там 0).
            Assert.AreEqual(memory, _cpu.GetA().Value, "уим 17(0): rg==15 → A := MemLoad(ad)");
            Assert.AreEqual(a, _cpu.GetM(15), "уим 17(0) кладёт адрес возврата в M[15]");
        }

        [TestMethod]
        public void Sti_TargetNot15_PopsStack_First()
        {
            // уим 14(0): aex = 12 dec → rg == 12 → pre-decrement M[15],
            // A := MemLoad(new M[15]), M[12] := ad (= старое A).
            ulong a = 64UL; // 0100 oct
            ulong operand = 0x600070008UL;

            StoreWord("10", "уим 14(0), стоп");
            StoreData("2000", operand);
            _cpu.SetM(15, StackTop);
            _cpu.SetA(a);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(OperandAddr, _cpu.GetM(15), "уим 14(0) делает pre-decrement M[15]");
            Assert.AreEqual(a, _cpu.GetM(12), "уим 14(0) кладёт адрес возврата в M[12]");
            Assert.AreEqual(operand, _cpu.GetA().Value, "уим 14(0) берёт A из нового M[15]");
        }
    }
}


