using System;
using System.IO;
using System.Text;
using Besm6.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// CLI application contract tests via <see cref="CliApplication.Run"/>.
    /// </summary>
    [TestClass]
    public class CliApplicationTests
    {
        [TestMethod]
        public void NoArgs_ShowHelp_ReturnsZero()
        {
            var sb = new StringBuilder();
            var outWriter = new StringWriter(sb);
            int rc = CliApplication.Run(Array.Empty<string>(), outWriter, outWriter);

            Assert.AreEqual(0, rc);
            string output = sb.ToString();
            StringAssert.Contains(output, "BESM-6 Simulator");
            StringAssert.Contains(output, "Usage: besm6 <command>");
            StringAssert.Contains(output, "run");
            StringAssert.Contains(output, "asm");
            StringAssert.Contains(output, "disasm");
        }

        [TestMethod]
        public void HelpCommand_ReturnsZero_ListsCommands()
        {
            var sb = new StringBuilder();
            var outWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "help" }, outWriter, outWriter);

            Assert.AreEqual(0, rc);
            string output = sb.ToString();
            StringAssert.Contains(output, "BESM-6 Simulator");
            StringAssert.Contains(output, "run");
            StringAssert.Contains(output, "asm");
            StringAssert.Contains(output, "disasm");
            StringAssert.Contains(output, "check");
        }

        [TestMethod]
        public void UnknownCommand_ReturnsOne_WritesError()
        {
            var sb = new StringBuilder();
            var errWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "nonexistent" }, new StringWriter(), errWriter);

            Assert.AreEqual(1, rc);
            StringAssert.Contains(sb.ToString(), "Unknown command: nonexistent");
        }

        [TestMethod]
        public void RunCommand_MissingFile_ReturnsOne()
        {
            var sb = new StringBuilder();
            var errWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "run" }, new StringWriter(), errWriter);

            Assert.AreEqual(1, rc);
            StringAssert.Contains(sb.ToString(), "Usage");
        }

        [TestMethod]
        public void AsmCommand_ValidInput_ReturnsZero()
        {
            var sb = new StringBuilder();
            var outWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "asm", "xta 10, atx 20" }, outWriter, outWriter);

            Assert.AreEqual(0, rc, $"asm failed: {sb}");
        }

        [TestMethod]
        public void DisasmCommand_ValidWord_ReturnsZero()
        {
            var sb = new StringBuilder();
            var outWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "disasm", "0010000000000000" }, outWriter, outWriter);

            Assert.AreEqual(0, rc, $"disasm failed: {sb}");
        }

        [TestMethod]
        public void CaseInsensitive_CommandName()
        {
            var sb = new StringBuilder();
            var outWriter = new StringWriter(sb);
            int rc = CliApplication.Run(new[] { "HELP" }, outWriter, outWriter);

            Assert.AreEqual(0, rc);
        }
    }
}