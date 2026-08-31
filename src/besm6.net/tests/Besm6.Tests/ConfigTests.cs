using System;
using System.IO;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigTests
{
    [TestMethod]
    public void Load_ExplicitMissingPath_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Throws<FileNotFoundException>(() => Config.Load(path));
    }

    [TestMethod]
    public void ResolvePath_RelativeResource_UsesConfigurationDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6_cfg_" + Guid.NewGuid().ToString("N"));
        string images = Path.Combine(root, "images");
        Directory.CreateDirectory(images);
        string configPath = Path.Combine(root, "besm6.json");
        File.WriteAllText(configPath, "{\"tapes\":\"images\"}");
        try
        {
            Config config = Config.Load(configPath);
            Assert.AreEqual(Path.GetFullPath(images), config.ResolvePath(config.Tapes!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ResolvePath_DefaultTapes_UsesBundledApplicationDirectory()
    {
        string tapes = Path.Combine(AppContext.BaseDirectory, "tapes");
        Assert.IsTrue(Directory.Exists(tapes), "build output must contain bundled tapes");
        Assert.AreEqual(Path.GetFullPath(tapes), new Config().ResolvePath("tapes"));
    }
}
