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
                case 322: cpu.SetAcc(1024L); return;   // 0502
                case 342: cpu.SetAcc(3072L); return;   // 0526 — адрес таблицы ALLTOISO
                case 368: cpu.SetAcc(2560L); return;   // 0560
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
                    if (addr >= 448 && addr < 496) // 0700..0757 oct — выключатели пульта
                    {
                        cpu.SetAcc(1L << ((int)(495 - addr)));
                        return;
                    }
                    if (addr >= 3072 && addr < 3072 + 128) // 06000..06000+127 oct — таблица ALLTOISO
                    {
                        cpu.SetAcc(CosyCodec.AllToIso[(int)(addr - 3072)]);
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

                case 55:  // 067 oct — DATE*, ОС Дубна.
                {
                    // Раскладка union E50_Date_Time (ref/extracode.h):
                    //   decisec  b0-3,  sec_lo  b4-7,  sec_hi  b8-11, min_lo  b12-15,
                    //   min_hi   b16-19, hour_lo b20-23, hour_hi b24-25 (2 бита),
                    //   year_lo  b26-29, year_hi b30-33, month_lo b34-37, month_hi b38-41,
                    //   day_lo   b42-45, day_hi  b46-47 (2 бита)
                    // Фиксированное значение C++: 04/07/24 23:45:56 (0'101C'9234'5560 hex).
                    const long d = (6L << 4) | (5L << 8) | (5L << 12) | (4L << 16)
                                | (3L << 20) | (2L << 24) | (4L << 26) | (2L << 30)
                                | (7L << 34) | (4L << 42);
                    cpu.SetAcc(d);
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

                case 137:   // 0211 oct — C++: throw Exception("Task paused waiting for tape")
                    throw new ProcessorException("Task paused waiting for tape");

                case 28735: cpu.SetAcc(0); break;         // 070077 oct
                case 28800: cpu.SetAcc(4096L); break;     // 070200 oct — C++: ACC = 0'0010'0000 (бит 12)
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

            // Детальная диагностика E57 (BESM6_TRACE).
            if (_traceExtracodes)
            {
                long acc = cpu.GetAcc();
                long m13 = cpu.GetM(13);
                EnsureTraceWriter().WriteLine(
                    $"[E57] addr=0{Convert.ToString(addr, 8)} ACC=0{acc:X} M[13]=0{m13:X}");
            }

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
                    if (_traceExtracodes)
                    {
                        TapeImage? mounted = _diskByUnit(diskUnit);
                        EnsureTraceWriter().WriteLine(
                            $"[E57] ASSIGN tape=0{tapeIdAssign:X} -> unit=0{Convert.ToString(diskUnit, 8)} " +
                            $"mounted_id=0{(mounted?.VolumeId.ToString("X") ?? "null")}");
                    }
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

            // Детальная диагностика E70 (BESM6_TRACE): декодирование слова управления.
            if (_traceExtracodes)
            {
                int zoneF = (int)(ctrl & 0xFFF);
                int seek = (int)((ctrl >> 40) & 1);
                int tract = (int)(ctrl & 0x1F);
                int sector = (int)((ctrl >> 6) & 0x3);
                int paragraph = (int)((ctrl >> 24) & 0x3);
                int rawSect = (int)((ctrl >> 35) & 1);
                int physIo = (int)((ctrl >> 38) & 1);
                int sectIo = (int)((ctrl >> 47) & 1);
                string medium;
                if (unit >= 24 && unit < 56)
                {
                    TapeImage? d = _diskByUnit(unit);
                    medium = d == null ? "DISK(!!not-mounted!!)" : $"DISK(tape=0{d.VolumeId:X})";
                }
                else
                {
                    int thisDrum = unit & 31;
                    medium = physIo == 1 ? $"PHYSIO(drum=0{thisDrum:X},mapped=0{_mappedDrum:X})" : $"DRUM(0{thisDrum:X})";
                }
                EnsureTraceWriter().WriteLine(
                    $"[E70] m16=0{Convert.ToString(execAddr, 8)} cw=0{ctrl:X12} op={(isRead ? "R" : "W")}{(seek == 1 ? "(seek)" : "")} " +
                    $"unit=0{Convert.ToString(unit, 8)} page=0{Convert.ToString(page, 8)} zone=0{Convert.ToString(zoneF, 8)} " +
                    $"tract=0{Convert.ToString(tract, 8)} sect={sector} par=0{Convert.ToString(paragraph, 8)} rawSect={rawSect} " +
                    $"physIo={physIo} sectIo={sectIo} -> {medium}");
            }

            if (unit >= 24 && unit < 56)
            {
                if ((ctrl & (1L << 40)) != 0) return;
                int zone = (int)(ctrl & 0xFFF);
                int memAddr = page << 10;
                TapeImage? disk = _diskByUnit(unit);
                if (disk == null)
                {
                    // Порт C++ disk_mount: если диск не смонтирован — создаём пустой.
                    // MONSYS при загрузке может обращаться к дискам, которые ещё
                    // не были смонтированы через E57 ASSIGN.
                    _mountTape(0, unit);
                    disk = _diskByUnit(unit);
                    if (disk == null)
                        throw new ProcessorException($"E70: disk unit 0{Convert.ToString(unit, 8)} not mounted");
                }
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
                        throw new ProcessorException($"E70: drum unit 0{Convert.ToString(thisDrum, 8)} not available");
                if (isRead)
                    drum.ReadToMemory(_machine.Memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
                else
                    drum.WriteFromMemory(_machine.Memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
            }
        }

        // ─── E71: терминальный I/O и перфоратор (порт Processor::e71 из dubna/) ───
        // Контрольное слово (E64_Pointer) лежит по адресу M[16]:
        //   разряды 47-44 — start_reg, 43-39 — flags, 38-24 — start_addr,
        //   разряды 23-20 — end_reg,   19-15 — (рез.), 14-0  — end_addr.
        // start = (start_addr + M[start_reg]) & 077777, end = (end_addr + M[end_reg]) & 077777.
        // flags: 1 = перфоратор, 4 = вывод на терминал, 6 = ввод с терминала.
        private void E71()
        {
            var cpu = _machine.Cpu;
            int ctlAddr = (int)(cpu.GetM(M16) & 0x7FFF);
            long word = _machine.Memory.Read(ctlAddr).Value;

            int startReg = (int)((word >> 44) & 0xF);
            int flags    = (int)((word >> 39) & 0x1F);
            int startOff = (int)((word >> 24) & 0x7FFF);
            int endReg   = (int)((word >> 20) & 0xF);
            int endOff   = (int)(word & 0x7FFF);

            int start = (startOff + (int)cpu.GetM(startReg)) & 0x7FFF;
            int end   = (endOff + (int)cpu.GetM(endReg)) & 0x7FFF;

            switch (flags)
            {
                case 1: // Перфоратор.
                    if ((end - start + 1) % 24 != 0)
                        throw new ProcessorException("Punched card buffer " + Convert.ToString(start, 8) +
                            "-" + Convert.ToString(end, 8) + " has fractional cards");
                    _machine.Puncher.Punch(start, end);
                    return;

                case 4: // Вывод на терминал (KOI-7 -> Unicode, до NUL или до end).
                {
                    int a1 = start, a2 = end;
                    E64Finish();
                    var bp = new BytePointer(_machine.Memory, a1);
                    byte c = 1;
                    var sb = new System.Text.StringBuilder();
                    while (c != 0)
                    {
                        if (a2 != 0 && a1 > a2) break;
                        for (int i = 0; c != 0 && i < 6; i++)
                        {
                            c = bp.Get();
                            if (c == 0) break;
                            sb.Append(CosyCodec.Koi7ToUnicode(c));
                            a1++;
                        }
                    }
                    _output(sb.ToString() + "\n");
                    return;
                }

                case 6: // Ввод с терминала (строка -> KOI-7 в память).
                {
                    int endOrMax = end != 0 ? end : 0x7FFF;
                    int buflen = (endOrMax - start + 1) * 6;
                    E64Finish();
                    _output("-\r"); // стандартный промпт
                    string inp = _input("");
                    string koi7 = CosyCodec.Utf8ToKoi7(inp, buflen);
                    if (koi7.Length < buflen) koi7 += '\0'; // завершающий нулевой байт, если влезает
                    var bp = new BytePointer(_machine.Memory, start);
                    for (int i = 0; i < koi7.Length; i++) bp.Put((byte)koi7[i]);
                    while (bp.ByteIndex != 0) bp.Put(0); // дописать нулями до конца слова
                    return;
                }

                default:
                    return; // в C++ неизвестный флаг — бездействие
            }
        }
    }
}
