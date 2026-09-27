using System;
using System.IO;
using System.Text;
using Besm6.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Регрессия: --dump-mem-at и --profile подписываются на один и тот же хук
    /// <c>Processor.InstructionTrace</c>. Раньше --profile присваивал хук вторым и
    /// затирал наблюдатель дампа, поэтому снимок работающей ОС молча пропадал и
    /// на выходе оставался дамп памяти ПОСЛЕ прогона, где монитор уже стёр свой
    /// образ (в зоне ядра — мусор вместо кода).
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class MemoryDumpTraceHookTests
    {
        private const int KernelStart = 0x7C00;   // 0o76000 — начало ядра MONSYS в снимке
        private const int KernelEnd = 0x7F70;     // 0o77560

        [TestMethod]
        public void Profile_DoesNotSuppressInRunMemorySnapshot()
        {
            string? job = FindFileInParentDirs("examples", "copy.dub");
            if (job == null)
            {
                Assert.Inconclusive("File examples/copy.dub not found");
                return;
            }

            string plain = Path.Combine(Path.GetTempPath(), $"os_plain_{Guid.NewGuid():N}.img");
            string profiled = Path.Combine(Path.GetTempPath(), $"os_prof_{Guid.NewGuid():N}.img");
            try
            {
                int rc1 = Run(job, plain, profile: false);
                int rc2 = Run(job, profiled, profile: true);

                Assert.AreEqual(0, rc1, "прогон без --profile");
                Assert.AreEqual(0, rc2, "прогон с --profile");

                Assert.IsTrue(File.Exists(plain), "нет дампа без --profile");
                Assert.IsTrue(File.Exists(profiled), "нет дампа с --profile");

                CollectionAssert.AreEqual(File.ReadAllBytes(plain), File.ReadAllBytes(profiled),
                    "--profile изменил снимок: хук дампа был затёрт наблюдателем профайлера");

                // Снимок сделан ВО ВРЕМЯ исполнения ⇒ в зоне ядра лежит код монитора.
                Assert.IsTrue(KernelNonZeroWords(profiled) > 0,
                    "в зоне ядра нет ни одного ненулевого слова — это дамп после прогона, а не снимок");
            }
            finally
            {
                foreach (string p in new[] { plain, profiled })
                    if (File.Exists(p)) File.Delete(p);
            }
        }

        [TestMethod]
        public void Profile_AndMemorySnapshot_RunTogether()
        {
            string? job = FindFileInParentDirs("examples", "copy.dub");
            if (job == null)
            {
                Assert.Inconclusive("File examples/copy.dub not found");
                return;
            }

            string dump = Path.Combine(Path.GetTempPath(), $"os_both_{Guid.NewGuid():N}.img");
            try
            {
                var sb = new StringBuilder();
                int rc = CliApplication.Run(new[]
                {
                    "run", job, "--limit", "400000",
                    "--profile", "--dump-mem", dump, "--dump-mem-at", "300000"
                }, new StringWriter(sb), new StringWriter());

                Assert.AreEqual(0, rc, sb.ToString());
                StringAssert.Contains(sb.ToString(), "%",
                    "таблица профиля опкодов не выведена — хук профайлера потерян");
            }
            finally
            {
                if (File.Exists(dump)) File.Delete(dump);
            }
        }

        private static int Run(string job, string dump, bool profile)
        {
            var args = new System.Collections.Generic.List<string>
            {
                "run", job, "--limit", "400000", "--dump-mem", dump, "--dump-mem-at", "300000"
            };
            if (profile) args.Add("--profile");

            var sb = new StringBuilder();
            int rc = CliApplication.Run(args.ToArray(), new StringWriter(sb), new StringWriter(sb));
            if (rc != 0) Console.WriteLine(sb.ToString());
            return rc;
        }

        private static int KernelNonZeroWords(string dump)
        {
            byte[] bytes = File.ReadAllBytes(dump);
            int n = 0;
            for (int a = KernelStart; a < KernelEnd && (a + 1) * 6 <= bytes.Length; a++)
            {
                bool nonZero = false;
                for (int b = 0; b < 6; b++)
                    if (bytes[a * 6 + b] != 0) { nonZero = true; break; }
                if (nonZero) n++;
            }
            return n;
        }

        private static string? FindFileInParentDirs(string relativePath, string fileName)
        {
            string? currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null)
            {
                string testPath = Path.Combine(currentDir, relativePath, fileName);
                if (File.Exists(testPath))
                    return testPath;
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            return null;
        }
    }
}