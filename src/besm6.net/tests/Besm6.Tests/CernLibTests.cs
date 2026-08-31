using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// CERNlib-совместимость: каждый активный случай из CernLibManifest (397:
    /// 183 lib1 + 214 lib2) компилируется FORTRAN-компилятором MONSYS, линкуется
    /// с CERN-библиотекой, исполняется, и stdout строго сравнивается с
    /// ref/tests/lib{N}/expect_{name}.txt.
    ///
    /// Beacon-случаи (lib1/a400, lib2/z005) входят в общую матрицу.
    /// w303 — отдельное эталонное исключение ([Ignore], вечный цикл в эталоне).
    /// См. plans/SuperPlan.md, Task A1–A3.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class CernLibTests
    {
        private readonly CernLibFixture _fx = new CernLibFixture();

        [TestInitialize]
        public void Init() => _fx.Setup();

        [TestCleanup]
        public void Cleanup() => _fx.Cleanup();

        [TestMethod]
        [Timeout(300000)]
        [DynamicData(nameof(CernLibData))]
        public void Case_MatchesExpectFile(CernLibCase c)
        {
            int lib = c.Library;
            string name = c.Name;
            bool ok = _fx.RunAndCompare(lib, name, out string actual, out string expect, out string diag);
            if (ok) return;

            var msg = new System.Text.StringBuilder();
            msg.Append("CERN lib").Append(lib).Append('/').Append(name).AppendLine(" FAILED. ");
            if (diag != null) msg.Append(diag);

            if (actual == null || expect == null)
            {
                Assert.Fail(msg.ToString());
            }

            // Первая точка расхождения (для быстрого ориентирования).
            int p = 0;
            int lim = Math.Min(actual.Length, expect.Length);
            while (p < lim && actual[p] == expect[p]) p++;
            int from = Math.Max(0, p - 60);
            int show = 160;
            msg.AppendLine("Расхождение в символе " + p + " из " + actual.Length + " / " + expect.Length + ":");
            msg.Append("  expected[...]: ").AppendLine(CernLibFixture.Quote(expect.Substring(from, Math.Min(show, expect.Length - from))));
            msg.Append("  actual  [...]: ").AppendLine(CernLibFixture.Quote(actual.Substring(from, Math.Min(show, actual.Length - from))));
            msg.Append("Артефакты: tests-run/cernlib/{actual,diff}_").Append(name).AppendLine(".txt");
            Assert.Fail(msg.ToString());
        }

        /// <summary>Данные теории: все 397 активных случаев из коммиченного manifest.</summary>
        public static IEnumerable<object[]> CernLibData()
        {
            foreach (var c in CernLibManifest.ActiveCases)
                yield return new object[] { c };
        }

        [TestMethod]
        public void GenTrace()
        {
            if (System.Environment.GetEnvironmentVariable("BESM6_TRACE") == null)
                return; // самм-оф: без env-переменной просто пропускаем
            string dir = _fx.ArtifactsDir;
            System.IO.Directory.CreateDirectory(dir);
            string tracePath = System.IO.Path.Combine(dir, "csh_a400_trace.txt");
            var result = _fx.GenerateTrace(1, "a400", tracePath);
            Console.WriteLine("Trace written: " + tracePath + " | result=" + result + " instr=" + result.Instructions);
        }

        [TestMethod]
        [Ignore("В C++ cernlib_test.cpp закомментирован: вечный цикл (не портится до изменения expect)")]
        public void W303_LoopsForever()

        {
            _fx.RunAndCompare(2, "w303", out _, out _, out _);
        }
    }
}