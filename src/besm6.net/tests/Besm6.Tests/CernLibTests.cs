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
        [Timeout(420000)] // wall-clock лимит фикстуры 300 с (CernLibFixture.WallClockLimitMs) + 120 с на boot/артефакты/нагрузку
        [DynamicData(nameof(CernLibData))]
        public void Case_MatchesExpectFile(CernLibCase c)
        {
            CernLibRunResult r = _fx.Run(c);
            EmitProgress(c, r);
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

        // ─────────── Прогресс/Elapsed долгой матрицы (31.08.2026) ───────────
        // Пер-кейс строка в stdout (попадает в лог dotnet test) + heartbeat-файл
        // tests-run/cernlib/progress.txt для живого мониторинга: [bar] done/total,
        // elapsed сессии, ETA. Пишется ПОСЛЕ fx.Run — консоль к этому моменту уже
        // восстановлена фикстурой, так что строка не загрязняет захваченный вывод машины.
        private static readonly object ProgressLock = new object();
        private static int _caseNo;
        private static int? _totalCases;
        private static long _caseMsTotal;
        private static readonly System.Diagnostics.Stopwatch SessionSw = System.Diagnostics.Stopwatch.StartNew();

        private static string ProgressBar(int done, int total)
        {
            const int width = 20;
            int filled = total <= 0 ? width : (int)Math.Round(width * (double)done / total);
            if (filled > width) filled = width;
            if (filled < 0) filled = 0;
            return new string('#', filled) + new string('.', width - filled);
        }

        private static string FormatHms(long ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100}";
        }

        private void EmitProgress(CernLibCase c, CernLibRunResult r)
        {
            lock (ProgressLock)
            {
                int idx = System.Threading.Interlocked.Increment(ref _caseNo);
                if (_totalCases == null)
                {
                    string? filter = Environment.GetEnvironmentVariable(CernLibBatchFilter.EnvVarName);
                    _totalCases = CernLibBatchFilter.Filter(CernLibManifest.ActiveCases, filter).Count();
                }
                int total = _totalCases.Value;
                long msTotal = System.Threading.Interlocked.Add(ref _caseMsTotal, r.ElapsedMs);
                double avg = (double)msTotal / idx;
                string line =
                    $"[{ProgressBar(idx, total)}] {idx}/{total} ({100 * idx / total}%) {c} " +
                    $"-> {r.Classification} [{r.ElapsedMs} ms, {r.Instructions.ToString(System.Globalization.CultureInfo.InvariantCulture)} ins] " +
                    $"| elapsed={FormatHms((long)SessionSw.Elapsed.TotalMilliseconds)} eta={FormatHms((long)(avg * (total - idx)))}";

                Console.WriteLine(line);

                try
                {
                    string path = System.IO.Path.Combine(_fx.ArtifactsDir, "progress.txt");
                    System.IO.File.WriteAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
                }
                catch
                {
                    // Прогресс не критичен для результата теста.
                }
            }
        }
    }
}