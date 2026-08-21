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
        private long _lastPc = -1;
        private int _repeatCount = 0;

        public bool Handle(int opcode, long aex)
        {
            long pc = _machine.Cpu.GetPc();
            if (pc == _lastPc) _repeatCount++;
            else { _repeatCount = 0; _lastPc = pc; }
            if (_repeatCount > 20)
            {
                Console.Error.WriteLine($"[TRACE] extracode={opcode} aex=0{aex:X} PC=0{pc:X} repeat={_repeatCount}");
                _repeatCount = 0;
            }

            Extracode code = (Extracode)opcode;
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

                // ВНИМАНИЕ: в C++ кейсы — ВОСЬМЕРОИЧНЫЕ (014, 067, ...).
                // В C# литералы десятичные, поэтому записаны DECIMAL + комментарий oct.

                case 12: E50Parse(); break;   // 014 oct
                case 15: E50Format(); break;  // 017 oct

                // 066 oct (54 dec) — plotter change page, ACC = 0.
                case 54: cpu.SetAcc(0); break;

                // 067 oct (55 dec) — DATE*: вернуть дату/время (фиксированное).
                case 55:
                {
                    // Фиксированное 04.07.2024 23:45:56 (как в C++ при отключённой энтропии).
                    long d = 0;
                    d |= 4L << 4;    // day_lo = 4
                    d |= 7L << 12;   // month_lo = 7
                    d |= 2L << 16;   // year_hi = 2
                    d |= 4L << 20;   // year_lo = 4
                    d |= 2L << 24;   // hour_hi = 2
                    d |= 3L << 28;   // hour_lo = 3
                    d |= 4L << 32;   // min_hi = 4
                    d |= 5L << 36;   // min_lo = 5
                    d |= 5L << 40;   // sec_hi = 5
                    d |= 6L << 44;   // sec_lo = 6
                    cpu.SetAcc(d & 0xFFFFFFFFFFFFL);
                    break;
                }

                // no-op кейсы (в C++ просто break). Значения — DECIMAL эквиваленты oct.
                case 52:    // 064 oct — print job name
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

                // 070077 oct (28735 dec) — CPU time = 0.
                case 28735: cpu.SetAcc(0); break;

                // 070200 oct (28800 dec) — ACC = 0'0010'0000 = 8192.
                case 28800: cpu.SetAcc(8192L); break;

                // 070210 oct (28808 dec) — ACC = 0.
                case 28808: cpu.SetAcc(0); break;

                // 070214 oct (28812 dec) — ACC = 0'1234'5670'1234'5670.
                case 28812: cpu.SetAcc(System.Convert.ToInt64("1234567012345670", 8)); break;

                default:
                    throw new ProcessorException($"Unimplemented extracode *50 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E57: монтаж лент ─────────────────────────────────────────────────

        private void E57()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            if (addr == 1)
            {
                long tapeId = cpu.GetAcc() & 0x7FFF;
                int unit = (int)(cpu.GetM(0) & 0x7);
                _mountTape(tapeId, unit);
            }
            else if (addr == 2)
            {
                long tapeId = cpu.GetAcc() & 0x7FFF;
                cpu.SetAcc((long)_findTape(tapeId));
            }
            else if (addr == 3)
            {
                _releaseTapes(cpu.GetAcc() & 0x7FFF);
            }
        }

        // ─── E64: вывод текста (полный протокол, см. ExtracodeHandler.E64.cs) ───

        private void E64(long aex)
        {
            // C++: ctl_addr = core.M[016] (= M[14]). Не из инструкции, а из M-регистра.
            int addr = (int)(_machine.Cpu.GetM(M16) & 0x7FFF);
            E64Full(addr);
        }

        // ─── E70: disk/drum I/O ──────────────────────────────────────────────
        // Точный порт Processor::e70 из dubna/extracode.cpp + dubna/extracode.h.
        //
        // Control word: в ACC (если M[16]==0) или в памяти по M[16].
        //
        // DISK (unit 30..67 oct):
        //   bits 11-0:  zone (12)
        //   bits 17-12: unit (6)
        //   bits 34-30: page (5) — memory page
        //   bit  39:    read_op (1=Read, 0=Write)
        //   bit  40:    seek (speculative, no data transfer)
        //
        // DRUM (unit 0..27 oct):
        //   bits  4-0:  tract (5)
        //   bits  7-6:  sector (2)
        //   bits 17-12: unit (6)
        //   bits 25-24: paragraph (2)
        //   bits 34-30: page (5)
        //   bit  35:    raw_sect
        //   bit  38:    phys_io (redirect to mapped disk)
        //   bit  39:    read_op
        //   bit  47:    sect_io (1=sector, 0=full tract)
        //
        // Memory address: page * 1024 (+ paragraph * 256 for sect_io).

        private void E70()
        {
            var cpu = _machine.Cpu;

            // Control word: в ACC если exec addr == 0, иначе в памяти.
            long execAddr = cpu.GetM(M16) & 0x7FFF;
            long ctrl = (execAddr == 0) ? cpu.GetAcc() : _machine.Memory.Read((int)execAddr).Value;

            bool isRead = (ctrl & (1L << 39)) != 0;
            int unit = (int)((ctrl >> 12) & 0x3F);
            int page = (int)((ctrl >> 30) & 0x1F);

            if (unit >= 24 && unit < 56)   // 030..067 oct (BESM-6 disk units)
            {
                // ── Disk I/O ──
                if ((ctrl & (1L << 40)) != 0) return; // seek: no data transfer

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
                // ── Drum I/O ──
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
                    tract = (raw >> 2) & 31;   // 037 oct
                }

                int thisDrum = unit & 31;      // 037 oct

                // Phys_io: перенаправить на mapped disk.
                if (physIo && _mappedDrum >= 0 && thisDrum >= _mappedDrum)
                {
                    if (_physIoDisk != null)
                    {
                        int diskZone = tract + (thisDrum - _mappedDrum) * 32;   // 040 oct
                        if (!sectIo)
                        {
                            if (isRead)
                                _physIoDisk.ReadToMemory(_machine.Memory, (uint)diskZone, 0, memAddr, 1024);
                            else
                                _physIoDisk.WriteFromMemory(_machine.Memory, (uint)diskZone, 0, memAddr, 1024);
                        }
                        else
                        {
                            if (isRead)
                                _physIoDisk.ReadToMemory(_machine.Memory, (uint)diskZone, (uint)sector, memAddr, 256);
                            else
                                _physIoDisk.WriteFromMemory(_machine.Memory, (uint)diskZone, (uint)sector, memAddr, 256);
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
