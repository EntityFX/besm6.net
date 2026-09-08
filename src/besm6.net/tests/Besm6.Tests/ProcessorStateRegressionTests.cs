using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Besm6.Tests
{
    /// <summary>
    /// Короткие state-machine тесты для R, C и stack correction.
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

        /// <summary>
        /// Полная таблица «инструкция → итоговый R-режим» для ВСЕХ
        /// mode-changing инструкций (референс: set_logical/set_additive/set_multiplicative
        /// в ref/processor.cpp). Старт в режиме, ОТЛИЧНОМ от ожидаемого: итог обязан
        /// измениться именно инструкцией. счмр (031) исключён — её режим условный
        /// (зависит от входящего режима).
        /// </summary>
        [TestMethod]
        // ── Logical (set_logical) ──
        [DataRow("зпм 2000", (int)RFlags.Log)]
        [DataRow("счм 2000", (int)RFlags.Log)]
        [DataRow("сч 2000", (int)RFlags.Log)]
        [DataRow("и 2000", (int)RFlags.Log)]
        [DataRow("нтж 2000", (int)RFlags.Log)]
        [DataRow("или 2000", (int)RFlags.Log)]
        [DataRow("сбр 2000", (int)RFlags.Log)]
        [DataRow("рзб 2000", (int)RFlags.Log)]
        [DataRow("чед 2000", (int)RFlags.Log)]
        [DataRow("нед 2000", (int)RFlags.Log)]
        [DataRow("сд 2000", (int)RFlags.Log)]
        [DataRow("счрж 7", (int)RFlags.Log)]
        [DataRow("сда 2000", (int)RFlags.Log)]
        [DataRow("уим 2000", (int)RFlags.Log)]
        [DataRow("счи 2000", (int)RFlags.Log)]
        // ── Additive (set_additive) ──
        [DataRow("сл 2000", (int)RFlags.Add)]
        [DataRow("вч 2000", (int)RFlags.Add)]
        [DataRow("вчоб 2000", (int)RFlags.Add)]
        [DataRow("вчаб 2000", (int)RFlags.Add)]
        [DataRow("знак 2000", (int)RFlags.Add)]
        // ── Multiplicative (set_multiplicative) ──
        [DataRow("слц 2000", (int)RFlags.Mult)]
        [DataRow("дел 2000", (int)RFlags.Mult)]
        [DataRow("умн 2000", (int)RFlags.Mult)]
        [DataRow("слп 2000", (int)RFlags.Mult)]
        [DataRow("вчп 2000", (int)RFlags.Mult)]
        [DataRow("слпа 2000", (int)RFlags.Mult)]
        [DataRow("вчпа 2000", (int)RFlags.Mult)]
        public void Instruction_SetsExpectedRMode(string instruction, int expectedMode)
        {
            StoreWord("10", instruction + ", stop");
            // Каноническое плавающее 1.0: валидный делитель для дел (сырое 1 = «ноль»
            // по определению нуля БЭСМ-6) и нейтральный операнд для остальных.
            StoreData("2000", Besm6Math.DoubleToBesm6(1.0));
            _cpu.SetA(0);
            // Старт в режиме, отличном от ожидаемого: итог обязан измениться инструкцией.
            uint start = expectedMode == (int)RFlags.Log ? (uint)RFlags.Add : (uint)RFlags.Log;
            _cpu.SetR((ulong)(RFlags.OvfDisable | RFlags.RoundDisable | (RFlags)start));
            _cpu.SetK(O("10"));

            _cpu.Step();

            uint mode = _cpu.GetR() & (uint)RFlags.Mode;
            Assert.AreEqual((uint)expectedMode, mode, instruction);
        }

        [TestMethod]
        public void Utc_ModifiesExactlyTheNextInstruction()
        {
            // LEFT: UTC 1 устанавливает C для RIGHT.
            // RIGHT: VTM (1) получает effective address 1.
            // Следующее слово: VTM (2) уже не должно видеть предыдущий C.
            StoreWord("10", "мода 1, уиа (1)");
            StoreWord("11", "уиа (2), стоп");
            _cpu.SetK(O("10"));

            _cpu.Step(); // UTC
            Assert.IsTrue(_cpu.ApplyC, "После UTC модификатор должен ждать следующую инструкцию.");

            _cpu.Step(); // VTM (1), адрес становится 1
            Assert.AreEqual(1u, _cpu.GetM(1));
            Assert.IsFalse(_cpu.ApplyC, "После потребления C должен быть снят.");

            _cpu.Step(); // VTM (2), без модификатора
            Assert.AreEqual(0u, _cpu.GetM(2),
                "C от UTC не должен протекать через одну инструкцию дальше.");
        }

        [TestMethod]
        public void Utc_LastModifierWinsWhenModifiersAreChained()
        {
            StoreWord("10", "мода 1, мода 2");
            StoreWord("11", "уиа (1), stop");
            _cpu.SetK(O("10"));

            _cpu.Step(); // C=1 for next instruction
            _cpu.Step(); // second UTC sees previous C: its own addr becomes 3, then emits C=3
            _cpu.Step(); // VTM (1) sees C=3

            Assert.AreEqual(3u, _cpu.GetM(1),
                "Цепочка UTC должна применять предыдущий C к следующему UTC, как к обычной инструкции.");
        }

        [TestMethod]
        public void StackCorrection_RestoresPreparedStackAfterArithmeticException()
        {
            //   --M[017]; corr_stack = 1;
            // При исключении Machine вызывает stack_correction(), которое возвращает M[017]
            // (M[017] += corr_stack; corr_stack = 0 — Processor.StackCorrection()).
            // Этот тест фиксирует reference-семантику: без корректной corr_stack
            // M[15] остался бы декрементированным.
            _cpu.SetM(15, O("2001"));
            _cpu.SetA(0);
            StoreData("2000", 0); // divisor = 0
            StoreWord("10", "дел (17), stop");
            _cpu.SetK(O("10"));

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
