using System.Text;
using Besm6.Assembler;

namespace Besm6.Tui
{
    /// <summary>
    /// Чистый рендерер панели БЭСМ-6: по снимку сессии и (опционально)
    /// read-only состоянию машины собирает ANSI-строку панели.
    /// Не выполняет команды, не пишет в консоль и не обращается к файловой системе.
    /// </summary>
    public sealed class TuiRenderer
    {
        // ANSI-коды.
        private const string CLR = "\x1b[2J\x1b[H";
        private const string RESET = "\x1b[0m";
        private const string BOLD = "\x1b[1m";
        private const string DIM = "\x1b[2m";
        private const string CYAN = "\x1b[96m";
        private const string GREEN = "\x1b[92m";
        private const string RED = "\x1b[91m";
        private const string YELLOW = "\x1b[93m";
        private const string GRAY = "\x1b[90m";

        /// <summary>
        /// Собирает полную ANSI-панель: заголовок, лампы, регистры, окно памяти, статус.
        /// </summary>
        /// <param name="state">Снимок сессии.</param>
        /// <param name="machine">Машина (read-only) или null, если не создана.</param>
        public string Render(TuiSessionState state, MachineCore? machine)
        {
            var sb = new StringBuilder();
            sb.Append(CLR);
            Header(sb, state);
            IndicatorLamps(sb, state, machine);
            Registers(sb, state, machine);
            MemoryWindow(sb, state, machine);
            StatusLine(sb, state);
            return sb.ToString();
        }

        private static void Header(StringBuilder sb, TuiSessionState state)
        {
            sb.Append(BOLD).Append(CYAN).Append("  ╔═ BESM-6 · Машина").Append(RESET);
            sb.Append("  ").Append(GRAY).Append("════════════════════════════════════════════════════").Append(RESET).Append('\n');
            string loaded = state.JobFile != null ? state.JobFile : "(no file)";
            sb.Append(GRAY).Append("  ").Append(RESET).Append(DIM).Append("file: ").Append(RESET).Append(loaded).Append('\n');
            sb.Append('\n');
        }

        private static void IndicatorLamps(StringBuilder sb, TuiSessionState state, MachineCore? machine)
        {
            sb.Append(GRAY).Append("  ЛАМПЫ-ИНДИКАТОРЫ:").Append(RESET).Append('\n');
            sb.Append("   ");
            if (machine == null)
            {
                Lamp(sb, "RUN", false);
                Lamp(sb, "HALT", false);
                Lamp(sb, "ADD", false);
                Lamp(sb, "MUL", false);
                Lamp(sb, "LOG", false);
                Lamp(sb, "RIGHT", false);
            }
            else
            {
                var cpu = machine.Cpu;
                Lamp(sb, "RUN", state.Running);
                Lamp(sb, "HALT", state.Halted);
                Lamp(sb, "ADD", cpu.RMode == "ADD");
                Lamp(sb, "MUL", cpu.RMode == "MUL");
                Lamp(sb, "LOG", cpu.RMode == "LOG");
                Lamp(sb, "RIGHT", cpu.RightInstruction);
            }
            sb.Append('\n');
        }

        private static void Registers(StringBuilder sb, TuiSessionState state, MachineCore? machine)
        {
            sb.Append(BOLD).Append(GRAY).Append("  ─── РЕГИСТРЫ ─────────────────────────────────────────").Append(RESET).Append('\n');

            if (machine == null)
            {
                sb.Append(DIM).Append("   (загрузите программу: load file.dub)").Append(RESET).Append('\n');
                sb.Append('\n');
                return;
            }
            var cpu = machine.Cpu;

            void Row(string a, string b)
            {
                sb.Append("   ").Append(a.PadRight(24)).Append(GRAY).Append("│").Append(RESET);
                sb.Append("  ").Append(b).Append('\n');
            }

            Row("K     " + Hex(cpu.K), "A     " + Hex(cpu.A.Value));
            Row("Y     " + Hex(cpu.Y.Value), "C     " + Hex(cpu.C));
            Row("R     " + Hex(cpu.R), "MODE  " + cpu.RMode);
            Row("STEPS " + state.InstructionCount.ToString(), "STATE " + (state.Halted ? "HALTED" : (state.Running ? "RUNNING" : "IDLE")));
            sb.Append('\n');

            // Индексные регистры M[0..15] — две колонки по 8.
            sb.Append(GRAY).Append("   индексные:").Append(RESET).Append('\n');
            for (int i = 0; i < 8; i++)
            {
                sb.Append("   ").Append(M(i)).Append(" ").Append(Hex((ulong)cpu.GetM(i)).PadRight(16));
                sb.Append(GRAY).Append("│").Append(RESET).Append("  ");
                sb.Append(M(i + 8)).Append(" ").Append(Hex((ulong)cpu.GetM(i + 8)));
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        private static void MemoryWindow(StringBuilder sb, TuiSessionState state, MachineCore? machine)
        {
            sb.Append(BOLD).Append(GRAY).Append("  ─── ПАМЯТЬ (окно 16 слов) ──────────────────────────────").Append(RESET).Append('\n');

            if (machine == null)
            {
                sb.Append(DIM).Append("   (нет памяти)").Append(RESET).Append('\n');
                sb.Append('\n');
                return;
            }
            var cpu = machine.Cpu;
            int k = (int)(cpu.K & 0x7FFF);
            var mem = machine.Memory;

            for (int i = 0; i < 16; i++)
            {
                int a = (state.MemoryBase + i) & 0x7FFF;
                if (a >= mem.Size) break;
                var w = mem.Read((uint)a);
                bool isK = (a == k);
                if (isK) sb.Append(YELLOW);

                sb.Append("   ").Append(a.ToString("X4")).Append(GRAY).Append(" │ ").Append(RESET);
                sb.Append(w.Value.ToString("X12")).Append(GRAY).Append(" │ ").Append(RESET);
                sb.Append(Disassembler.DisasmHalf((long)(w.Value >> 24))).Append(" ");
                sb.Append(Disassembler.DisasmHalf((long)(w.Value & 0xFFFFFFL)));
                if (isK)
                {
                    sb.Append(BOLD).Append(GREEN).Append("  ◄ K").Append(RESET);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        private static void StatusLine(StringBuilder sb, TuiSessionState state)
        {
            bool bad = state.Status.StartsWith("stopped") || state.Status.StartsWith("not found") ||
                       state.Status.Contains("error") || state.Status.StartsWith("Unknown") ||
                       state.Status.StartsWith("bad") || state.Status.StartsWith("load a file") ||
                       state.Status.StartsWith("write <") || state.Status.StartsWith("asm <");
            string color = bad ? RED : GREEN;
            sb.Append(GRAY).Append("  ─────────────────────────────────────────────────────────────").Append(RESET).Append('\n');
            sb.Append(BOLD).Append(color).Append("  СТАТУС: " + state.Status).Append(RESET).Append('\n');
            sb.Append(GRAY).Append("  [load | run | step | mem | asm | write | reset | help | quit]").Append(RESET).Append('\n');
        }

        // ==================== ВСПОМОГАТЕЛЬНОЕ ====================

        private static void Lamp(StringBuilder sb, string name, bool on)
        {
            string color = on ? GREEN : GRAY;
            string dot = on ? "●" : "○";
            sb.Append(color).Append(BOLD).Append(" ").Append(dot).Append(" ").Append(name.PadRight(6)).Append(RESET);
            sb.Append(" ");
        }

        private static string M(int i) => "M[" + Convert.ToString(i, 8) + "]";

        private static string Hex(ulong v) => "0x" + (v & Word48.Mask48).ToString("X12");
    }
}
