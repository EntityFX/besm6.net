using System.Text.Json;
using Besm6.Cli;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExecutionSpeedCliTests
{
    [TestMethod]
    public void InvalidOrMissingSpeed_FailsBeforeLoadingJob()
    {
        foreach (string[] args in new[] { new[] { "run", "missing.dub", "--speed" },
            new[] { "run", "missing.dub", "--speed", "fast" }, new[] { "run", "missing.dub", "--speed", "1" } })
        {
            var error = new StringWriter();
            Assert.AreEqual(1, CliApplication.Run(args, new StringWriter(), error));
            StringAssert.Contains(error.ToString(), "--speed requires 'original' or 'max'");
        }
    }

    [TestMethod]
    public void InvalidConfigSpeed_IsReportedAsCliError()
    {
        string config = Path.GetTempFileName();
        try
        {
            File.WriteAllText(config, "{\"speed\":\"fast\"}");
            var error = new StringWriter();
            Assert.AreEqual(1, CliApplication.Run(new[] { "run", "missing.dub", "--config", config }, new StringWriter(), error));
            StringAssert.Contains(error.ToString(), "speed must be 'original' or 'max'");
        }
        finally { File.Delete(config); }
    }

    [TestMethod]
    public void Override_Profile_DumpAndRegisterTrace_WorkTogether()
    {
        string directory = Path.Combine(Path.GetTempPath(), "besm6-speed-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ulong word = Besm6.Asm.Assembler.Asm("vtm 1(1), stop");
            string job = Path.Combine(directory, "job.dub");
            File.WriteAllLines(job, new[] { "*trans-main:1000", "`" + Convert.ToString((long)word, 8).PadLeft(16, '0') });
            string config = Path.Combine(directory, "config.json");
            File.WriteAllText(config, "{\"speed\":\"original\",\"useWallClock\":false}");
            string dump = Path.Combine(directory, "memory.bin");
            string registers = Path.Combine(directory, "registers.txt");
            var output = new StringWriter();
            var error = new StringWriter();
            int result = CliApplication.Run(new[] { "run", job, "--config", config, "--speed", "max", "--stats", "--profile",
                "--dump-mem", dump, "--dump-mem-at", "1", "--dump-mem-count", "2", "--trace-regs", registers }, output, error);
            Assert.AreEqual(0, result, error.ToString());
            string report = output.ToString().Split('\n').Single(line => line.StartsWith("Execution stats: "));
            using var json = JsonDocument.Parse(report["Execution stats: ".Length..]);
            var measured = json.RootElement.GetProperty("statistics");
            Assert.AreEqual("max", measured.GetProperty("speed").GetString());
            Assert.AreEqual(2L, measured.GetProperty("completedInstructions").GetInt64());
            Assert.AreEqual(3L, measured.GetProperty("modelCycles").GetInt64());
            StringAssert.Contains(output.ToString(), "Инструкций: 2");
            Assert.AreEqual(12L, new FileInfo(dump).Length);
            Assert.IsTrue(new FileInfo(registers).Length > 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
