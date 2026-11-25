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

    [TestMethod]
    public void SupervisorHardwareRunsExtracodeInDubWithoutMachineJson()
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".dub");
        try
        {
            // Octal 070 is extracode 56, whose supervisor vector is 0170 (decimal 376).
            // Loading at 0567 places the STOP where the extracode returns, so the run
            // proves the UU duration exists and the instruction retires. The raw words
            // are full 48-bit values: left half 070000 (*70 0), then left half 3300000
            // (STOP), each with an empty right half.
            File.WriteAllText(path, "*trans-main 0567\n*assem\n`0070000000000000\n`0330000000000000\n*end file\n");
            var output = new StringWriter();
            var error = new StringWriter();
            int code = CliApplication.Run(
                new[] { "run", path, "--supervisor-hardware", "--machine-state" }, output, error);
            Assert.AreEqual(0, code, error.ToString());
            StringAssert.Contains(output.ToString(), "Halted by STOP at 0178 after 2 instructions");
            string line = output.ToString().Split('\n')
                .Last(line => line.StartsWith("Machine state: ", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(line["Machine state: ".Length..]);
            var state = json.RootElement;
            Assert.AreEqual("Hardware", state.GetProperty("executionModel").GetString());
            Assert.AreEqual(7_000UL, state.GetProperty("hardwareNanoseconds").GetUInt64(),
                "The 50-cycle extracode and 2-cycle STOP must contribute 7000 ns of model time.");
            Assert.IsTrue(state.GetProperty("buffers").EnumerateArray()
                .Any(buffer => buffer.GetProperty("name").GetString() == "БРЧ"));
            Assert.AreEqual(string.Empty, error.ToString());
        }
        finally { File.Delete(path); }
    }
}
