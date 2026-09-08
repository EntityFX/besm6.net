using System;
namespace Besm6.Runtime
{
    /// <summary>
    /// Обработчик экстракодов для загрузчика Dubna (порт dubna/extracode.cpp).
    /// Диспетчер + мелкие экстракоды (E63, E65, E67, E72, E75, E76).
    /// Тяжёлые — в partial-файлах: .E50, .E57, .E64, .E70, .E71.
    /// </summary>
    public sealed partial class ExtracodeHandler
    {
        private readonly MachineCore _machine;
        private readonly Func<long, TapeImage?> _diskByTapeId;
        private readonly Func<int, TapeImage?> _diskByUnit;
        private readonly Func<int, TapeImage?> _drumByUnit;
        private readonly Action<string> _output;
        private readonly Func<string, string> _input;

        // E57: колбэки для монтажа/поиска/отзыва лент.
        private readonly Func<long, int, bool> _mountTape;
        private readonly Func<long, int, bool, bool>? _mountTapeWithMode;
        private readonly Func<long, int> _findTape;
        private readonly Action<long> _releaseTapes;
        private readonly Func<ulong, ulong, bool, uint> _fileSearch;
        private readonly Func<int, uint, bool, uint, int> _fileMount;
        private readonly Action<int, int> _scratchMount;

        private const int M16 = 14; // индекс-регистр 16 = M[14] в нумерации БЭСМ-6

        /// <summary>
        /// E50 067 (DATE*): реальное системное время или фиксированная дата (04/07/2024 23:45:56).
        /// (ref/machine.h:77 <c>entropy_flag{}</c> = false) и в gtest-фикстурах — это
        /// детерминированное значение для тестов. CLI (ref/main.cpp:103) явно включает
        /// wall clock (<c>session.enable_entropy()</c>), флаг <c>-r</c> отключает;
        /// в C# это настраивается через <c>Config.UseWallClock</c> / <c>MachineFactory</c>.
        /// </summary>
        public bool UseWallClock { get; set; } = false;

        /// <summary>
        /// Эвристика обнаружения зависания: 500+ вызовов экстракодов без вывода (E64)
        /// исполняет, пока программа не завершится естественно, опираясь только на
        /// предел инструкций (-l). Поэтому детектор можно отключить (--no-hang-detect /
        /// </summary>
        public bool HangDetect { get; set; } = false;

        // Phys_io remap (drum → disk redirection, set via MapDrumToDisk).
        private int _mappedDrum = -1;
        private int _physIoDiskUnit = -1;
        private TapeImage? _physIoDisk = null;

        /// <summary>
        /// Настроить перенаправление барабана на диск (phys_io).
        /// Порт Machine::map_drum_to_disk.
        /// </summary>
        public void MapDrumToDisk(int drum, int diskUnit, TapeImage disk)
        {
            _mappedDrum = drum;
            _physIoDiskUnit = diskUnit;
            // чтобы phys-io записи шли в копию, а оригинал (MONSYS) остался нетронутым.
            // Иначе MONSYS читает уже изменённые данные и зацикливается в I/O-wait/abort.
            _physIoDisk = new TapeImage(disk.VolumeId, (byte[])disk.Data.Clone(), readOnly: false);
        }

        public ExtracodeHandler(
            MachineCore machine,
            Func<long, TapeImage?> diskByTapeId,
            Func<int, TapeImage?> diskByUnit,
            Func<int, TapeImage?> drumByUnit,
            Action<string>? output = null,
            Func<string, string>? input = null,
            Func<long, int, bool>? mountTape = null,
            Func<long, int>? findTape = null,
            Action<long>? releaseTapes = null,
            Func<long, int, bool, bool>? mountTapeWithMode = null,
            Func<ulong, ulong, bool, uint>? fileSearch = null,
            Func<int, uint, bool, uint, int>? fileMount = null,
            Action<int, int>? scratchMount = null)
        {
            _machine = machine;
            _diskByTapeId = diskByTapeId;
            _diskByUnit = diskByUnit;
            _drumByUnit = drumByUnit;
            _output = output ?? (s => Console.Write(s));
            _input = input ?? (p => { Console.Write(p); return Console.ReadLine() ?? ""; });
            _mountTape = mountTape ?? ((id, u) => false);
            _mountTapeWithMode = mountTapeWithMode;
            _findTape = findTape ?? ((id) => 0);
            _releaseTapes = releaseTapes ?? ((mask) => { });
            _fileSearch = fileSearch ?? ((disc, file, write) => 0);
            _fileMount = fileMount ?? ((unit, offset, write, fileOffset) => 8);
            _scratchMount = scratchMount ?? ((unit, zones) => { });
            Array.Fill(_e64Line, G_SPACE);
        }

        /// <summary>
        /// Точка входа из Processor.ExtracodeDispatch.
        /// </summary>
        // Hang detection: no output (E64) or halt (E74) for too many extracode calls.
        private int _noOutputCount = 0;       // extracode calls since last E64/E74
        private const int NoOutputLimit = 500; // 500 extracode calls without output = hang

        private readonly bool _traceExtracodes = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BESM6_TRACE"));
        private System.IO.StreamWriter? _traceWriter = null;
        private StreamWriter EnsureTraceWriter()
        {
            if (_traceWriter == null)
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "ec_trace.log");
                _traceWriter = new StreamWriter(path, append: false) { AutoFlush = true };
            }
            return _traceWriter;
        }

        public bool Handle(ExtracodeCall call)
        {
            long k = _machine.Cpu.GetK();
            int opcode = (int)call.Code;
            uint aex = call.EffectiveAddress;
            Extracode code = call.Code;

            // print_executive_address + besm6_print_instruction_octal/mnemonics), чтобы можно
            // было напрямую diff'ить с трассой dubna_ref.exe -t.
            //   <K:5oct> <L|R>: <reg:2> <opcode:3> <addr:4> <mnemonic> [= exec-addr]
            if (_traceExtracodes)
            {
                var cpu2 = _machine.Cpu;
                int reg = call.Register;
                uint rawAddr = call.RawAddress;
                bool rFlag = call.IsRightHalf;

                static string Oct(long v, int width) => Convert.ToString(v, 8).PadLeft(width, '0');

                // mnemonic = *NN [addr] [(reg)]  (см. besm6_print_instruction_mnemonics)
                string mnem = "*" + Convert.ToString(opcode, 8);
                if (rawAddr != 0)
                {
                    mnem += " ";
                    if (rawAddr >= 0x7FC0) mnem += "-" + Convert.ToString((rawAddr ^ 0x7FFF) + 1, 8);
                    else mnem += Convert.ToString(rawAddr, 8);
                }
                if (reg != 0)
                {
                    if (rawAddr == 0) mnem += " ";
                    mnem += "(" + Convert.ToString(reg, 8) + ")";
                }

                // исполнительный адрес (см. print_executive_address): = M[reg], если reg != 0
                string execAddr = "";
                if (reg != 0)
                {
                    long mreg = cpu2.GetM(reg) & 0x7FFF;
                    execAddr = " = " + Convert.ToString(mreg, 8);
                }

                EnsureTraceWriter().WriteLine(
                    $"{Oct(k, 5)} {(rFlag ? 'R' : 'L')}: {Oct(reg, 2)} {Oct(opcode, 3)} {Oct(rawAddr, 4)} {mnem}{execAddr}");
            }

            // Hang detection: too many extracode calls without any output.
            if (HangDetect)
            {
                _noOutputCount++;
                if (code == Extracode.E64 || code == Extracode.E74)
                {
                    _noOutputCount = 0; // got output or halt
                }
                else if (_noOutputCount > NoOutputLimit)
                {
                    throw new ProcessorException(
                        $"Hang detected: MONSYS executing {NoOutputLimit}+ extracode calls without producing output. " +
                        $"Last K=0{Convert.ToString(k, 8)}, opcode=0{Convert.ToString(opcode, 8)}. " +
                        "This means MONSYS is in an I/O wait state expecting a compiler " +
                        "(BEMSH/EXFOR/B) or resource that never completes. " +
                        "This is a known limitation: the C++ reference (dubna/) also cannot " +
                        "run ALGOL/FORTRAN/B jobs because the OS kernel is incomplete. " +
                        "See plans/monsys-kernel-support.md for details.");
                }
            }

            switch (code)
            {
                case Extracode.E50: E50(); return true;
                case Extracode.E51: E51(); return true;
                case Extracode.E52: E52(); return true;
                case Extracode.E53: E53(); return true;
                case Extracode.E54: E54(); return true;
                case Extracode.E55: E55(); return true;
                case Extracode.E56: E56(); return true;
                case Extracode.E57: E57(); return true;
                case Extracode.E61: E61(); return true;
                case Extracode.E63: E63(); return true;
                case Extracode.E64: E64(aex); return true;
                case Extracode.E65: E65(); return true;
                case Extracode.E67: E67(); return true;
                case Extracode.E70: E70(); return true;
                case Extracode.E71: E71(); return true;
                case Extracode.E72: E72(); return true;
                case Extracode.E73: return true;
                case Extracode.E74: throw new ProcessorException("");
                case Extracode.E75: E75(); return true;
                case Extracode.E76: E76(); return true;
                case Extracode.E20: return true;
                case Extracode.E21: return true;
                default: return false;
            }
        }

        /// <summary>
        /// Совместимый вход для прямых тестов обработчика, не проходящих через CPU.
        /// Контекст регистра, исходного адреса и половины слова в таком вызове отсутствует.
        /// </summary>
        public bool Handle(int opcode, uint effectiveAddress) =>
            Handle(new ExtracodeCall((Extracode)opcode, effectiveAddress, 0, 0, false));

    }
}
