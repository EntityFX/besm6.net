using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Регрессии instrumentation: trace обязан описывать именно исполняемую инструкцию,
    /// а не уже изменённые PC/half после предварительного advance.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
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

        [TestMethod]
        public void CanonicalTrace_WritesOneHeaderAndOneTsvRowPerInstruction()
        {
            const string headerPrefix = "seq\tpc\thalf\traw48\trk24\topcode\treg\taddr\t";
            string path = Path.Combine(Path.GetTempPath(), $"besm6-canon-{Guid.NewGuid():N}.tsv");
            string? saved = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");

            try
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", path);
                _memory.Write(O("10"), new Word48(Besm6.Asm.Assembler.Asm("vtm 1(1), vtm 2(2)")));
                _cpu.SetPc(O("10"));

                _cpu.Step();
                typeof(Processor).GetMethod("CanonFlush", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_cpu, null);
                ((IDisposable)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu)!).Dispose();

                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length,
                    "Canonical TSV must contain one physical header line and one physical row per executed instruction.");
                StringAssert.StartsWith(lines[0], headerPrefix);

                string[] columns = lines[0].Split('\t');
                string[] values = lines[1].Split('\t');
                Assert.AreEqual(columns.Length, values.Length, "Every canonical row must match the header schema.");

                var row = columns.Zip(values, (column, value) => (column, value))
                    .ToDictionary(item => item.column, item => item.value);
                Assert.AreEqual("8", row["pc"]);
                Assert.AreEqual("L", row["half"]);
                Assert.AreEqual("1", row["pc_a"] == "8" && row["half_a"] == "R" ? "1" : "0",
                    "POST control state must describe the next half after executing LEFT.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", saved);
                var writer = (IDisposable?)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu);
                writer?.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CanonicalTrace_IncludesStopInstructionPostState()
        {
            string path = Path.Combine(Path.GetTempPath(), $"besm6-canon-stop-{Guid.NewGuid():N}.tsv");
            string? saved = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");

            try
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", path);
                _memory.Write(O("10"), new Word48(Besm6.Asm.Assembler.Asm("stop, vtm 2(2)")));
                _cpu.SetPc(O("10"));

                Assert.IsTrue(_cpu.Step());
                ((IDisposable)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu)!).Dispose();

                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length, "STOP is an executed machine instruction and must have a complete PRE/POST row.");
                string[] columns = lines[0].Split('\t');
                string[] values = lines[1].Split('\t');
                var row = columns.Zip(values, (column, value) => (column, value))
                    .ToDictionary(item => item.column, item => item.value);
                Assert.AreEqual(((uint)Opcode.Stop).ToString(), row["opcode"]);
                Assert.AreEqual("8", row["pc_a"]);
                Assert.AreEqual("R", row["half_a"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", saved);
                var writer = (IDisposable?)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu);
                writer?.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CanonicalTrace_IncludesThrowingExtracodePostState()
        {
            string path = Path.Combine(Path.GetTempPath(), $"besm6-canon-e74-{Guid.NewGuid():N}.tsv");
            string? saved = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");

            try
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", path);
                _memory.Write(O("10"), new Word48(Besm6.Asm.Assembler.Asm("*74, vtm 2(2)")));
                _cpu.SetPc(O("10"));
                _cpu.ExtracodeHandler = (_, _) => throw new ProcessorException("");

                ProcessorException? exception = null;
                try
                {
                    _cpu.Step();
                }
                catch (ProcessorException caught)
                {
                    exception = caught;
                }
                Assert.IsNotNull(exception, "E74 handler exception must escape Processor.Step().");
                Assert.AreEqual(string.Empty, exception.Message);
                ((IDisposable)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu)!).Dispose();

                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length,
                    "A terminal extracode is executed and must have a complete PRE/POST row before its exception escapes.");
                string[] columns = lines[0].Split('\t');
                string[] values = lines[1].Split('\t');
                Assert.AreEqual(columns.Length, values.Length, "Terminal extracode row must match the canonical schema.");
                var row = columns.Zip(values, (column, value) => (column, value))
                    .ToDictionary(item => item.column, item => item.value);
                Assert.AreEqual(Convert.ToInt32("74", 8).ToString(), row["opcode"]);
                Assert.AreEqual("9", row["pc_a"]);
                Assert.AreEqual("L", row["half_a"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", saved);
                var writer = (IDisposable?)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu);
                writer?.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CanonicalTrace_ThrowingExtracodeCanBeFinalizedAfterIntercept()
        {
            string path = Path.Combine(Path.GetTempPath(), $"besm6-canon-intercept-{Guid.NewGuid():N}.tsv");
            string? saved = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");

            try
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", path);
                _memory.Write(O("10"), new Word48(Besm6.Asm.Assembler.Asm("*50, stop")));
                _cpu.SetPc(O("10"));
                _cpu.InterceptCount = 1;
                _cpu.InterceptAddr = O("20");
                _cpu.ExtracodeHandler = (_, _) => throw new ProcessorException("Division by zero");

                ProcessorException? exception = null;
                try
                {
                    _cpu.Step();
                }
                catch (ProcessorException caught)
                {
                    exception = caught;
                }
                Assert.IsNotNull(exception);
                _cpu.StackCorrection();
                Assert.IsTrue(_cpu.Intercept(exception.Message));
                typeof(Processor).GetMethod("CanonPost", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_cpu, new object[] { _cpu.GetPc(), _cpu.RightInstruction });
                ((IDisposable)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu)!).Dispose();

                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length);
                string[] columns = lines[0].Split('\t');
                string[] values = lines[1].Split('\t');
                var row = columns.Zip(values, (column, value) => (column, value))
                    .ToDictionary(item => item.column, item => item.value);
                Assert.AreEqual(O("20").ToString(), row["pc_a"]);
                Assert.AreEqual("L", row["half_a"]);
                Assert.AreEqual("0", row["icnt_a"],
                    "POST must include the consumed intercept rather than the pre-intercept exception state.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", saved);
                var writer = (IDisposable?)typeof(Processor)
                    .GetField("_canonTrace", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(_cpu);
                writer?.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
