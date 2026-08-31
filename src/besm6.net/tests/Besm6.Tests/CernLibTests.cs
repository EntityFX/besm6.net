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
    /// Batch-разбиение: env BESM6_CERN_BATCH (CernLibBatchFilter).
    /// Артефакты случаев: tests-run/cernlib/lib{N}/{name}/. См. plans/SuperPlan.md, Task A1–A3.
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
            CernLibRunResult r = _fx.Run(c);
            if (r.Success) return;

            var msg = new System.Text.StringBuilder();
            msg.Append(c.ToString()).AppendLine(" FAILED [").Append(r.Classification).AppendLine("]");
            if (r.LoaderMessage != null)
                msg.AppendLine(r.LoaderMessage);
            msg.Append("instructions: ").Append(r.Instructions).Append(", elapsed: ").Append(r.ElapsedMs).Append("ms");
            if (r.InstructionLimitExceeded) msg.Append(" (превышен instruction-лимит)");
            if (r.WallClockLimitExceeded) msg.Append(" (превышен wall-clock лимит)");
            if (r.FirstDiffPosition.HasValue)
            {
                msg.AppendLine();
                msg.AppendLine("Расхождение в символе " + r.FirstDiffPosition.Value + ":");
                msg.Append("  expected[...]: ").AppendLine(CernLibFixture.Quote(r.FirstDiffExpected!));
                msg.Append("  actual  [...]: ").AppendLine(CernLibFixture.Quote(r.FirstDiffActual!));
            }
            if (r.DiffPath != null)
                msg.Append("Артефакты: ").AppendLine(r.DiffPath).Append("run.json: ").AppendLine(r.RunInfoPath);
            Assert.Fail(msg.ToString());
        }

        /// <summary>
        /// Данные теории: активные случаи из коммиченного manifest, с опциональным
        /// детерминированным batch-разбиением (env BESM6_CERN_BATCH, см. CernLibBatchFilter).
        /// </summary>
        public static IEnumerable<object[]> CernLibData()
        {
            string? filter = Environment.GetEnvironmentVariable(CernLibBatchFilter.EnvVarName);
            foreach (var c in CernLibBatchFilter.Filter(CernLibManifest.ActiveCases, filter))
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