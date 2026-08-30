using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Регрессии instrumentation: trace обязан описывать именно исполняемую инструкцию,
    /// а не уже изменённые PC/half после предварительного advance.
    /// </summary>
    [TestClass]
    [TestCategory("Architecture")]
    [TestCategory("Trace")]
    public sealed class ProcessorTraceRegressionTests
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

        private static uint O(string octal) => Convert.ToUInt32(octal, 8);

        [TestMethod]
        public void TraceInstruction_ReportsPreExecutionPcAndExecutedHalf()
        {
            _memory.Write(O("10"), new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), vtm 2(2)")));
            _cpu.SetPc(O("10"));

            var trace = new List<(uint Pc, bool Right, uint Rk, uint Opcode)>();
            _cpu.TraceInstruction = (pc, right, rk, opcode) => trace.Add((pc, right, rk, opcode));

            _cpu.Step();
            _cpu.Step();

            Assert.AreEqual(2, trace.Count);

            Assert.AreEqual(O("10"), trace[0].Pc);
            Assert.IsFalse(trace[0].Right, "Первая инструкция слова должна логироваться как LEFT.");
            Assert.AreEqual((uint)Opcode.Uia, trace[0].Opcode);

            Assert.AreEqual(O("10"), trace[1].Pc,
                "RIGHT half всё ещё принадлежит тому же 48-битному слову.");
            Assert.IsTrue(trace[1].Right, "Вторая инструкция слова должна логироваться как RIGHT.");
            Assert.AreEqual((uint)Opcode.Uia, trace[1].Opcode);
        }

        [TestMethod]
        public void TraceInstruction_RkMatchesActualLeftAndRightHalfWords()
        {
            ulong word = Besm6.Asm.Assembler.Asm("vtm 1(1), vtm 2(2)");
            _memory.Write(O("10"), new Word48(word));
            _cpu.SetPc(O("10"));

            var trace = new List<(uint Rk, bool Right)>();
            _cpu.TraceInstruction = (_, right, rk, _) => trace.Add((rk, right));

            _cpu.Step();
            _cpu.Step();

            uint expectedLeft = (uint)((word >> 24) & 0xFFFFFFUL);
            uint expectedRight = (uint)(word & 0xFFFFFFUL);

            Assert.AreEqual(expectedLeft, trace[0].Rk);
            Assert.IsFalse(trace[0].Right);
            Assert.AreEqual(expectedRight, trace[1].Rk);
            Assert.IsTrue(trace[1].Right);
        }
    }
}
