using Besm6.Tui;

namespace Besm6.Tui.Tests
{
    /// <summary>
    /// Тесты чистого рендера панели: снимок без машины, снимок с машиной
    /// (A/Y/R/M/C/K), ANSI-сохранение, цветовая семантика статуса.
    /// Рендер не выполняет команды и не обращается к файловой системе.
    /// </summary>
    [TestClass]
    public class TuiRendererTests
    {
        private const string RED = "\x1b[91m";
        private const string GREEN = "\x1b[92m";

        [TestMethod]
        public void Render_WithoutMachine_ShowsPlaceholderPanel()
        {
            string panel = new TuiRenderer().Render(new TuiSessionState(), null);

            StringAssert.Contains(panel, "BESM-6");
            StringAssert.Contains(panel, "(no file)");
            StringAssert.Contains(panel, "ЛАМПЫ-ИНДИКАТОРЫ");
            StringAssert.Contains(panel, "РЕГИСТРЫ");
            StringAssert.Contains(panel, "(загрузите программу: load file.dub)");
            StringAssert.Contains(panel, "ПАМЯТЬ");
            StringAssert.Contains(panel, "(нет памяти)");
            StringAssert.Contains(panel, "СТАТУС: ready");
        }

        [TestMethod]
        public void Render_WithMachine_ShowsRegistersAndMemoryWindow()
        {
            var machine = new MachineCore(4096);
            machine.Cpu.SetK(0x100);
            machine.Cpu.SetA(0x12345);
            machine.Cpu.SetM(0, 0x55);
            machine.Memory.Write(0x100, new Word48(0x100200));
            var state = new TuiSessionState { MemoryBase = 0x100, JobFile = "job.dub" };

            string panel = new TuiRenderer().Render(state, machine);

            StringAssert.Contains(panel, "K     0x");
            StringAssert.Contains(panel, "A     0x");
            StringAssert.Contains(panel, "Y     0x");
            StringAssert.Contains(panel, "C     0x");
            StringAssert.Contains(panel, "R     0x");
            StringAssert.Contains(panel, "MODE  ");
            StringAssert.Contains(panel, "M[0]");
            StringAssert.Contains(panel, "M[17]");
            StringAssert.Contains(panel, "job.dub");
            StringAssert.Contains(panel, "◄ K");
        }

        [TestMethod]
        public void Render_StatusColor_RedForBadGreenForOk()
        {
            var renderer = new TuiRenderer();
            string bad = renderer.Render(new TuiSessionState { Status = "stopped: limit" }, null);
            string ok = renderer.Render(new TuiSessionState { Status = "ready" }, null);

            StringAssert.Contains(bad, RED + "  СТАТУС:");
            StringAssert.Contains(ok, GREEN + "  СТАТУС:");
        }

        [TestMethod]
        public void Render_KeepsClearScreenAndAnsiLayout()
        {
            string panel = new TuiRenderer().Render(new TuiSessionState(), null);
            StringAssert.StartsWith(panel, "\x1b[2J\x1b[H");
        }
    }
}
