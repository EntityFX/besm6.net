using Besm6.Cli;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GuestBenchmarkCliTests
{
    [TestMethod]
    [DataRow("--passes")]
    [DataRow("--mflops")]
    [DataRow("--ops")]
    [DataRow("--loop-cycles")]
    [DataRow("--baseline-cycles")]
    [DataRow("--baseline-instructions")]
    public void RemovedRatingOptionFailsBeforeConfigOrJobLoading(string option)
    {
        foreach (string[] args in new[]
        {
            new[] { "run", "missing.dub", option },
            new[] { "run", "missing.dub", option, "100", "--config", "missing.json" },
            new[] { "run", "missing.dub", option + "=100" },
            new[] { "run", option, "100" }
        })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.AreEqual(1, CliApplication.Run(args, output, error));
            StringAssert.Contains(error.ToString(), option + " has been removed");
            StringAssert.Contains(error.ToString(), "--guest-clock");
            Assert.AreEqual(string.Empty, output.ToString());
        }
    }

    [TestMethod]
    public void HelpRetainsGeneralDiagnosticsWithoutRatingParameters()
    {
        string usage = new RunCommand().Usage;
        foreach (string option in new[] { "--stats", "--profile", "--guest-clock", "--speed" })
            StringAssert.Contains(usage, option);
        foreach (string option in new[] { "--passes", "--mflops", "--ops", "--loop-cycles",
            "--baseline-cycles", "--baseline-instructions" })
            Assert.IsFalse(usage.Contains(option, StringComparison.Ordinal));
    }
}
