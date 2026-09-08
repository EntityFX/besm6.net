using Besm6.Tui;

namespace Besm6.Tui.Tests
{
    /// <summary>
    /// Тесты разбора строк TUI-команд: канонические имена, псевдонимы,
    /// аргументы, регистронезависимость.
    /// </summary>
    [TestClass]
    public class TuiCommandParserTests
    {
        [TestMethod]
        [DataRow("quit", TuiCommand.Quit)]
        [DataRow("exit", TuiCommand.Quit)]
        [DataRow("q", TuiCommand.Quit)]
        [DataRow("help", TuiCommand.Help)]
        [DataRow("h", TuiCommand.Help)]
        [DataRow("load hello.dub", TuiCommand.Load)]
        [DataRow("run", TuiCommand.Run)]
        [DataRow("step", TuiCommand.Step)]
        [DataRow("s", TuiCommand.Step)]
        [DataRow("c", TuiCommand.Step)]
        [DataRow("cont", TuiCommand.Step)]
        [DataRow("continue", TuiCommand.Step)]
        [DataRow("mem 10", TuiCommand.Mem)]
        [DataRow("m", TuiCommand.Mem)]
        [DataRow("asm xta 10", TuiCommand.Asm)]
        [DataRow("write 10 20", TuiCommand.Write)]
        [DataRow("w 10 20", TuiCommand.Write)]
        [DataRow("reset", TuiCommand.Reset)]
        [DataRow("rst", TuiCommand.Reset)]
        public void Parse_RecognizesCommandsAndAliases(string input, TuiCommand expected)
        {
            var request = TuiCommandParser.Parse(input);
            Assert.AreEqual(expected, request.Command);
            Assert.IsTrue(request.IsKnown);
        }

        [TestMethod]
        public void Parse_CaseInsensitive()
        {
            Assert.AreEqual(TuiCommand.Run, TuiCommandParser.Parse("RUN").Command);
            Assert.AreEqual(TuiCommand.Load, TuiCommandParser.Parse("LOAD job.dub").Command);
        }

        [TestMethod]
        public void Parse_CarriesArgument()
        {
            Assert.AreEqual("hello.dub", TuiCommandParser.Parse("load  hello.dub  ").Argument);
            Assert.AreEqual("xta 10", TuiCommandParser.Parse("asm xta 10").Argument);
            Assert.AreEqual("10", TuiCommandParser.Parse("mem 10").Argument);
        }

        [TestMethod]
        public void Parse_WriteSplitsAddressAndValue()
        {
            var request = TuiCommandParser.Parse("write 10 20");
            Assert.AreEqual(TuiCommand.Write, request.Command);
            Assert.AreEqual("10", request.Argument);
            Assert.AreEqual("20", request.Secondary);
        }

        [TestMethod]
        [DataRow("bogus")]
        [DataRow("")]
        [DataRow("   ")]
        public void Parse_UnknownReturnsUnknown(string input)
        {
            var request = TuiCommandParser.Parse(input);
            Assert.AreEqual(TuiCommand.Unknown, request.Command);
            Assert.IsFalse(request.IsKnown);
        }
    }
}
