using System.Globalization;
using Besm6.Assembler;

namespace Besm6.Tui
{
    /// <summary>
    /// Контроллер TUI: владеет машиной и загрузчиком, выполняет распознанные
    /// команды и обновляет снимок сессии. Не содержит ANSI-разметки и не выводит
    /// ничего в консоль — вывод делает <see cref="TuiRenderer"/>.
    /// </summary>
    public sealed class TuiController
    {
        private readonly Config _config;
        private MachineCore? _machine;
        private DubnaLoader? _loader;

        /// <summary>Снимок состояния сессии (общий для рендера и цикла).</summary>
        public TuiSessionState State { get; }

        /// <summary>Созданная машина (read-only для рендера) или null.</summary>
        internal MachineCore? Machine => _machine;

        public TuiController(Config config)
        {
            _config = config;
            State = new TuiSessionState();
        }

        /// <summary>Проверяет стартовый .dub-файл и готовит сессию к работе.</summary>
        public void Initialize(string? jobFile)
        {
            if (jobFile != null && !File.Exists(jobFile))
            {
                State.JobFile = jobFile;
                State.Status = "not found: " + jobFile;
                return;
            }

            if (jobFile != null)
                LoadJob(jobFile);
        }

        /// <summary>
        /// Выполняет распознанную команду и обновляет статус сессии.
        /// </summary>
        public void Execute(TuiCommandRequest request)
        {
            switch (request.Command)
            {
                case TuiCommand.Help:
                    State.Status = "load file | run | step | mem <hex> | asm <instr> | write <addr> <val> | reset | quit";
                    return;

                case TuiCommand.Load:
                    LoadCommand(request.Argument);
                    return;

                case TuiCommand.Run:
                    RunCommand();
                    return;

                case TuiCommand.Step:
                    StepCommand();
                    return;

                case TuiCommand.Mem:
                    MemCommand(request.Argument);
                    return;

                case TuiCommand.Asm:
                    AsmCommand(request.Argument);
                    return;

                case TuiCommand.Write:
                    WriteCommand(request.Argument, request.Secondary);
                    return;

                case TuiCommand.Reset:
                    ResetCommand();
                    return;

                default:
                    return;
            }
        }

        private void LoadCommand(string file)
        {
            if (file.Length == 0) { State.Status = "load <file.dub>"; return; }
            if (!File.Exists(file)) { State.Status = "not found: " + file; return; }
            LoadJob(file);
        }

        private void LoadJob(string file)
        {
            State.JobFile = file;
            InitMachine();
            State.Running = false;
            State.Halted = false;
            State.InstructionCount = 0;
            try
            {
                long start = _loader!.LoadScript(file);
                State.MemoryBase = _loader.LoadedBase;
                State.Status = "loaded @" + start.ToString("X4") + "  — " + file;
            }
            catch (Exception ex)
            {
                State.Status = "load error: " + ex.Message;
            }
        }

        private void RunCommand()
        {
            if (_machine == null || State.JobFile == null) { State.Status = "load a file first"; return; }
            State.Running = true;
            var result = _loader!.RunLoaded();
            State.Running = false;
            if (result.Success) { State.Halted = true; State.Status = "HALTED by STOP @" + result.K.ToString("X4"); }
            else State.Status = "stopped: " + (result.ErrorMessage ?? "limit");
            State.InstructionCount += result.Instructions;
        }

        private void StepCommand()
        {
            if (_machine == null) { State.Status = "load a file first"; return; }
            StepOne();
        }

        private void MemCommand(string arg)
        {
            if (_machine == null) { State.Status = "load a file first"; return; }
            if (arg.Length > 0 && int.TryParse(arg, NumberStyles.HexNumber, null, out int a))
                State.MemoryBase = a & 0x7FFF;
            else if (arg.Length > 0) State.Status = "bad hex address: " + arg;
            else State.Status = "memory window @ " + State.MemoryBase.ToString("X4");
        }

        private void AsmCommand(string arg)
        {
            if (arg.Length == 0) { State.Status = "asm <instruction>"; return; }
            try
            {
                ulong w = new AutoDetectingAssembler().AssembleWord(arg);
                State.Status = "asm: " + arg + " = 0x" + w.ToString("X12");
            }
            catch (Exception ex)
            {
                State.Status = "asm error: " + ex.Message;
            }
        }

        private void WriteCommand(string addr, string value)
        {
            if (_machine == null) { State.Status = "load a file first"; return; }
            if (addr.Length == 0 || value.Length == 0) { State.Status = "write <addr> <value>"; return; }
            if (!int.TryParse(addr, NumberStyles.HexNumber, null, out int wa))
            { State.Status = "bad address"; return; }
            if (!TryParseWord(value, out long wv)) { State.Status = "bad value"; return; }
            _machine.Memory.Write((uint)(wa & 0x7FFF), new Word48((ulong)wv));
            State.MemoryBase = (wa & ~0xF) & 0x7FFF;
            State.Status = "wrote 0x" + wv.ToString("X12") + " @ " + wa.ToString("X4");
        }

        private void ResetCommand()
        {
            if (_machine == null) { State.Status = "load a file first"; return; }
            _machine.Cpu.Reset();
            State.Running = false;
            State.Halted = false;
            State.Status = "reset";
        }

        private void InitMachine()
        {
            _machine = MachineFactory.CreateMachine(_config);
            _loader = MachineFactory.CreateLoader(_config, _machine);
            State.HasMachine = true;
        }

        private void StepOne()
        {
            var cpu = _machine!.Cpu;
            long before = cpu.K;
            var word = _machine.Memory.Read((uint)((int)before & 0x7FFF));
            var dis = Disassembler.DisasmWord((long)word.Value);
            bool stopped = cpu.Step();
            State.InstructionCount++;
            if (stopped) { State.Halted = true; State.Running = false; State.Status = "HALTED by STOP @" + before.ToString("X4"); }
            else State.Status = before.ToString("X4") + "  " + dis;
        }

        private static bool TryParseWord(string s, out long val)
        {
            val = 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(s.Substring(2), NumberStyles.HexNumber, null, out val);
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
            return long.TryParse(s, NumberStyles.HexNumber, null, out val);
        }
    }
}
