using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Короткие state-machine тесты для RAU, MOD и stack correction.
    /// Они намеренно намного меньше полных cpu_test/CERNLIB сценариев.
    /// </summary>
    [TestClass]
    [TestCategory("Architecture")]
    public sealed class ProcessorStateRegressionTests
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

        [DataTestMethod]
        [DataRow("сч 2000", (int)RauFlags.Log)]
        [DataRow("и 2000", (int)RauFlags.Log)]
        [DataRow("нтж 2000", (int)RauFlags.Log)]
        [DataRow("или 2000", (int)RauFlags.Log)]
        [DataRow("счрж 7", (int)RauFlags.Log)]
        [DataRow("знак 2000", (int)RauFlags.Add)]
        [DataRow("слц 2000", (int)RauFlags.Mult)]
        public void Instruction_SetsExpectedRauMode(string instruction, int expectedMode)
        {
            StoreWord("10", instruction + ", stop");
            StoreData("2000", 1UL);
            _cpu.SetAcc(0);
            _cpu.SetRau((ulong)(RauFlags.OvfDisable | RauFlags.RoundDisable | RauFlags.Add));
            _cpu.SetPc(O("10"));

            _cpu.Step();

            uint mode = _cpu.GetRau() & (uint)RauFlags.Mode;
            Assert.AreEqual((uint)expectedMode, mode, instruction);
        }

        [TestMethod]
        public void Utc_ModifiesExactlyTheNextInstruction()
        {
            // LEFT: UTC 1 устанавливает MOD для RIGHT.
            // RIGHT: VTM (1) получает effective address 1.
            // Следующее слово: VTM (2) уже не должно видеть предыдущий MOD.
            StoreWord("10", "мода 1, уиа (1)");
            StoreWord("11", "уиа (2), стоп");
            _cpu.SetPc(O("10"));

            _cpu.Step(); // UTC
            Assert.IsTrue(_cpu.ApplyModReg, "После UTC модификатор должен ждать следующую инструкцию.");

            _cpu.Step(); // VTM (1), адрес становится 1
            Assert.AreEqual(1u, _cpu.GetM(1));
            Assert.IsFalse(_cpu.ApplyModReg, "После потребления MOD должен быть снят.");

            _cpu.Step(); // VTM (2), без модификатора
            Assert.AreEqual(0u, _cpu.GetM(2),
                "MOD от UTC не должен протекать через одну инструкцию дальше.");
        }

        [TestMethod]
        public void Utc_LastModifierWinsWhenModifiersAreChained()
        {
            StoreWord("10", "мода 1, мода 2");
            StoreWord("11", "уиа (1), stop");
            _cpu.SetPc(O("10"));

            _cpu.Step(); // MOD=1 for next instruction
            _cpu.Step(); // second UTC sees previous MOD: its own addr becomes 3, then emits MOD=3
            _cpu.Step(); // VTM (1) sees MOD=3

            Assert.AreEqual(3u, _cpu.GetM(1),
                "Цепочка UTC должна применять предыдущий MOD к следующему UTC, как к обычной инструкции.");
        }

        [TestMethod]
        public void StackCorrection_RestoresPreparedStackAfterArithmeticException()
        {
            // C++ PrepareStack для addr=0, reg=017:
            //   --M[017]; corr_stack = 1;
            // При исключении Machine вызывает stack_correction(), которое возвращает M[017]
            // (M[017] += corr_stack; corr_stack = 0 — Processor.StackCorrection()).
            // Этот тест фиксирует reference-семантику: без корректной corr_stack
            // M[15] остался бы декрементированным.
            _cpu.SetM(15, O("2001"));
            _cpu.SetAcc(0);
            StoreData("2000", 0); // divisor = 0
            StoreWord("10", "дел (17), stop");
            _cpu.SetPc(O("10"));

            ProcessorException? error = null;
            try
            {
                _cpu.Step();
            }
            catch (ProcessorException ex)
            {
                error = ex;
            }

            Assert.IsNotNull(error, "Деление на ноль должно выбросить ProcessorException.");
            Assert.AreEqual("Division by zero", error.Message);
            Assert.AreEqual(O("2000"), _cpu.GetM(15),
                "PrepareStack должен предварительно декрементировать M[15].");

            _cpu.StackCorrection();

            Assert.AreEqual(O("2001"), _cpu.GetM(15),
                "stack_correction должен откатить предварительный декремент после исключения.");
        }

        [TestMethod]
        public void StackCorrection_WithoutPendingCorrection_DoesNotChangeM15()
        {
            _cpu.SetM(15, O("2345"));

            _cpu.StackCorrection();

            Assert.AreEqual(O("2345"), _cpu.GetM(15));
        }
    }
}
