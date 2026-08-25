using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Порт cernlib_test.cpp (397 активных тестов C++). Фаза 0 — маяки:
    ///  a400 (lib1): арифметика слов, форматирование вывода, CERN-линковка;
    ///  z005 (lib2): DATE*/DATEZB/IDATZA (детерминизм даты) — самый простой тест.
    /// w303 (lib2) в C++ закомментирован (вечный цикл) — здесь тоже [Ignore].
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
        [DataRow(1, "a400")]
        [DataRow(2, "z005")]
        public void Beacon_MatchesExpectFile(int lib, string name)
        {
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

        [TestMethod]
        [Ignore("В C++ cernlib_test.cpp закомментирован: вечный цикл (не портится до изменения expect)")]
        public void W303_LoopsForever()
        {
            _fx.RunAndCompare(2, "w303", out _, out _, out _);
        }
    }
}