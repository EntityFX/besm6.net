using System;
using System.IO;
using System.Text;
using Besm6.Core;
using Besm6.Asm;
using Besm6.Loader;

namespace Besm6.Tui
{
    /// <summary>
    /// Панель (dashboard) БЭСМ-6: регистры, лампочки-индикаторы и окно памяти
    /// постоянно отображаются на экране и перерисовываются при каждом действии.
    /// Это не CLI-лог — это интерактивная панель машинного состояния.
    /// </summary>
    public sealed class TuiApp
    {
        private readonly Config _config;
        private string? _jobFile;
        private MachineCore? _machine;
        private DubnaLoader? _loader;

        private bool _running;        // машина «запущена» (RUN-лампа)
        private bool _halted;         // выполнена команда СТОП
        private long _instrCount;     // счётчик инструкций (за сессию)
        private string _status = "ready";  // строка статуса/сообщения
        private int _memBase = 0;     // базовый адрес окна памяти

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

        public TuiApp(Config config, string? jobFile)
        {
            _config = config;
            _jobFile = jobFile;
        }

        public int Run()
        {
            if (_jobFile != null && !File.Exists(_jobFile))
            {
                _status = "not found: " + _jobFile;
                Draw();
                Console.Write("\r> ");
                return 1;
            }

            Draw();

            while (true)
            {
                Console.Write("\r" + GRAY + "  > " + RESET);
                var line = Console.ReadLine() ?? "";
                line = line.Trim();
                if (line.Length == 0) continue;

                int spaceIdx = line.IndexOf(' ');
                string cmd = (spaceIdx >= 0 ? line.Substring(0, spaceIdx) : line).ToLowerInvariant();
                string arg = spaceIdx >= 0 ? line.Substring(spaceIdx + 1).Trim() : "";

                if (cmd == "quit" || cmd == "exit" || cmd == "q")
                {
                    Console.WriteLine("\nBESM-6 TUI exited.");
                    return 0;
                }

                bool handled = Handle(cmd, arg);
                if (!handled) _status = "Unknown command: " + cmd + "  (help)";
                Draw();
            }
        }

        // Возвращает true если команда распознана.
        private bool Handle(string cmd, string arg)
        {
            switch (cmd)
            {
                case "help": case "h":
                    _status = "load file | run | step | mem <hex> | asm <instr> | write <addr> <val> | reset | quit";
                    return true;

                case "load":
                    if (arg.Length == 0) { _status = "load <file.dub>"; return true; }
                    if (!File.Exists(arg)) { _status = "not found: " + arg; return true; }
                    _jobFile = arg;
                    InitMachine();
                    _running = false; _halted = false; _instrCount = 0;
                    try
                    {
                        long start = _loader!.LoadScript(arg);
                        _memBase = _loader.LoadedBase;
                        _status = "loaded @" + start.ToString("X4") + "  — " + arg;
                    }
                    catch (Exception ex) { _status = "load error: " + ex.Message; }
                    return true;

                case "run":
                    if (_machine == null || _jobFile == null) { _status = "load a file first"; return true; }
                    _running = true;
                    var result = _loader!.RunLoaded();
                    _running = false;
                    if (result.Success) { _halted = true; _status = "HALTED by STOP @" + result.Pc.ToString("X4"); }
                    else _status = "stopped: " + (result.ErrorMessage ?? "limit");
                    _instrCount += result.Instructions;
                    return true;

                case "step": case "cont": case "continue": case "c": case "s":
                    if (_machine == null) { _status = "load a file first"; return true; }
                    StepOne();
                    return true;

                case "mem": case "m":
                    if (_machine == null) { _status = "load a file first"; return true; }
                    if (arg.Length > 0 && int.TryParse(arg, System.Globalization.NumberStyles.HexNumber, null, out int a))
                        _memBase = a & 0x7FFF;
                    else if (arg.Length > 0) _status = "bad hex address: " + arg;
                    else _status = "memory window @ " + _memBase.ToString("X4");
                    return true;

                case "asm":
                    if (arg.Length == 0) { _status = "asm <instruction>"; return true; }
                    try
                    {
                        ulong w = Assembler.Asm(arg);
                        _status = "asm: " + arg + " = 0x" + w.ToString("X12");
                    }
                    catch (Exception ex) { _status = "asm error: " + ex.Message; }
                    return true;

                case "write": case "w":
                    if (_machine == null) { _status = "load a file first"; return true; }
                    var parts = arg.Split(' ', 2);
                    if (parts.Length < 2) { _status = "write <addr> <value>"; return true; }
                    if (!int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out int wa))
                    { _status = "bad address"; return true; }
                    if (!TryParseWord(parts[1], out long wv)) { _status = "bad value"; return true; }
                    _machine.Memory.Write((uint)(wa & 0x7FFF), new Word48((ulong)wv));
                    _memBase = (wa & ~0xF) & 0x7FFF;
                    _status = "wrote 0x" + wv.ToString("X12") + " @ " + wa.ToString("X4");
                    return true;

                case "reset": case "rst":
                    if (_machine == null) { _status = "load a file first"; return true; }
                    _machine.Cpu.Reset();
                    _running = false; _halted = false;
                    _status = "reset";
                    return true;

                default: return false;
            }
        }

        private void InitMachine()
        {
            _machine = MachineFactory.CreateMachine(_config);
            _loader = MachineFactory.CreateLoader(_config, _machine);
        }

        private void StepOne()
        {
            var cpu = _machine!.Cpu;
            long before = cpu.PC;
            var word = _machine.Memory.Read((uint)((int)before & 0x7FFF));
            var dis = Disassembler.DisasmWord((long)word.Value);
            bool stopped = cpu.Step();
            _instrCount++;
            if (stopped) { _halted = true; _running = false; _status = "HALTED by STOP @" + before.ToString("X4"); }
            else _status = before.ToString("X4") + "  " + dis;
        }

        // ==================== РЕНДЕР ПАНЕЛИ ====================

        private void Draw()
        {
            var sb = new StringBuilder();
            sb.Append(CLR);
            Header(sb);
            IndicatorLamps(sb);
            Registers(sb);
            MemoryWindow(sb);
            StatusLine(sb);
            Console.Write(sb.ToString());
        }

        private void Header(StringBuilder sb)
        {
            sb.Append(BOLD).Append(CYAN).Append("  ╔═ BESM-6 · Машина").Append(RESET);
            sb.Append("  ").Append(GRAY).Append("════════════════════════════════════════════════════").Append(RESET).Append('\n');
            string loaded = _jobFile != null ? _jobFile : "(no file)";
            sb.Append(GRAY).Append("  ").Append(RESET).Append(DIM).Append("file: ").Append(RESET).Append(loaded).Append('\n');
            sb.Append('\n');
        }

        private void IndicatorLamps(StringBuilder sb)
        {
            sb.Append(GRAY).Append("  ЛАМПЫ-ИНДИКАТОРЫ:").Append(RESET).Append('\n');
            sb.Append("   ");
            if (_machine == null)
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
                var cpu = _machine.Cpu;
                Lamp(sb, "RUN", _running);
                Lamp(sb, "HALT", _halted);
                Lamp(sb, "ADD", cpu.AluMode == "ADD");
                Lamp(sb, "MUL", cpu.AluMode == "MUL");
                Lamp(sb, "LOG", cpu.AluMode == "LOG");
                Lamp(sb, "RIGHT", cpu.RightInstruction);
            }
            sb.Append('\n');
        }

        private void Registers(StringBuilder sb)
        {
            sb.Append(BOLD).Append(GRAY).Append("  ─── РЕГИСТРЫ ─────────────────────────────────────────").Append(RESET).Append('\n');

            if (_machine == null)
            {
                sb.Append(DIM).Append("   (загрузите программу: load file.dub)").Append(RESET).Append('\n');
                sb.Append('\n');
                return;
            }
            var cpu = _machine.Cpu;

            void Row(string a, string b)
            {
                sb.Append("   ").Append(a.PadRight(24)).Append(GRAY).Append("│").Append(RESET);
                sb.Append("  ").Append(b).Append('\n');
            }

            Row("PC    " + Hex(cpu.PC), "ACC   " + Hex(cpu.Acc.Value));
            Row("RMR   " + Hex(cpu.Rmr.Value), "MOD   " + Hex((ulong)cpu.Mod));
            Row("RAU   " + Hex(cpu.Rau), "MODE  " + cpu.AluMode);
            Row("STEPS " + _instrCount.ToString(), "STATE " + (_halted ? "HALTED" : (_running ? "RUNNING" : "IDLE")));
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

        private string M(int i) => "M[" + i.ToString("X") + "]";

        private void MemoryWindow(StringBuilder sb)
        {
            sb.Append(BOLD).Append(GRAY).Append("  ─── ПАМЯТЬ (окно 16 слов) ──────────────────────────────").Append(RESET).Append('\n');

            if (_machine == null)
            {
                sb.Append(DIM).Append("   (нет памяти)").Append(RESET).Append('\n');
                sb.Append('\n');
                return;
            }
            var cpu = _machine.Cpu;
            int pc = (int)(cpu.PC & 0x7FFF);
            var mem = _machine.Memory;

            for (int i = 0; i < 16; i++)
            {
                int a = (_memBase + i) & 0x7FFF;
                if (a >= mem.Size) break;
                var w = mem.Read((uint)a);
                bool isPc = (a == pc);
                if (isPc) sb.Append(YELLOW);

                sb.Append("   ").Append(a.ToString("X4")).Append(GRAY).Append(" │ ").Append(RESET);
                sb.Append(w.Value.ToString("X12")).Append(GRAY).Append(" │ ").Append(RESET);
                sb.Append(Disassembler.DisasmHalf((long)(w.Value >> 24))).Append(" ");
                sb.Append(Disassembler.DisasmHalf((long)(w.Value & 0xFFFFFFL)));
                if (isPc)
                {
                    sb.Append(BOLD).Append(GREEN).Append("  ◄ PC").Append(RESET);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        private void StatusLine(StringBuilder sb)
        {
            bool bad = _status.StartsWith("stopped") || _status.StartsWith("not found") ||
                       _status.Contains("error") || _status.StartsWith("Unknown") ||
                       _status.StartsWith("bad") || _status.StartsWith("load a file") ||
                       _status.StartsWith("write <") || _status.StartsWith("asm <");
            string color = bad ? RED : GREEN;
            sb.Append(GRAY).Append("  ─────────────────────────────────────────────────────────────").Append(RESET).Append('\n');
            sb.Append(BOLD).Append(color).Append("  СТАТУС: " + _status).Append(RESET).Append('\n');
            sb.Append(GRAY).Append("  [load | run | step | mem | asm | write | reset | help | quit]").Append(RESET).Append('\n');
        }

        // ==================== ВСПОМОГАТЕЛЬНОЕ ====================

        private void Lamp(StringBuilder sb, string name, bool on)
        {
            string color = on ? GREEN : GRAY;
            string dot = on ? "●" : "○";
            sb.Append(color).Append(BOLD).Append(" ").Append(dot).Append(" ").Append(name.PadRight(6)).Append(RESET);
            sb.Append(" ");
        }

        private static string Hex(ulong v) => "0x" + (v & Word48.Mask48).ToString("X12");

        private bool TryParseWord(string s, out long val)
        {
            val = 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out val);
            // все цифры 0-7 → восьмеричное
            bool allOct = true;
            foreach (char c in s) { if (c < '0' || c > '7') { allOct = false; break; } }
            if (allOct)
            {
                long oct = 0;
                foreach (char c in s) oct = (oct << 3) | (long)(c - '0');
                val = oct;
                return true;
            }
            return long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out val);
        }
    }
}