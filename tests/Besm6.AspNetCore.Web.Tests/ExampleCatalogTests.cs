using Besm6.AspNetCore.Web.Hosting;

namespace Besm6.AspNetCore.Web.Tests;

[TestClass]
public sealed class ExampleCatalogTests
{
    [TestMethod]
    public void CatalogOnlyResolvesListedProgramAndJsonFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6-examples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(root, "hello.dub"), "*end file\n");
            File.WriteAllText(Path.Combine(root, "nested", "hello.dub"), "*end file\n");
            File.WriteAllText(Path.Combine(root, "private.txt"), "not an example");
            File.WriteAllText(Path.Combine(root, "machine.json"), "{}");
            var catalog = new ExampleCatalog(root);
            Assert.AreEqual(3, catalog.Entries.Count);
            Assert.IsTrue(catalog.TryGetPath("nested/hello.dub", out string path));
            Assert.AreEqual(Path.Combine(root, "nested", "hello.dub"), path);
            Assert.IsTrue(catalog.TryGetPath("machine.json", out _));
            foreach (string id in new[] { "../hello.dub", "private.txt", Path.Combine(root, "hello.dub"), "nested/../hello.dub" })
                Assert.IsFalse(catalog.TryGetPath(id, out _));
        }
        finally { Directory.Delete(root, true); }
    }
}
