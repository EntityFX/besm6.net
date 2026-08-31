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
}
