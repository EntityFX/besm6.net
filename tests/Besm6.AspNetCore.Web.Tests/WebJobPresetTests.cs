using System.Text;
using Besm6.AspNetCore.Web.Hosting;

namespace Besm6.AspNetCore.Web.Tests;

[TestClass]
public sealed class WebJobPresetTests
{
    [TestMethod]
    public void BundledBenchmarkPresetsResolveIdenticalDeckAndExplicitOptions()
    {
        var catalog = new ExampleCatalog();
        Assert.IsTrue(catalog.TryGetPath("dhrystone-forex.dub", out string deck));
        foreach (string name in new[] { "max", "original", "virtual", "debug", "buffered" })
        {
            Assert.IsTrue(catalog.TryGetPath($"web-jobs/dhrystone-{name}.json", out string path));
            var job = WebJobPreset.Resolve(File.ReadAllBytes(path), true, catalog);
            CollectionAssert.AreEqual(File.ReadAllBytes(deck), job.Content);
            Assert.AreEqual(name == "buffered" ? "buffered" : "dubna", job.Options.MachineModel);
            Assert.AreEqual(name is "debug" or "buffered" ? "max" : name, job.Options.Speed);
            Assert.AreEqual(name == "debug" ? "debug" : "full", job.Options.Mode);
            Assert.AreEqual(false, job.Options.AutoRefresh);
            Assert.AreEqual(false, job.Options.DecodeMemory);
        }
    }

    [TestMethod]
    [DataRow("{\"kind\":\"web-job\",\"example\":\"../secret.dub\"}")]
    [DataRow("{\"kind\":\"web-job\",\"example\":\"dhrystone-forex.dub\",\"machineModel\":\"supervisor-hardware\"}")]
    [DataRow("{\"kind\":\"web-job\",\"example\":\"machines/supervisor-functional.json\"}")]
    public void PresetsRejectPathsAndIncompatibleModels(string json)
    {
        Assert.Throws<ArgumentException>(() => WebJobPreset.Resolve(Encoding.UTF8.GetBytes(json), true, new ExampleCatalog()));
    }

    [TestMethod]
    [DataRow("hardware")]
    [DataRow("functional")]
    public void MachineImageSelectsItsExecutionModelAndKeepsOriginalBytes(string model)
    {
        byte[] json = Encoding.UTF8.GetBytes("\uFEFF{\"executionModel\":\"" + model + "\"}");
        var job = WebJobPreset.Resolve(json, true, new ExampleCatalog());
        CollectionAssert.AreEqual(json, job.Content);
        Assert.AreEqual("supervisor-" + model, job.Options.MachineModel);
    }
}
