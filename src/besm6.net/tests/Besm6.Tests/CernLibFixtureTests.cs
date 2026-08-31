using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Изолированные тесты CERNlib-раннера (SuperPlan Task A2):
    /// нормализация строк, раздельные артефакты lib/name, восстановление консоли
    /// после исключения лоадера, классификация лимитов (instruction / wall-clock),
    /// артефакты при неудаче, batch-разбиение.
    /// Все тесты герметичны: работают на синтетическом корне с ref/tests и не
    /// зависят от git-ignored ref/ в репозитории.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class CernLibFixtureTests
    {
        private const string LoopingSource =
            "      PROGRAM T1\r\n" +
            "  100 CONTINUE\r\n" +
            "      GO TO 100\r\n" +
            "      END\r\n";

        // -------------------------------------------------------------------
        // Нормализация: переписываются ТОЛЬКО переводы строк
        // -------------------------------------------------------------------
        [TestMethod]
        public void NormalizeLineEndings_OnlyNewlines_WhitespaceAndControlCharsPreserved()
        {
            // Табы, пробелы, пустые строки, NUL, form feed — сохраняются; \r\n и \r → \n.
            Assert.AreEqual("a\tb\nc\0d\ne\f", CernLibFixture.NormalizeLineEndings("a\tb\r\nc\0d\re\f"));
            Assert.AreEqual("x\n\ny", CernLibFixture.NormalizeLineEndings("x\r\n\r\ny"));
            Assert.AreEqual("  z", CernLibFixture.NormalizeLineEndings("  z"));
            Assert.AreEqual(string.Empty, CernLibFixture.NormalizeLineEndings(string.Empty));
            Assert.AreEqual(null, CernLibFixture.NormalizeLineEndings(null!));
        }

        // -------------------------------------------------------------------
        // Раздельные каталоги артефактов: lib1/x и lib2/x не конфликтуют
        // -------------------------------------------------------------------
        [TestMethod]
        public void ArtifactDir_SeparatePerLibraryAndName()
        {
            using var tr = new TempRoot();
            var fx = new CernLibFixture(tr.Path);
            string lib1x = fx.ArtifactDir(1, "x");
            string lib2x = fx.ArtifactDir(2, "x");
            string lib1x1 = fx.ArtifactDir(1, "x1");

            Assert.IsTrue(lib1x.EndsWith(Path.Combine("cernlib", "lib1", "x"), StringComparison.Ordinal), lib1x);
            Assert.IsTrue(lib2x.EndsWith(Path.Combine("cernlib", "lib2", "x"), StringComparison.Ordinal), lib2x);
            Assert.AreNotEqual(lib1x, lib2x, "одинаковые имена в lib1/lib2 не должны перезаписывать друг друга");
            Assert.AreNotEqual(lib1x, lib1x1, "соседние имена в одной библиотеке не должны пересекаться");
        }

        // -------------------------------------------------------------------
        // Классификации
        // -------------------------------------------------------------------
        [TestMethod]
        public void Run_MissingSource_DistinctClassification_NoArtifacts()
        {
            using var tr = new TempRoot(lib1Source: LoopingSource, lib1Expect: "x\r\n");
            var fx = new CernLibFixture(tr.Path);
            fx.Setup();
            try
            {
                CernLibRunResult r = fx.Run(1, "no_such_case");
                Assert.AreEqual(CernLibClassification.MissingSource, r.Classification);
                Assert.IsFalse(r.Success);
                Assert.IsNull(r.ActualText);
                Assert.IsNull(r.ActualPath);
                StringAssert.Contains(r.LoaderMessage ?? string.Empty, "нет исходника/expect");
            }
            finally { fx.Cleanup(); }
        }

        [TestMethod]
        public void Run_LoaderException_ConsoleRestored_AndLoaderErrorClassification()
        {
            using var tr = new TempRoot(lib1Source: "C synthetic\r\n", lib1Expect: "x\r\n");
            var fx = new CernLibFixture(tr.Path);
            TextWriter original = Console.Out;
            fx.Setup();
            try
            {
                fx.TestHookBeforeRunScript = () => throw new InvalidOperationException("synthetic loader failure");
                CernLibRunResult r = fx.Run(1, "t1");
                Assert.AreEqual(CernLibClassification.LoaderError, r.Classification);
                StringAssert.Contains(r.LoaderMessage ?? string.Empty, "synthetic loader failure");
                Assert.AreSame(original, Console.Out, "редирекция консоли должна восстановиться после исключения лоадера");
            }
            finally { fx.Cleanup(); }
        }

        [TestMethod]
        public void Run_InstructionLimitExceeded_LimitExceededClassification_WithArtifacts()
        {
            using var tr = new TempRoot(lib1Source: LoopingSource, lib1Expect: "x\r\n");
            var fx = new CernLibFixture(tr.Path)
            {
                InstructionLimit = 200,
                WallClockLimitMs = 60_000,
            };
            fx.Setup();
            try
            {
                CernLibRunResult r = fx.Run(1, "t1");
                Assert.AreEqual(CernLibClassification.LimitExceeded, r.Classification,
                    "превышение instruction-лимита — отдельный класс, не OutputMismatch: " + r.LoaderMessage);
                Assert.IsTrue(r.InstructionLimitExceeded);
                Assert.IsFalse(r.WallClockLimitExceeded);
                Assert.IsTrue(r.Instructions > 0);

                string dir = fx.ArtifactDir(1, "t1");
                Assert.IsTrue(File.Exists(Path.Combine(dir, "actual.txt")), "actual.txt обязан быть создан");
                Assert.IsTrue(File.Exists(Path.Combine(dir, "diff.txt")), "diff.txt обязан быть создан");
                string runJson = File.ReadAllText(Path.Combine(dir, "run.json"));
                StringAssert.Contains(runJson, "LimitExceeded");
                StringAssert.Contains(runJson, "\"instructions\"");
                StringAssert.Contains(runJson, "lib1/t1");
            }
            finally { fx.Cleanup(); }
        }

        [TestMethod]
        public void Run_WallClockLimitExceeded_LimitExceededClassification()
        {
            using var tr = new TempRoot(lib1Source: LoopingSource, lib1Expect: "x\r\n");
            var fx = new CernLibFixture(tr.Path)
            {
                InstructionLimit = 10_000_000,
                WallClockLimitMs = 1, // 10M инструкций исполняется дольше 1 мс
            };
            fx.Setup();
            try
            {
                CernLibRunResult r = fx.Run(1, "t1");
                Assert.AreEqual(CernLibClassification.LimitExceeded, r.Classification, r.LoaderMessage);
                Assert.IsTrue(r.WallClockLimitExceeded);
            }
            finally { fx.Cleanup(); }
        }

        // -------------------------------------------------------------------
        // Batch-разбиение (BESM6_CERN_BATCH): детерминированно, без изменения manifest
        // -------------------------------------------------------------------
        [TestMethod]
        public void BatchFilter_DefaultIsAllAndPreservesOrder()
        {
            var all = CernLibManifest.ActiveCases.ToList();
            CollectionAssert.AreEqual(all, CernLibBatchFilter.Filter(all, null).ToList());
            CollectionAssert.AreEqual(all, CernLibBatchFilter.Filter(all, "all").ToList());
        }

        [TestMethod]
        public void BatchFilter_Libraries_CoverAllWithoutOverlap()
        {
            var all = CernLibManifest.ActiveCases;
            var lib1 = CernLibBatchFilter.Filter(all, "lib1").ToList();
            var lib2 = CernLibBatchFilter.Filter(all, "lib2").ToList();
            Assert.IsTrue(lib1.All(c => c.Library == 1));
            Assert.IsTrue(lib2.All(c => c.Library == 2));
            Assert.AreEqual(all.Count, lib1.Count + lib2.Count);
        }

        [TestMethod]
        public void BatchFilter_Range_IsPrefixOfLibraryInManifestOrder()
        {
            var all = CernLibManifest.ActiveCases;
            var expected = all.Where(c => c.Library == 2).Take(5).ToList();
            CollectionAssert.AreEqual(expected, CernLibBatchFilter.Filter(all, "lib2:0-4").ToList());
        }

        [TestMethod]
        public void BatchFilter_Names_MatchesBothLibraries()
        {
            var all = CernLibManifest.ActiveCases;
            var r = CernLibBatchFilter.Filter(all, "names:d302,j531a").ToList();
            CollectionAssert.AreEquivalent(
                new[] { new CernLibCase(1, "d302"), new CernLibCase(2, "j531a") }, r);
        }

        [TestMethod]
        public void BatchFilter_UnknownToken_FailsFast()
        {
            Assert.Throws<InvalidDataException>(() =>
                CernLibBatchFilter.Filter(CernLibManifest.ActiveCases, "lib3"));
        }

        [TestMethod]
        public void BatchFilter_BadRange_FailsFast()
        {
            Assert.Throws<InvalidDataException>(() =>
                CernLibBatchFilter.Filter(CernLibManifest.ActiveCases, "lib1:5-2"));
        }

        // -------------------------------------------------------------------
        // Синтетический корень: ref/tests/lib1/{t1.f,expect_t1.txt}
        // -------------------------------------------------------------------
        private sealed class TempRoot : IDisposable
        {
            public string Path { get; }

            public TempRoot(string? lib1Source = null, string? lib1Expect = null)
            {
                Path = System.IO.Directory.CreateTempSubdirectory("besm6_cernlib_fixture_").FullName;
                string lib1 = System.IO.Path.Combine(Path, "ref", "tests", "lib1");
                System.IO.Directory.CreateDirectory(lib1);
                if (lib1Source != null)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(lib1, "t1.f"), lib1Source);
                if (lib1Expect != null)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(lib1, "expect_t1.txt"), lib1Expect);
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(Path, true); }
                catch { /* best effort: временный каталог */ }
            }
        }
    }
}