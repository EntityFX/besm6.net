using System;
using Besm6.Core;

namespace Besm6.Loader
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
        private readonly Func<long, int> _findTape;
        private readonly Action<long> _releaseTapes;

        private const int M16 = 14; // индекс-регистр 16 = M[14] в нумерации БЭСМ-6

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
            _physIoDisk = disk;
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
            Action<long>? releaseTapes = null)
        {
            _machine = machine;
            _diskByTapeId = diskByTapeId;
            _diskByUnit = diskByUnit;
            _drumByUnit = drumByUnit;
            _output = output ?? (s => Console.Write(s));
            _input = input ?? (p => { Console.Write(p); return Console.ReadLine() ?? ""; });
            _mountTape = mountTape ?? ((id, u) => false);
            _findTape = findTape ?? ((id) => 0);
            _releaseTapes = releaseTapes ?? ((mask) => { });
        }

        /// <summary>
        /// Точка входа из Processor.ExtracodeHandler.
        /// </summary>
        // Hang detection: no output (E64) or halt (E74) for too many extracode calls.
        private long _lastPc = -1;
        private int _repeatCount = 0;
        private int _noOutputCount = 0;       // extracode calls since last E64/E74
        private const int NoOutputLimit = 500; // 500 extracode calls without output = hang
        private int _noOutputTotalInstr = 0;  // total instructions since last output (approx)

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

        public bool Handle(int opcode, long aex)
        {
            long pc = _machine.Cpu.GetPc();
            if (pc == _lastPc) _repeatCount++;
            else { _repeatCount = 0; _lastPc = pc; }
            if (_repeatCount > 20)
            {
                var cpu = _machine.Cpu;
                long m16val = cpu.GetM(M16) & 0x7FFF;
                long acc = cpu.GetAcc();
                Console.Error.WriteLine(
                    $"[TRACE] extracode={opcode} aex=0{aex:X} PC=0{pc:X} repeat={_repeatCount} " +
                    $"M16=0{m16val:X}({Convert.ToString(m16val, 8)}) ACC=0{acc:X}");
            }

            Extracode code = (Extracode)opcode;

            // Детальная трассировка (ESIM_TRACE=1)
            if (_traceExtracodes)
            {
                var cpu2 = _machine.Cpu;
                long m16 = cpu2.GetM(M16) & 0x7FFF;
                long acc2 = cpu2.GetAcc();
                EnsureTraceWriter().WriteLine($"[EC] {opcode} (0{Convert.ToString(opcode,8)}) aex=0{aex:X} M16=0{Convert.ToString(m16,8)} ACC=0{acc2:X8} PC=0{Convert.ToString(pc,8)}");
            }

            // Hang detection: too many extracode calls without any output.
            _noOutputCount++;
            if (code == Extracode.E64 || code == Extracode.E74)
            {
                _noOutputCount = 0; // got output or halt
            }
            else if (_noOutputCount > NoOutputLimit)
            {
                throw new ProcessorException(
                    $"Hang detected: MONSYS executing {NoOutputLimit}+ extracode calls without producing output. " +
                    $"Last PC=0{Convert.ToString(pc, 8)}, opcode=0{Convert.ToString(opcode, 8)}. " +
                    "This means MONSYS is in an I/O wait state expecting a compiler " +
                    "(BEMSH/EXFOR/B) or resource that never completes. " +
                    "This is a known limitation: the C++ reference (dubna/) also cannot " +
                    "run ALGOL/FORTRAN/B jobs because the OS kernel is incomplete. " +
                    "See plans/monsys-kernel-support.md for details.");
            }

            switch (code)
            {
                case Extracode.E50: E50(); return true;
                case Extracode.E51: _machine.Cpu.SetAcc(Besm6Math.Sin(_machine.Cpu.GetAcc())); return true;
                case Extracode.E52: _machine.Cpu.SetAcc(Besm6Math.Cos(_machine.Cpu.GetAcc())); return true;
                case Extracode.E53: _machine.Cpu.SetAcc(Besm6Math.Atan(_machine.Cpu.GetAcc())); return true;
                case Extracode.E54: _machine.Cpu.SetAcc(Besm6Math.Asin(_machine.Cpu.GetAcc())); return true;
                case Extracode.E55: _machine.Cpu.SetAcc(Besm6Math.Log(_machine.Cpu.GetAcc())); return true;
                case Extracode.E56: _machine.Cpu.SetAcc(Besm6Math.Exp(_machine.Cpu.GetAcc())); return true;
                case Extracode.E57: E57(); return true;
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
                case Extracode.E20: return true;  // 0200 oct — no-op (C++: reserved)
                case Extracode.E21: return true;  // 0210 oct — lock/release semaphores (C++: TODO/no-op)
                default: return false;
            }
        }

        // ─── E63: ОС Дубна ───────────────────────────────────────────────────

        private void E63()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            switch (addr)
            {
                case 1: cpu.SetAcc(206L); return;
                case 3: return;
                case 4: cpu.SetAcc(206L); return;
                case 7: cpu.SetAcc(5L << 33); return;
                case 322: cpu.SetAcc(1024L); return;
                case 324: cpu.SetAcc(0L); return;  // 504 oct — OS status/no-op
                case 379: cpu.SetAcc(2048L); return;
                case 381: cpu.SetAcc(2560L); return;
                case 450: cpu.SetAcc(0); return;
                case 452: cpu.SetAcc(1L << 43); return;
                case 496: cpu.SetAcc(1536L); return;
                case 497: cpu.SetAcc(1536L); return;
                case 501: cpu.SetAcc(116888797660524L); return;
                case 502: cpu.SetAcc(87149724850530L); return;
                case 1024: cpu.SetAcc(342391L); return;
                case 1536: cpu.SetAcc(0); return;
                case 1537: cpu.SetAcc(0); return;
                case 1544: cpu.SetAcc(0); return;
                case 1545: cpu.SetAcc(0); return;
                case 2048: cpu.SetAcc(0); return;
                case 12273: return;  // 27761 oct = 12273 dec (bemsh/madlen) — no-op
                default:
                    throw new ProcessorException($"Unimplemented extracode *63 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E65: выключатели пульта ─────────────────────────────────────────

        private void E65()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            switch (addr)
            {
                case 1: case 2: case 3: case 4: case 5: case 6: case 7:
                    cpu.SetAcc(0); return;
                case 322: cpu.SetAcc(1024L); return;
                case 346: cpu.SetAcc(3072L); return;
                case 368: cpu.SetAcc(2560L); return;
                case 372: cpu.SetAcc(512L); return;
                case 381: cpu.SetAcc(4608L); return;
                case 382: cpu.SetAcc(3584L); return;
                case 496: cpu.SetAcc(34359739904L); return;
                case 497: cpu.SetAcc(2048L); return;
                case 498: cpu.SetAcc(4096L); return;
                case 500: cpu.SetAcc(143497262541046L); return;
                case 502: cpu.SetAcc(87149724850530L); return;
                case 514: cpu.SetAcc(233475L); return;
                case 1024: cpu.SetAcc(0); return;
                case 1536: cpu.SetAcc(0); return;
                case 1537: cpu.SetAcc(0); return;
                case 1541: cpu.SetAcc(0); return;
                case 2048: cpu.SetAcc(0); return;
                case 2561: cpu.SetAcc(0); return;
                case 4608: case 4609: case 4610: case 4611:
                case 4612: case 4613: case 4614: case 4615:
                case 4616: case 4617: case 4618: case 4619:
                case 4620: case 4621: case 4622: case 4623:
                    cpu.SetAcc(0); return;
                default:
                    if (addr >= 448 && addr < 496)
                    {
                        cpu.SetAcc(1L << ((int)(487 - addr)));
                        return;
                    }
                    throw new ProcessorException($"Unimplemented extracode *65 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E67: отладка (jump) ─────────────────────────────────────────────

        private void E67()
        {
            var cpu = _machine.Cpu;
            long word = _machine.Memory.Read((int)(cpu.GetM(M16) & 0x7FFF)).Value;
            cpu.SetPc((word >> 24) & 0x7FFF);
        }

        // ─── E72: ОС Дубна (страницы памяти) ─────────────────────────────────

        private void E72()
        {
            // All E72 variants are OK for our purposes.
        }

        // ─── E75: запись аккумулятора в память ───────────────────────────────

        private void E75()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            // Exact port of C++ e75(): if (addr > 0) mem_store + intercept check.
            if (addr > 0)
            {
                _machine.Memory.Write((int)addr, new Word48(cpu.GetAcc()));

                // addr == 020 oct (16 dec) → enable intercept for overflow/div-zero.
                if (addr == 16)
                    cpu.InterceptCount = 1;
            }
        }

        // ─── E76: вызов рутин в режиме ядра ──────────────────────────────────

        private void E76()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            if (addr == 0 || addr == 1) return;
            if (addr >= 10) return;
            throw new ProcessorException($"Unimplemented extracode *76 {Convert.ToString(addr, 8)}");
        }

        // ─── E50: математика + сервисы (fn из M[16]) ─────────────────────────
        // Точный порт Processor::e50 из dubna/e50.cpp.
        // case 0-7 — математика (ACC = input = output).
        // case 014/017 — parse/format (требуют записи RMR + байтовый I/O, не в C# API).
        // Остальные case — сервисы ОС Дубна (no-op / DATE* / фиксированные ответы).

        private void E50()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            long arg = cpu.GetAcc();
            switch (addr)
            {
                case 0: cpu.SetAcc(Besm6Math.Sqrt(arg)); break;
                case 1: cpu.SetAcc(Besm6Math.Sin(arg)); break;
                case 2: cpu.SetAcc(Besm6Math.Cos(arg)); break;
                case 3: cpu.SetAcc(Besm6Math.Atan(arg)); break;
                case 4: cpu.SetAcc(Besm6Math.Asin(arg)); break;
                case 5: cpu.SetAcc(Besm6Math.Log(arg)); break;
                case 6: cpu.SetAcc(Besm6Math.Exp(arg)); break;
                case 7: cpu.SetAcc(Besm6Math.Floor(arg)); break;

                case 12: E50Parse(); break;   // 014 oct
                case 15: E50Format(); break;  // 017 oct

                case 54: cpu.SetAcc(0); break;  // 066 oct

                case 55:  // 067 oct — DATE*
                {
                    long d = 0;
                    d |= 4L << 4;
                    d |= 7L << 12;
                    d |= 2L << 16;
                    d |= 4L << 20;
                    d |= 2L << 24;
                    d |= 3L << 28;
                    d |= 4L << 32;
                    d |= 5L << 36;
                    d |= 5L << 40;
                    d |= 6L << 44;
                    cpu.SetAcc(d & 0xFFFFFFFFFFFFL);
                    break;
                }

                case 52:    // 064 oct
                case 57:    // 071 oct
                case 61:    // 075 oct
                case 62:    // 076 oct
                case 66:    // 0102 oct
                case 67:    // 0103 oct
                case 130:   // 0202 oct
                case 131:   // 0203 oct
                case 133:   // 0205 oct
                case 136:   // 0210 oct
                case 137:   // 0211 oct
                case 139:   // 0213 oct
                case 28815: // 070217 oct
                case 28819: // 070223 oct
                case 28830: // 070236 oct
                case 29331: // 071223 oct
                case 29824: // 072200 oct
                case 29833: // 072211 oct
                case 29836: // 072214 oct
                case 29838: // 072216 oct
                case 29840: // 072220 oct
                case 29841: // 072221 oct
                case 29842: // 072222 oct
                case 30848: // 074200 oct
                case 31161: // 074671 oct
                case 31163: // 074673 oct
                case 31872: // 076200 oct
                    break;

                case 28735: cpu.SetAcc(0); break;         // 070077 oct
                case 28800: cpu.SetAcc(8192L); break;     // 070200 oct
                case 28808: cpu.SetAcc(0); break;         // 070210 oct
                case 28812: cpu.SetAcc(System.Convert.ToInt64("1234567012345670", 8)); break; // 070214 oct

                default:
                    throw new ProcessorException($"Unimplemented extracode *50 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E57: монтаж лент / файлов (порт dubna/e57.cpp) ───────────────────

        private void E57()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);

            // Специальные адреса (C++ switch).
            switch (addr)
            {
                case 0:
                    // floor(ACC) — уже в E50 case 14.
                    cpu.SetAcc(Besm6Math.Floor(cpu.GetAcc()));
                    return;
                case 2:
                    // Calcomp plotter — no-op.
                    cpu.SetAcc(0);
                    return;
                case 3:
                    // Delay 1 sec — no-op.
                    return;
                case 5:
                    // Forex unknown — return 0.
                    cpu.SetAcc(0);
                    return;
                case 7:
                    // Task paused waiting for tape.
                    throw new ProcessorException("E57: Task paused waiting for tape");
            }

            if (addr == 32767) // 077777 octal
            {
                // E57 file ops (VOLUME_OPEN / FILE_SEARCH / FILE_OPEN / SCRATCH).
                // Для bemsh.dub достаточно tape ops; file ops пока no-op.
                // MONSYS не вызывает этот путь.
                return;
            }

            if (addr >= 8) // 010 octal
            {
                // E57 tape ops: ASSIGN / RELEASE / FIND (порт e57_tape).
                // C++ octal → decimal: 0100=64, 0200=128, 040=32, 02000=1024, 04000=2048
                const long E57_WRITE   = 64;
                const long E57_READ    = 128;
                const long E57_READY   = 32;
                const long E57_ASSIGN  = 1024;
                const long E57_RELEASE = 2048;

                if ((addr & E57_ASSIGN) != 0)
                {
                    // Mount tape: tapeId in ACC, disk unit in M[15 octal] = M[13 decimal].
                    long tapeIdAssign = cpu.GetAcc();
                    int diskUnit = (int)(cpu.GetM(13) & 0x7F);
                    bool ok = _mountTape(tapeIdAssign, diskUnit);
                    if (!ok)
                        throw new ProcessorException($"E57 ASSIGN: cannot mount tape 0x{tapeIdAssign:X} on unit {diskUnit}");
                    cpu.SetAcc((long)diskUnit);
                    return;
                }

                if ((addr & E57_RELEASE) != 0)
                {
                    // Release tapes according to bitmask on accumulator.
                    _releaseTapes(cpu.GetAcc());
                    cpu.SetAcc(0);
                    return;
                }

                // Find mounted tape (by name and number).
                // Return disk number (unit) in ACC.
                long tapeIdFind = cpu.GetAcc();
                int unit = _findTape(tapeIdFind);
                cpu.SetAcc((long)unit);
            }
            else
            {
                // addr == 1 or 4: tape control by Gusev — unsupported.
                throw new ProcessorException($"E57: unimplemented extracode *57 {Convert.ToString((int)addr, 8)}");
            }
        }

        // ─── E64: вывод текста (полный протокол, см. ExtracodeHandler.E64.cs) ───

        private void E64(long aex)
        {
            int addr = (int)(_machine.Cpu.GetM(M16) & 0x7FFF);
            E64Full(addr);
        }

        // ─── E70: disk/drum I/O ──────────────────────────────────────────────

        private void E70()
        {
            var cpu = _machine.Cpu;
            long execAddr = cpu.GetM(M16) & 0x7FFF;
            long ctrl = (execAddr == 0) ? cpu.GetAcc() : _machine.Memory.Read((int)execAddr).Value;

            bool isRead = (ctrl & (1L << 39)) != 0;
            int unit = (int)((ctrl >> 12) & 0x3F);
            int page = (int)((ctrl >> 30) & 0x1F);

            if (unit >= 24 && unit < 56)
            {
                if ((ctrl & (1L << 40)) != 0) return;
                int zone = (int)(ctrl & 0xFFF);
                int memAddr = page << 10;
                TapeImage? disk = _diskByUnit(unit);
                if (disk == null)
                    throw new ProcessorException($"E70: disk unit 0{unit:o} not mounted");
                if (isRead)
                    disk.ReadToMemory(_machine.Memory, (uint)zone, 0, memAddr, 1024);
                else
                    disk.WriteFromMemory(_machine.Memory, (uint)zone, 0, memAddr, 1024);
            }
            else
            {
                int tract = (int)(ctrl & 0x1F);
                int sector = (int)((ctrl >> 6) & 0x3);
                int paragraph = (int)((ctrl >> 24) & 0x3);
                bool physIo = (ctrl & (1L << 38)) != 0;
                bool sectIo = (ctrl & (1L << 47)) != 0;
                bool rawSect = (ctrl & (1L << 35)) != 0;

                int memAddr = page << 10;
                if (sectIo) memAddr += paragraph << 8;

                if (rawSect && sectIo)
                {
                    int raw = (int)(ctrl & 0xFFF);
                    sector = raw & 3;
                    tract = (raw >> 2) & 31;
                }

                int thisDrum = unit & 31;

                if (physIo && _mappedDrum >= 0 && thisDrum >= _mappedDrum)
                {
                    if (_physIoDisk != null)
                    {
                        int diskZone = tract + (thisDrum - _mappedDrum) * 32;
                        if (!sectIo)
                        {
                            if (isRead) _physIoDisk.ReadToMemory(_machine.Memory, (uint)diskZone, 0, memAddr, 1024);
                            else _physIoDisk.WriteFromMemory(_machine.Memory, (uint)diskZone, 0, memAddr, 1024);
                        }
                        else
                        {
                            if (isRead) _physIoDisk.ReadToMemory(_machine.Memory, (uint)diskZone, (uint)sector, memAddr, 256);
                            else _physIoDisk.WriteFromMemory(_machine.Memory, (uint)diskZone, (uint)sector, memAddr, 256);
                        }
                    }
                    return;
                }

                int nwords = sectIo ? 256 : 1024;
                TapeImage? drum = _drumByUnit(thisDrum);
                if (drum == null)
                    throw new ProcessorException($"E70: drum unit 0{thisDrum:o} not available");
                if (isRead)
                    drum.ReadToMemory(_machine.Memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
                else
                    drum.WriteFromMemory(_machine.Memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
            }
        }

        // ─── E71: терминальный I/O ───────────────────────────────────────────

        private void E71()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            if (addr == 0)
            {
                long start = cpu.GetAcc() & 0x7FFF;
                string line = _input("");
                for (int i = 0; i < line.Length; i++)
                    _machine.Memory.Write((int)(start + i), new Word48((long)line[i]));
            }
            else if (addr == 1)
            {
                long start = cpu.GetAcc() & 0x7FFF;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 1024; i++)
                {
                    long w = _machine.Memory.Read((int)(start + i)).Value;
                    if (w >= 32 && w < 127) sb.Append((char)w);
                    else if (w == 0) break;
                }
                _output(sb.ToString());
            }
        }
    }
}