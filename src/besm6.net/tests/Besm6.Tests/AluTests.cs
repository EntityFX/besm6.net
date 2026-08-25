using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Тесты арифметического устройства (АЛУ) через единственный движок <see cref="Processor"/>.
    /// Восьмеричные литералы — 17 символов (биты 47..0; старшая цифра всегда 0 для 48-бит).
    /// </summary>
    [TestClass]
    public class AluTests
    {
        private LinearMemory _memory;
        private Processor _cpu;

        [TestInitialize]
        public void Setup()
        {
            _memory = new LinearMemory();
            _cpu = new Processor(_memory);
        }

        private sealed class LinearMemory : IMemory
        {
            private readonly Word48[] _words = new Word48[32768];
            public Word48 Read(uint address) => _words[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
            public int Size => 32768;
        }

        private void SetAcc(string oct) => _cpu.SetAcc(FromOctal(oct).Value);
        private Word48 GetAcc() => _cpu.GetAcc();
        private void SetRau(ulong rau) => _cpu.SetRau(rau);

        [TestMethod]
        public void Test_Alu_Add()
        {
            // 1.0 + 1.0 = 2.0
            VerifyAdd("04050000000000000", "04050000000000000", "04110000000000000");
            // 1.0 + (-0.5) = 0.5
            VerifyAdd("04050000000000000", "04030000000000000", "04010000000000000");
            // (-1.0) + (-1.0) = -2.0
            VerifyAdd("04020000000000000", "04020000000000000", "04060000000000000");
            // 1.0 + 0 = 1.0
            VerifyAdd("04050000000000000", "00000000000000000", "04050000000000000");
        }

        [TestMethod]
        public void Test_Alu_Sub()
        {
            // 1.0 - 1.0 = 0
            VerifySub("04050000000000000", "04050000000000000", "00000000000000000");
            // 1.0 - (-1.0) = 2.0
            VerifySub("04050000000000000", "04020000000000000", "04110000000000000");
            // (-1.0) - 1.0 = -2.0
            VerifySub("04020000000000000", "04050000000000000", "04060000000000000");
            // (-1.0) - (-1.0) = 0
            VerifySub("04020000000000000", "04020000000000000", "00000000000000000");
        }

        [TestMethod]
        public void Test_Alu_Multiply()
        {
            // 1.0 * 1.0 = 1.0
            VerifyMul("04050000000000000", "04050000000000000", "04050000000000000");
            // 1.0 * (-1.0) = -1.0
            VerifyMul("04050000000000000", "04020000000000000", "04020000000000000");
            // (-1.0) * 1.0 = -1.0
            VerifyMul("04020000000000000", "04050000000000000", "04020000000000000");
            // (-1.0) * (-1.0) = 1.0
            VerifyMul("04020000000000000", "04020000000000000", "04050000000000000");
            // 1.0 * 0 = 0
            VerifyMul("04050000000000000", "00000000000000000", "00000000000000000");
        }

        [TestMethod]
        public void Test_Alu_Multiply_NoNormalize()
        {
            // RAU=3 отключает нормализацию и округление.
            SetRau(3);
            // 1.0 * 1.0 = 1/4 * 2^2 (денормализованный)
            VerifyMul("04050000000000000", "04050000000000000", "04104000000000000");
            // 1.0 * (-1.0) = -1/2 * 2^1
            VerifyMul("04050000000000000", "04020000000000000", "04070000000000000");
            // (-1.0) * 1.0 = -1/2 * 2^1
            VerifyMul("04020000000000000", "04050000000000000", "04070000000000000");
            // (-1.0) * (-1.0) = 1.0
            VerifyMul("04020000000000000", "04020000000000000", "04050000000000000");
        }

        [TestMethod]
        public void Test_Alu_Divide()
        {
            // 1.0 / 1.0 = 1.0
            VerifyDiv("04050000000000000", "04050000000000000", "04050000000000000");
            // -1.0 / -1.0 = 1.0
            VerifyDiv("04020000000000000", "04020000000000000", "04050000000000000");
            // 1.0 / -1.0 = -1.0
            VerifyDiv("04050000000000000", "04020000000000000", "04020000000000000");
            // -1.0 / 1.0 = -1.0
            VerifyDiv("04020000000000000", "04050000000000000", "04020000000000000");
            // 1.0 / 0.5 = 2.0
            VerifyDiv("04050000000000000", "04010000000000000", "04110000000000000");
            // 0.5 / 1.0 = 0.5
            VerifyDiv("04010000000000000", "04050000000000000", "04010000000000000");
        }

        [TestMethod]
        public void Test_Alu_Divide_NoNormalize()
        {
            SetRau(3);
            // 1.0 / 1.0 = 1.0
            VerifyDiv("04050000000000000", "04050000000000000", "04050000000000000");
            // -1.0 / -1.0 = 1.0
            VerifyDiv("04020000000000000", "04020000000000000", "04050000000000000");
            // 1.0 / -1.0 = -1.0 (денормализованный)
            VerifyDiv("04050000000000000", "04020000000000000", "04070000000000000");
            // -1.0 / 1.0 = -1.0
            VerifyDiv("04020000000000000", "04050000000000000", "04020000000000000");
        }

        [TestMethod]
        public void Test_Alu_DivideByZero()
        {
            try
            {
                SetAcc("04050000000000000");
                _cpu.ArithDivide(new Word48(0));
                Assert.Fail("Expected ProcessorException was not thrown");
            }
            catch (ProcessorException)
            {
                // Success
            }
        }

        [TestMethod]
        public void Test_Alu_ChangeSign()
        {
            // 1.0 -> -1.0
            VerifyChangeSign("04050000000000000", "04020000000000000");
            // -1.0 -> 1.0
            VerifyChangeSign("04020000000000000", "04050000000000000");
            // 0 -> 0
            VerifyChangeSign("00000000000000000", "00000000000000000");
        }

        [TestMethod]
        public void Test_Alu_Shift()
        {
            // Shift right 1 bit: 0x10 (oct) -> 0x4 (oct) в младших разрядах слова.
            VerifyShift("00000000000000010", 1, "00000000000000004");
            // Shift left 1 bit: 0x4 -> 0x10
            VerifyShift("00000000000000004", -1, "00000000000000010");
        }

        private void VerifyAdd(string octA, string octX, string octExpected)
        {
            SetAcc(octA);
            _cpu.ArithAdd(FromOctal(octX), false, false);
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"Add failed: {octA} + {octX} = {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private void VerifySub(string octA, string octX, string octExpected)
        {
            SetAcc(octA);
            _cpu.ArithAdd(FromOctal(octX), false, true);
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"Sub failed: {octA} - {octX} = {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private void VerifyMul(string octA, string octX, string octExpected)
        {
            SetAcc(octA);
            _cpu.ArithMultiply(FromOctal(octX));
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"Multiply failed: {octA} * {octX} = {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private void VerifyDiv(string octA, string octX, string octExpected)
        {
            SetAcc(octA);
            _cpu.ArithDivide(FromOctal(octX));
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"Divide failed: {octA} / {octX} = {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private void VerifyChangeSign(string octA, string octExpected)
        {
            SetAcc(octA);
            // Смена знака аккумулятора (команда «знак» с отрицательным операндом): negateAcc=true.
            _cpu.ArithChangeSign(true);
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"ChangeSign failed: {octA} -> {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private void VerifyShift(string octA, int shift, string octExpected)
        {
            SetAcc(octA);
            _cpu.ArithShift(shift);
            Assert.AreEqual(FromOctal(octExpected), GetAcc(), $"Shift failed: {octA} by {shift} = {ToOctal(GetAcc().Value)} (Expected: {octExpected})");
        }

        private static Word48 FromOctal(string oct)
        {
            ulong val = 0;
            foreach (char c in oct)
                val = (val << 3) | (ulong)(c - '0');
            return new Word48(val);
        }

        private static string ToOctal(ulong val)
        {
            char[] digits = new char[16];
            for (int i = 15; i >= 0; i--)
            {
                digits[i] = (char)((val & 7) + '0');
                val >>= 3;
            }
            return new string(digits);
        }
    }
}
