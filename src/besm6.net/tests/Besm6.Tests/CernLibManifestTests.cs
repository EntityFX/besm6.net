using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Task A1 (plans/SuperPlan.md): зафиксированная воспроизводимая CERN-матрица.
    /// Manifest — единственный источник списка активных тестов для CernLibTests.
    /// </summary>
    [TestClass]
    public class CernLibManifestTests
    {
        [TestMethod]
        public void ActiveCases_ContainsExactly397()
        {
            var cases = CernLibManifest.ActiveCases;
            Assert.AreEqual(397, cases.Count);

            int lib1 = 0, lib2 = 0;
            foreach (var c in cases)
            {
                if (c.Library == 1) lib1++;
                else if (c.Library == 2) lib2++;
                else Assert.Fail("Недопустимая библиотека: " + c);
            }
            Assert.AreEqual(183, lib1, "lib1 active");
            Assert.AreEqual(214, lib2, "lib2 active");
        }

        [TestMethod]
        public void ActiveCases_PairsAreUnique()
        {
            var seen = new HashSet<(int, string)>();
            foreach (var c in CernLibManifest.ActiveCases)
            {
                bool added = seen.Add((c.Library, c.Name));
                Assert.IsTrue(added, "Дубликат случая: " + c);
            }
        }

        [TestMethod]
        public void ActiveCases_NamesAreValid()
        {
            var re = new Regex("^[a-z0-9][a-z0-9._-]*$");
            foreach (var c in CernLibManifest.ActiveCases)
            {
                Assert.IsTrue(c.Library == 1 || c.Library == 2, "Библиотека вне {1,2}: " + c);
                Assert.IsTrue(re.IsMatch(c.Name), "Имя не похоже на имя CERNlib-теста: " + c);
            }
        }

        [TestMethod]
        public void ActiveCases_ContainsBeacons()
        {
            var cases = CernLibManifest.ActiveCases;
            Assert.IsTrue(cases.Contains(new CernLibCase(1, "a400")), "beacon lib1/a400 отсутствует");
            Assert.IsTrue(cases.Contains(new CernLibCase(2, "z005")), "beacon lib2/z005 отсутствует");
        }

        [TestMethod]
        public void ActiveCases_MatchesReferenceCpp_WhenReferencePresent()
        {
            // Guard: если эталон доступен (developer checkout с ref/), manifest обязан
            // совпадать с активными вызовами test_cernlib в cernlib_test.cpp.
            // На чистом checkout ref/ отсутствует — manifest самодостаточен, тест проходит
            // по первому условию.
            string root = FindRepoRoot();
            if (root == null)
                return;

            // Каталог эталонных тестов: ref/tests (layout dubna) или ref/dubna/tests (layout dubna-subdir).
            string testsDir = Path.Combine(root, "ref", "tests");
            if (!Directory.Exists(testsDir))
                testsDir = Path.Combine(root, "ref", "dubna", "tests");
            string cpp = Path.Combine(testsDir, "cernlib_test.cpp");
            if (!File.Exists(cpp))
                return;

            var fromCpp = new HashSet<(int, string)>();
            foreach (string line in File.ReadLines(cpp))
            {
                if (line.TrimStart().StartsWith("//"))
                    continue;
                var m = Regex.Match(line, @"^\s*test_cernlib\(\s*([12])\s*,\s*""([^""]+)""");
                if (m.Success)
                    fromCpp.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value));
            }

            var fromManifest = new HashSet<(int, string)>();
            foreach (var c in CernLibManifest.ActiveCases)
                fromManifest.Add((c.Library, c.Name));

            Assert.AreEqual(fromCpp.Count, fromManifest.Count,
                "Число активных тестов в эталоне (" + fromCpp.Count + ") не совпадает с manifest (" +
                fromManifest.Count + "). Обновите manifest: pwsh -File plans/_count_cernlib.ps1 -OutJson ...");

            foreach (var k in fromCpp)
            {
                Assert.IsTrue(fromManifest.Contains(k), "Случай из эталона отсутствует в manifest: lib" + k.Item1 + "/" + k.Item2);
            }
            foreach (var k in fromManifest)
            {
                Assert.IsTrue(fromCpp.Contains(k), "Случай в manifest отсутствует в эталоне: lib" + k.Item1 + "/" + k.Item2);
            }

            // Наличие исходника и expect-файла для каждого активного случая.
            var missing = new List<string>();
            foreach (var k in fromManifest)
            {
                string dir = Path.Combine(testsDir, "lib" + k.Item1);
                if (!File.Exists(Path.Combine(dir, k.Item2 + ".f")))
                    missing.Add("lib" + k.Item1 + "/" + k.Item2 + ".f");
                if (!File.Exists(Path.Combine(dir, "expect_" + k.Item2 + ".txt")))
                    missing.Add("lib" + k.Item1 + "/expect_" + k.Item2 + ".txt");
            }
            Assert.AreEqual(0, missing.Count, "Отсутствуют файлы тестов: " + string.Join(", ", missing));
        }

        /// <summary>Поиск корня репозитория от CWD вверх (ref/tests или ref/dubna/tests).</summary>
        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "ref", "tests")) ||
                    Directory.Exists(Path.Combine(dir.FullName, "ref", "dubna", "tests")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}