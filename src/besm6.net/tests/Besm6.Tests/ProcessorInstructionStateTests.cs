using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Besm6.Tests
{
    /// <summary>
    /// P1: Y-побочные эффекты и R-матрица на уровне отдельных инструкций.
    /// Референс: ref/processor.cpp (case 004..047) — точные эффекты на A/Y/R.
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

        private const uint K = 0x0008;  // 0010 oct = 8 dec
        private const uint Data = 0x0400;  // 2000 oct = 1024 dec

        // ─── Y-эффекты ────────────────────────────────────────────────────

        /// <summary>нтж (012/aex): Y получает СТАРОЕ A до XOR.</summary>
        [TestMethod]
        public void Aex_PreservesOldAInY()
        {
            ulong oldA = 0x200030004UL; // 48-bit word, safe 9-digit literal
            ulong operand = 0x0000FFFF0000FFFFUL;

            StoreWord("10", "нтж 2000, стоп");
            StoreData("2000", operand);
            _cpu.SetA(oldA);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(oldA, _cpu.GetY().Value, "Y обязан сохранить A до нтж");
            Assert.AreEqual(oldA ^ operand, _cpu.GetA().Value);
            Assert.AreEqual((uint)RFlags.Log, _cpu.GetR() & (uint)RFlags.Mode);
        }

        [TestMethod]
        [DataRow("и 2000")]   // 011 aax
        [DataRow("или 2000")] // 015 aox
        [DataRow("сбр 2000")] // 020 apx
        [DataRow("рзб 2000")] // 021 aux
        [DataRow("чед 2000")] // 022 acx
        public void LogicalArith_OpcodesClearY(string instruction)
        {
            StoreWord("10", instruction + ", стоп");
            StoreData("2000", 1UL);
            _cpu.SetA(0x200030004UL);
            _cpu.SetY(0x400050006UL);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(0UL, _cpu.GetY().Value, instruction + ": Y обязан обнулиться");
        }

        /// <summary>счмр (031/yta), логический режим: A := Y (ref: a = y).</summary>
        [TestMethod]
        public void Yta_LogicalMode_CopiesYToA()
        {
            ulong rmrValue = 0x0ABCDEF01234UL; // 48 бит

            StoreWord("10", "счмр 2000, стоп");
            StoreData("2000", 1UL);
            _cpu.SetA(0x600070008UL);
            _cpu.SetY(rmrValue);
            _cpu.SetR((ulong)RFlags.Log);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(rmrValue, _cpu.GetA().Value, "в логическом режиме счмр копирует Y в A");
        }

        /// <summary>счмр (031/yta), НЕ логический режим: мантисса (40 бит) берётся из Y,
        /// экспонента A сохраняется (выбран адрес 0100 oct → дельта экспоненты 0),
        /// R остаётся аддитивным.</summary>
        [TestMethod]
        public void Yta_AdditiveMode_TakesYMantissa_KeepsAdditive()
        {
            ulong mantissa40 = 0xFFFF0000FFUL;      // ровно 40 бит
            ulong rmrValue = (1UL << 47) | mantissa40;
            ulong expField = 64UL << 41;            // экспонента 64 (bias) — дельта 0
            ulong expected = expField | mantissa40;

            StoreWord("10", "счмр 100, стоп"); // 0100 oct = 64 dec → aex&077 = 64 → дельта 0
            _cpu.SetA(expField | (1UL << 39));
            _cpu.SetY(rmrValue);
            _cpu.SetR((ulong)RFlags.Add);
            _cpu.SetK(K);

            _cpu.Step();

            Assert.AreEqual(expected, _cpu.GetA().Value, "экспонента A + 40-битная мантисса Y");
            Assert.AreEqual((uint)RFlags.Add, _cpu.GetR() & (uint)RFlags.Mode,
                "в не-логическом режиме счмр НЕ переключает R");
        }

        /// <summary>нед (023/anx): A==0 → Y=0, A:=операнд; A!=0 → Y=высшедшие биты.</summary>
        [TestMethod]
        [DataRow(0UL, true)]      // A==0  → Y обязан быть 0
        [DataRow(0x1234UL, false)] // A!=0 → Y ≠ 0 (вытесненные биты сдвига)
        public void Anx_BranchDependentY(ulong a, bool expectZeroY)
        {
            StoreWord("10", "нед 2000, стоп");
            StoreData("2000", 5UL);
            _cpu.SetA(a);
            _cpu.SetK(K);

            _cpu.Step();

            if (expectZeroY)
                Assert.AreEqual(0UL, _cpu.GetY().Value, "нед при A==0 обнуляет Y");
            else
                Assert.AreNotEqual(0UL, _cpu.GetY().Value, "нед при A!=0 кладёт в Y степень сдвига");
        }

        // ─── R-матрица: инструкция → итоговый режим АЛУ ──────────────────
        // Дополнение таблицы Instruction_SetsExpectedRMode (там уже есть
        // сч/и/нтж/или/счрж/знак/слц): здесь оставшиеся явные SetLogical/
        // SetAdditive/SetMultiplicative-вызовы в InstructionExecutor.
        [TestMethod]
        [DataRow("сл 2000", (int)RFlags.Add)]
        [DataRow("вч 2000", (int)RFlags.Add)]
        [DataRow("вчоб 2000", (int)RFlags.Add)]
        [DataRow("вчаб 2000", (int)RFlags.Add)]
        [DataRow("умн 2000", (int)RFlags.Mult)]
        [DataRow("дел 2000", (int)RFlags.Mult)]
        [DataRow("слп 2000", (int)RFlags.Mult)]
        [DataRow("вчп 2000", (int)RFlags.Mult)]
        [DataRow("слпа 2000", (int)RFlags.Mult)]
        [DataRow("вчпа 2000", (int)RFlags.Mult)]
        [DataRow("сд 2000", (int)RFlags.Log)]
        [DataRow("сда 2000", (int)RFlags.Log)]
        [DataRow("зпм 2000", (int)RFlags.Log)]
        [DataRow("счм", (int)RFlags.Log)]
        [DataRow("счи 2000", (int)RFlags.Log)]
        [DataRow("уим 2000", (int)RFlags.Log)]
        public void Instruction_SetsExpectedRMode_Extended(string instruction, int expectedMode)
        {
            StoreWord("10", instruction + ", стоп");
            // Каноническое плавающее 1.0: валидный делитель для дел, нейтральный операнд для остальных.
            StoreData("2000", Besm6Math.DoubleToBesm6(1.0));
            _cpu.SetA(0UL);
            // Старт в НЕ-ожидаемом режиме: итог обязан измениться именно инструкцией.
            _cpu.SetR((ulong)(RFlags.OvfDisable | RFlags.RoundDisable | RFlags.Mult));
            _cpu.SetK(K);

            _cpu.Step();

            uint mode = _cpu.GetR() & (uint)RFlags.Mode;
            Assert.AreEqual((uint)expectedMode, mode, instruction);
        }
    }
}


