using Besm6.AspNetCore.Web.Hosting;

namespace Besm6.AspNetCore.Web.Tests;

[TestClass]
public sealed class ExampleCatalogTests
{
    [TestMethod]
    public void CatalogOnlyResolvesListedDubFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6-examples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(root, "hello.dub"), "*end file\n");
            File.WriteAllText(Path.Combine(root, "nested", "hello.dub"), "*end file\n");
            File.WriteAllText(Path.Combine(root, "private.txt"), "not an example");
            var catalog = new ExampleCatalog(root);
            Assert.AreEqual(2, catalog.Entries.Count);
            Assert.IsTrue(catalog.TryGetPath("nested/hello.dub", out string path));
            Assert.AreEqual(Path.Combine(root, "nested", "hello.dub"), path);
            foreach (string id in new[] { "../hello.dub", "private.txt", Path.Combine(root, "hello.dub"), "nested/../hello.dub" })
                Assert.IsFalse(catalog.TryGetPath(id, out _));
        }
        finally { Directory.Delete(root, true); }
    }
}
