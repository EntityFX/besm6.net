using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CliContractTests
{
    [TestMethod]
    public void Help_PrintsAllCommandsAndReturnsZero()
    {
        Type program = typeof(Config).Assembly.GetType("Besm6.Program", throwOnError: true)!;
        MethodInfo main = program.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            int exitCode = (int)main.Invoke(null, new object[] { new[] { "help" } })!;
            Assert.AreEqual(0, exitCode);
            StringAssert.Contains(stdout.ToString(), "run");
            StringAssert.Contains(stdout.ToString(), "help");
            Assert.AreEqual(string.Empty, stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [TestMethod]
    public void Check_InstructionLimit_ReturnsFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6_check_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ulong jump = Besm6.Asm.Assembler.Asm("uj 1000");
            ulong word = (jump << 24) | jump;
            string octal = Convert.ToString((long)word, 8).PadLeft(16, '0');
            File.WriteAllLines(Path.Combine(root, "loop.dub"), new[]
            {
                "*trans-main:1000",
                "`" + octal,
            });
            string config = Path.Combine(root, "besm6.json");
            File.WriteAllText(config, "{\"checkLimit\":4}");

            int exitCode = new Besm6.Cli.CheckCommand().Execute(
                new[] { root, "--limit", "4", "--config", config });

            Assert.AreEqual(1, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Check_ControlCardOnlyJob_IsNotReportedAsNoContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6_check_" + Guid.NewGuid().ToString("N"));
        string emptyTapes = Path.Combine(root, "empty-tapes");
        Directory.CreateDirectory(emptyTapes);
        try
        {
            File.WriteAllLines(Path.Combine(root, "name.dub"), new[] { "*name sample", "*end file" });
            string config = Path.Combine(root, "besm6.json");
            File.WriteAllText(config, "{\"tapes\":\"empty-tapes\",\"checkLimit\":4}");
            TextWriter original = Console.Out;
            var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                int exitCode = new Besm6.Cli.CheckCommand().Execute(new[] { root, "--config", config });
                Assert.AreEqual(1, exitCode);
                Assert.IsFalse(output.ToString().Contains("NO-CONTENT"));
                StringAssert.Contains(output.ToString(), "MONSYS");
            }
            finally
            {
                Console.SetOut(original);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
