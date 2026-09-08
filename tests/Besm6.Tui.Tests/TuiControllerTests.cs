using Besm6.Tui;

namespace Besm6.Tui.Tests
{
    /// <summary>
    /// Тесты контроллера TUI: команды без машины дают прежние статусы,
    /// help/unknown не ломают сессию, asm работает без машины.
    /// </summary>
    [TestClass]
    public class TuiControllerTests
    {
        private static TuiController NewController() => new(new Config());

        [TestMethod]
        public void Execute_Help_SetsHelpStatus()
        {
            var controller = NewController();
            controller.Execute(TuiCommandParser.Parse("help"));
            StringAssert.Contains(controller.State.Status, "load file");
        }

        [TestMethod]
        public void Execute_Unknown_LeavesStatus()
        {
            var controller = NewController();
            string before = controller.State.Status;
            controller.Execute(TuiCommandParser.Parse("bogus"));
            Assert.AreEqual(before, controller.State.Status);
        }

        [TestMethod]
        [DataRow("step")]
        [DataRow("run")]
        [DataRow("mem 10")]
        [DataRow("write 10 20")]
        [DataRow("reset")]
        public void Execute_WithoutMachine_LoadAFileFirst(string input)
        {
            var controller = NewController();
            controller.Execute(TuiCommandParser.Parse(input));
            Assert.AreEqual("load a file first", controller.State.Status);
        }

        [TestMethod]
        public void Execute_LoadWithoutFile_SetsUsage()
        {
            var controller = NewController();
            controller.Execute(TuiCommandParser.Parse("load"));
            Assert.AreEqual("load <file.dub>", controller.State.Status);
        }

        [TestMethod]
        public void Execute_LoadMissingFile_SetsNotFound()
        {
            var controller = NewController();
            controller.Execute(TuiCommandParser.Parse("load no-such-file.dub"));
            StringAssert.StartsWith(controller.State.Status, "not found:");
        }

        [TestMethod]
        public void Execute_Asm_AssemblesWithoutMachine()
        {
            var controller = NewController();
            controller.Execute(TuiCommandParser.Parse("asm xta 10"));
            StringAssert.StartsWith(controller.State.Status, "asm: xta 10 = 0x");
        }

        [TestMethod]
        public void Initialize_MissingFile_SetsNotFoundStatus()
        {
            var controller = NewController();
            controller.Initialize("missing.dub");
            StringAssert.StartsWith(controller.State.Status, "not found:");
        }
    }
}
