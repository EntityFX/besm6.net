using System;
using System.IO;
using System.Linq;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class RuntimeAssetsTests
    {
        private static string Temp()
        {
            string root = Path.Combine(Path.GetTempPath(), "besm6_ra_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string? FindDevTapesDir()
        {
            foreach (var s in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                if (string.IsNullOrEmpty(s)) continue;
                for (var dir = new DirectoryInfo(s); dir != null; dir = dir.Parent)
                {
                    foreach (var cand in new[]
                    {
                        Path.Combine(dir.FullName, "tapes"),
                        Path.Combine(dir.FullName, "ref", "tapes"),
                        Path.Combine(dir.FullName, "ref", "dubna", "tapes"),
                    })
                    {
                        if (File.Exists(Path.Combine(cand, "monsys.9"))) return cand;
                    }
                }
            }
            return null;
        }

        [TestMethod]
        public void Catalog_FiveDistinctAssets_WithShaAndProvenance()
        {
            Assert.AreEqual(5, RuntimeAssets.Catalog.Count);
            foreach (var a in RuntimeAssets.Catalog)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.Name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.Sha256), "sha missing for " + a.Name);
                Assert.AreEqual(64, a.Sha256!.Length);
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.Provenance), "provenance missing for " + a.Name);
                // SuperPlan A4: исторические образы Дубны/CERN в репозитории git-ignored и
                // не распространяются — каждый помечен как user-provided со способом получения.
                Assert.AreEqual(RuntimeAssetLicense.UserProvided, a.License, "license for " + a.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.ObtainHint), "obtain hint missing for " + a.Name);
            }
            Assert.AreEqual(5, RuntimeAssets.Catalog.Select(a => a.Name).Distinct().Count());
            Assert.IsTrue(RuntimeAssets.RequiredSet.Count > 0);
        }

        [TestMethod]
        public void Catalog_Sha256_MatchesShippedImages()
        {
            string? tapes = FindDevTapesDir();
            Assert.IsNotNull(tapes, "dev tapes dir not found");
            foreach (var a in RuntimeAssets.Catalog)
            {
                string p = Path.Combine(tapes!, a.Name);
                Assert.IsTrue(File.Exists(p), "missing shipped image " + a.Name);
                Assert.AreEqual(a.Sha256, RuntimeAssets.Sha256OfFile(p), "sha mismatch for " + a.Name);
            }
        }

        [TestMethod]
        public void ResolveInDirs_FindsAssetInLaterDirectory()
        {
            string root = Temp();
            try
            {
                string dirA = Path.Combine(root, "a");
                string dirB = Path.Combine(root, "b");
                Directory.CreateDirectory(dirA);
                Directory.CreateDirectory(dirB);
                File.WriteAllText(Path.Combine(dirB, "monsys.9"), "data");
                var asset = new RuntimeAsset { Name = "monsys.9", TapeId = 1, Sha256 = null, Required = true };
                var res = RuntimeAssets.ResolveInDirs(new[] { dirA, dirB }, new[] { asset });
                Assert.AreEqual(Path.GetFullPath(Path.Combine(dirB, "monsys.9")), res.PathsByAsset["monsys.9"]);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ResolveInDirs_MissingAsset_Throws_ListsAssetAndDir()
        {
            string root = Temp();
            try
            {
                string empty = Path.Combine(root, "empty");
                Directory.CreateDirectory(empty);
                var asset = new RuntimeAsset
                {
                    Name = "monsys.9", TapeId = 1, Sha256 = null, Required = true, Provenance = "P", ObtainHint = "H"
                };
                RuntimeAssetsException ex = Assert.Throws<RuntimeAssetsException>(
                    () => { RuntimeAssets.ResolveInDirs(new[] { empty }, new[] { asset }); });
                StringAssert.Contains(ex.Message, "monsys.9");
                StringAssert.Contains(ex.Message, "empty");
                Assert.AreEqual(1, ex.ProblemAssets.Count);
                Assert.IsTrue(ex.SearchDirectories.Any(d => d.Contains("empty")));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ResolveInDirs_ListsAllMissingRequired()
        {
            string root = Temp();
            try
            {
                string empty = Path.Combine(root, "empty");
                Directory.CreateDirectory(empty);
                RuntimeAssetsException ex = Assert.Throws<RuntimeAssetsException>(
                    () => { RuntimeAssets.ResolveInDirs(new[] { empty }, RuntimeAssets.RequiredSet); });
                Assert.AreEqual(RuntimeAssets.RequiredSet.Count, ex.ProblemAssets.Count);
                StringAssert.Contains(ex.Message, "Missing required resources");
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ResolveInDirs_CorrectSha_Resolves()
        {
            string root = Temp();
            try
            {
                string d = Path.Combine(root, "tapes");
                Directory.CreateDirectory(d);
                string file = Path.Combine(d, "monsys.9");
                File.WriteAllText(file, "payload");
                string sha = RuntimeAssets.Sha256OfFile(file);
                var asset = new RuntimeAsset { Name = "monsys.9", TapeId = 1, Sha256 = sha, Required = true };
                var res = RuntimeAssets.ResolveInDirs(new[] { d }, new[] { asset });
                Assert.AreEqual(sha, res.Sha256ByAsset["monsys.9"]);
                Assert.AreEqual(Path.GetFullPath(file), res.PathsByAsset["monsys.9"]);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ResolveInDirs_BadSha_ThrowsChecksumMismatch()
        {
            string root = Temp();
            try
            {
                string d = Path.Combine(root, "tapes");
                Directory.CreateDirectory(d);
                string file = Path.Combine(d, "monsys.9");
                File.WriteAllText(file, "payload");
                var asset = new RuntimeAsset { Name = "monsys.9", TapeId = 1, Sha256 = new string('0', 64), Required = true };
                RuntimeAssetsException ex = Assert.Throws<RuntimeAssetsException>(
                    () => { RuntimeAssets.ResolveInDirs(new[] { d }, new[] { asset }); });
                StringAssert.Contains(ex.Message, "Checksum mismatch");
                Assert.AreEqual(1, ex.ProblemAssets.Count);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void SearchDirectories_PutsExplicitAbsolutePathFirst()
        {
            string root = Temp();
            try
            {
                string explicitDir = Path.Combine(root, "explicit-tapes");
                Directory.CreateDirectory(explicitDir);
                string configPath = Path.Combine(root, "besm6.json");
                File.WriteAllText(configPath, "{\"tapes\":\"" + explicitDir.Replace("\\", "\\\\") + "\"}");
                Config cfg = Config.Load(configPath);
                var dirs = RuntimeAssets.SearchDirectories(cfg);
                Assert.IsTrue(dirs.Count >= 2, "expected at least two search directories");
                Assert.AreEqual(Path.GetFullPath(explicitDir), dirs[0]);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void Resolve_RealCatalog_InDevTapes_Succeeds()
        {
            string? tapes = FindDevTapesDir();
            Assert.IsNotNull(tapes, "dev tapes dir not found");
            var res = RuntimeAssets.ResolveInDirs(new[] { tapes! }, RuntimeAssets.RequiredSet);
            Assert.AreEqual(RuntimeAssets.RequiredSet.Count, res.PathsByAsset.Count);
        }
    }
}
