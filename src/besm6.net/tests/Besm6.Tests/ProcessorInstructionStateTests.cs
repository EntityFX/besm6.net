using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// P1: RMR-побочные эффекты и RAU-матрица на уровне отдельных инструкций.
    /// Референс: ref/processor.cpp (case 004..047) — точные эффекты на ACC/RMR/RAU.
    /// Целенаправленно маленькие: фиксируют ПЕРВЫЙ сломанный переход состояния,
    /// а не сценарный поток (сценарии — в ProcessorTests.cs).
    /// </summary>
    [TestClass]
    [TestCategory("Architecture")]
    public sealed class ProcessorInstructionStateTests
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

        private const uint Pc = 0x0008;  // 0010 oct = 8 dec
        private const uint Data = 0x0400;  // 2000 oct = 1024 dec

        // ─── RMR-эффекты ────────────────────────────────────────────────────

        /// <summary>нтж (012/aex): RMR получает СТАРОЕ ACC до XOR.</summary>
        [TestMethod]
        public void Ntzh_PreservesOldAccInRmr()
        {
            ulong oldAcc = 0x1000200030004UL; // 44 бита (48-битное слово)
            ulong operand = 0x0000FFFF0000FFFFUL;

            StoreWord("10", "нтж 2000, стоп");
            StoreData("2000", operand);
            _cpu.SetAcc(oldAcc);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(oldAcc, _cpu.GetRmr().Value, "RMR обязан сохранить ACC до нтж");
            Assert.AreEqual(oldAcc ^ operand, _cpu.GetAcc().Value);
            Assert.AreEqual((uint)RauFlags.Log, _cpu.GetRau() & (uint)RauFlags.Mode);
        }

        [DataTestMethod]
        [DataRow("и 2000")]   // 011 aax
        [DataRow("или 2000")] // 015 aox
        [DataRow("сбр 2000")] // 020 apx
        [DataRow("рзб 2000")] // 021 aux
        [DataRow("чед 2000")] // 022 acx
        public void LogicalArith_OpcodesClearRmr(string instruction)
        {
            StoreWord("10", instruction + ", стоп");
            StoreData("2000", 1UL);
            _cpu.SetAcc(0x123456789ABCDEUL);
            _cpu.SetRmr(0xFEDCBA98765431UL);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(0UL, _cpu.GetRmr().Value, instruction + ": RMR обязан обнулиться");
        }

        /// <summary>счмр (031/yta), логический режим: ACC := RMR (ref: acc = rmr).</summary>
        [TestMethod]
        public void Schmr_LogicalMode_CopiesRmrToAcc()
        {
            ulong rmrValue = 0x0ABCDEF01234UL; // 48 бит

            StoreWord("10", "счмр 2000, стоп");
            StoreData("2000", 1UL);
            _cpu.SetAcc(0x99999999999999UL);
            _cpu.SetRmr(rmrValue);
            _cpu.SetRau((ulong)RauFlags.Log);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(rmrValue, _cpu.GetAcc().Value, "в логическом режиме счмр копирует RMR в ACC");
        }

        /// <summary>счмр (031/yta), НЕ логический режим: мантисса (40 бит) берётся из RMR,
        /// экспонента ACC сохраняется (выбран адрес 0100 oct → дельта экспоненты 0),
        /// RAU остаётся аддитивным.</summary>
        [TestMethod]
        public void Schmr_AdditiveMode_TakesRmrMantissa_KeepsAdditive()
        {
            ulong mantissa40 = 0xFFFF0000FFUL;      // ровно 40 бит
            ulong rmrValue = (1UL << 47) | mantissa40;
            ulong expField = 64UL << 41;            // экспонента 64 (bias) — дельта 0
            ulong expected = expField | mantissa40;

            StoreWord("10", "счмр 100, стоп"); // 0100 oct = 64 dec → aex&077 = 64 → дельта 0
            _cpu.SetAcc(expField | (1UL << 39));
            _cpu.SetRmr(rmrValue);
            _cpu.SetRau((ulong)RauFlags.Add);
            _cpu.SetPc(Pc);

            _cpu.Step();

            Assert.AreEqual(expected, _cpu.GetAcc().Value, "экспонента ACC + 40-битная мантисса RMR");
            Assert.AreEqual((uint)RauFlags.Add, _cpu.GetRau() & (uint)RauFlags.Mode,
                "в не-логическом режиме счмр НЕ переключает RAU");
        }

        /// <summary>нед (023/anx): ACC==0 → RMR=0, ACC:=операнд; ACC!=0 → RMR=высшедшие биты.</summary>
        [DataTestMethod]
        [DataRow(0UL, true)]      // ACC==0  → RMR обязан быть 0
        [DataRow(0x1234UL, false)] // ACC!=0 → RMR ≠ 0 (вытесненные биты сдвига)
        public void Ned_BranchDependentRmr(ulong acc, bool expectZeroRmr)
        {
            StoreWord("10", "нед 2000, стоп");
            StoreData("2000", 5UL);
            _cpu.SetAcc(acc);
            _cpu.SetPc(Pc);

            _cpu.Step();

            if (expectZeroRmr)
                Assert.AreEqual(0UL, _cpu.GetRmr().Value, "нед при ACC==0 обнуляет RMR");
            else
                Assert.AreNotEqual(0UL, _cpu.GetRmr().Value, "нед при ACC!=0 кладёт в RMR степень сдвига");
        }

        // ─── RAU-матрица: инструкция → итоговый режим АЛУ ──────────────────
        // Дополнение таблицы Instruction_SetsExpectedRauMode (там уже есть
        // сч/и/нтж/или/счрж/знак/слц): здесь оставшиеся явные SetLogical/
        // SetAdditive/SetMultiplicative-вызовы в InstructionExecutor.
        [DataTestMethod]
        [DataRow("сл 2000", (int)RauFlags.Add)]
        [DataRow("вч 2000", (int)RauFlags.Add)]
        [DataRow("вчоб 2000", (int)RauFlags.Add)]
        [DataRow("вчаб 2000", (int)RauFlags.Add)]
        [DataRow("умн 2000", (int)RauFlags.Mult)]
        [DataRow("дел 2000", (int)RauFlags.Mult)]
        [DataRow("слп 2000", (int)RauFlags.Mult)]
        [DataRow("вчп 2000", (int)RauFlags.Mult)]
        [DataRow("слпа 2000", (int)RauFlags.Mult)]
        [DataRow("вчпа 2000", (int)RauFlags.Mult)]
        [DataRow("сд 2000", (int)RauFlags.Log)]
        [DataRow("сда 2000", (int)RauFlags.Log)]
        [DataRow("зпм 2000", (int)RauFlags.Log)]
        [DataRow("счм", (int)RauFlags.Log)]
        [DataRow("счи 2000", (int)RauFlags.Log)]
        [DataRow("уим 2000", (int)RauFlags.Log)]
        public void Instruction_SetsExpectedRauMode_Extended(string instruction, int expectedMode)
        {
            StoreWord("10", instruction + ", стоп");
            // Каноническое плавающее 1.0: валидный делитель для дел, нейтральный операнд для остальных.
            StoreData("2000", Besm6Math.DoubleToBesm6(1.0));
            _cpu.SetAcc(0UL);
            // Старт в НЕ-ожидаемом режиме: итог обязан измениться именно инструкцией.
            _cpu.SetRau((ulong)(RauFlags.OvfDisable | RauFlags.RoundDisable | RauFlags.Mult));
            _cpu.SetPc(Pc);

            _cpu.Step();

            uint mode = _cpu.GetRau() & (uint)RauFlags.Mode;
            Assert.AreEqual((uint)expectedMode, mode, instruction);
        }
    }
}


