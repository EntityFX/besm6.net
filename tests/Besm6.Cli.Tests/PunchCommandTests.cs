using Besm6.Cli;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public class PunchCommandTests
{
    [TestMethod]
    public void TextInput_PreservesFortranIndentation()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(0, CliApplication.Run(new[] { "punch", "--text", "      A=B+3", "--encoding", "ibm", "--format", "ascii" }, output, error));
        StringAssert.Contains(output.ToString(), "Card 1 (Ibm)");
        StringAssert.Contains(output.ToString(), "Row 12   ......O. OO......");
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    public void FileInput_EmitsOneCardPerLineIncludingBlankLines_ToUtf8File()
    {
        string dir = Path.Combine(Path.GetTempPath(), "besm6-cards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "input.txt"), output = Path.Combine(dir, "output.txt");
            File.WriteAllText(input, "АБ3\r\n\r\n      A=B+3\r\n");
            Assert.AreEqual(0, CliApplication.Run(new[] { "punch", input, "--output", output }, new StringWriter(), new StringWriter()));
            string rendered = File.ReadAllText(output);
            StringAssert.Contains(rendered, "Card 3 (Upp)");
            Assert.IsFalse(rendered.Contains("Card 4"));
            StringAssert.Contains(rendered, "Row 01   ··●····· ●·●····● ●·····●●");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Stdin_BrailleIsThreeRowsPerCard()
    {
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader("A\nB\n"));
            using var output = new StringWriter();
            Assert.AreEqual(0, CliApplication.Run(new[] { "punch", "-", "--format", "braille" }, output, new StringWriter()));
            var lines = output.ToString().Split('\n');
            Assert.AreEqual(9, lines.Length);
            foreach (int i in new[] { 0, 1, 2, 4, 5, 6 }) Assert.AreEqual(40, lines[i].Length);
        }
        finally { Console.SetIn(original); }
    }

    [TestMethod]
    public void InvalidArgumentsAndText_ReturnFailureWithoutPartialCards()
    {
        string[][] cases = {
            new[] { "--encoding", "ascii" }, new[] { "--format", "png" }, new[] { "--text" },
            new[] { "--unknown" }, new[] { "--text", "?" }, new[] { "--text", "A\n?" },
            new[] { "--text", new string('A', 121) }, new[] { "--text", "A", "some.txt" },
            new[] { "--text", new string('A', 81), "--encoding", "ibm" }
        };
        foreach (var args in cases)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.AreEqual(1, CliApplication.Run(new[] { "punch" }.Concat(args).ToArray(), output, error));
            Assert.AreEqual("", output.ToString());
            StringAssert.Contains(error.ToString(), "Error:");
        }
    }

    [TestMethod]
    public void InvalidDeck_DoesNotOverwriteOutput()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "keep");
            Assert.AreEqual(1, CliApplication.Run(new[] { "punch", "--text", "A\n?", "--output", path }, new StringWriter(), new StringWriter()));
            Assert.AreEqual("keep", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
