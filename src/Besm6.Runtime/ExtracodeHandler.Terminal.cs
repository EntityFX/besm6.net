
namespace Besm6.Runtime
{
    /// <summary>
    /// Terminal subsystem: E61 (plotter/display), E70 (disk/drum I/O), E71 (terminal I/O).
    /// </summary>
    public sealed partial class ExtracodeHandler
    {

        // ─── E61: управление дисплеем VT-340 / плоттеры (порт dubna/e61) ────

        private void E61()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);

            if (addr == 0x7FFF) // 077777 octal
            {
                // Вывод на плоттер Watanabe или Tektronix.
                // Адрес начала данных — в младших 15 битах A; тип плоттера — в старших 12 битах.
                var bp = new BytePointer(_machine.Memory, (uint)(cpu.GetA().Value & 0x7FFF));
                switch ((cpu.GetA().Value >> 36) & 0xFFF)
                {
                    case 0:
                        // Watanabe WX4675.
                        for (;;)
                        {
                            byte ch = bp.Get();
                            if (ch == 0) break;
                            _machine.Plotter.WatanabePutCh((char)ch);
                        }
                        break;

                    case 0x300: // 01400 octal
                        // Tektronix.
                        if (bp.WordAddr == 0)
                        {
                            // Начало новой команды.
                        }
                        else
                        {
                            for (;;)
                            {
                                byte ch = bp.Get();
                                if (ch == 0) break;
                                _machine.Plotter.TektronixPutCh((char)ch);
                            }
                        }
                        break;

                    default:
                        throw new ProcessorException(
                            $"Extracode *61 77777: unknown target {Convert.ToString(((int)cpu.GetA().Value >> 36) & 0xFFF, 8)}");
                }
                cpu.SetA(0);
                return;
            }

            // Неизвестный адрес — сброс A.
            cpu.SetA(0);
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
            long ctrl = (execAddr == 0) ? (long)cpu.GetA().Value : (long)_machine.Memory.Read((uint)execAddr).Value;

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
            long word = (long)_machine.Memory.Read((uint)ctlAddr).Value;

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
                    var bp = new BytePointer(_machine.Memory, (uint)a1);
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
                    var bp = new BytePointer(_machine.Memory, (uint)start);
                    for (int i = 0; i < koi7.Length; i++) bp.Put((byte)koi7[i]);
                    while (bp.ByteIndex != 0) bp.Put(0); // дописать нулями до конца слова
                    return;
                }

                default:
                    return;
            }
        }
    }
}
