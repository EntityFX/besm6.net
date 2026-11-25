using System.Text.Json;
using Besm6.Cli;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareDubCliTests
{
    [TestMethod]
    public void SupervisorHardwareRunsDubWithoutMachineJson()
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".dub");
        try
        {
            File.WriteAllText(path, "*assem\natx 1234\nstop\n*end file\n");
            var output = new StringWriter();
            var error = new StringWriter();
            int code = CliApplication.Run(
                new[] { "run", path, "--supervisor-hardware", "--machine-state" }, output, error);
            Assert.AreEqual(0, code, error.ToString());
            string line = output.ToString().Split('\n')
                .Last(line => line.StartsWith("Machine state: ", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(line["Machine state: ".Length..]);
            var state = json.RootElement;
            Assert.AreEqual("Hardware", state.GetProperty("executionModel").GetString());
            Assert.IsTrue(state.GetProperty("buffers").EnumerateArray()
                .Any(buffer => buffer.GetProperty("name").GetString() == "БРЧ"));
        }
        finally { File.Delete(path); }
    }
}
