using System.Text;
using Besm6.Tui;

namespace Besm6.Tui.Tests
{
    /// <summary>
    /// Тесты точки входа: --help печатает справку и не входит в интерактивный
    /// цикл; ошибочные аргументы дают ненулевой код.
    /// </summary>
    [TestClass]
    public class TuiApplicationTests
    {
        private static int RunCaptured(string[] args, out string stdout, out string stderr)
        {
            var realOut = Console.Out;
            var realErr = Console.Error;
            var outSb = new StringBuilder();
            var errSb = new StringBuilder();
            Console.SetOut(new StringWriter(outSb));
            Console.SetError(new StringWriter(errSb));
            int code;
            try
            {
                code = TuiApplication.Run(args);
            }
            finally
            {
                Console.SetOut(realOut);
                Console.SetError(realErr);
            }
            stdout = outSb.ToString();
            stderr = errSb.ToString();
            return code;
        }

        [TestMethod]
        public void Run_Help_ReturnsZeroWithoutInteractiveLoop()
        {
            int code = RunCaptured(new[] { "--help" }, out string stdout, out _);

            Assert.AreEqual(0, code);
            StringAssert.Contains(stdout, "besm6-tui");
            StringAssert.Contains(stdout, "--config");
            StringAssert.Contains(stdout, "load <file.dub>");
        }

        [TestMethod]
        public void Run_UnknownOption_ReturnsOne()
        {
            int code = RunCaptured(new[] { "--wat" }, out _, out string stderr);
            Assert.AreEqual(1, code);
            StringAssert.Contains(stderr, "Unknown option");
        }

        [TestMethod]
        public void Run_ConfigWithoutPath_ReturnsOne()
        {
            int code = RunCaptured(new[] { "--config" }, out _, out _);
            Assert.AreEqual(1, code);
        }

        [TestMethod]
        public void Run_MissingExplicitConfig_ReturnsOne()
        {
            int code = RunCaptured(new[] { "--config", "no-such-config.json" }, out _, out string stderr);
            Assert.AreEqual(1, code);
            StringAssert.StartsWith(stderr, "config error:");
        }
    }
}
