namespace Besm6.Runtime
{
    public sealed partial class ExtracodeHandler
    {
        // ─── E50: математика + сервисы (fn из M[16]) ─────────────────────────
        // Точный порт Processor::e50 из dubna/e50.cpp.
        // case 0-7 — математика (A = input = output).
        // case 014/017 — parse/format (требуют записи Y + байтовый I/O, не в C# API).
        // Остальные case — сервисы ОС Дубна (no-op / DATE* / фиксированные ответы).

        private void E50()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            ulong arg = cpu.GetA().Value;
            switch (addr)
            {
                case 0: cpu.SetA(Besm6Math.Sqrt(arg)); break;
                case 1: cpu.SetA(Besm6Math.Sin(arg)); break;
                case 2: cpu.SetA(Besm6Math.Cos(arg)); break;
                case 3: cpu.SetA(Besm6Math.Atan(arg)); break;
                case 4: cpu.SetA(Besm6Math.Asin(arg)); break;
                case 5: cpu.SetA(Besm6Math.Log(arg)); break;
                case 6: cpu.SetA(Besm6Math.Exp(arg)); break;
                case 7: cpu.SetA(Besm6Math.Floor(arg)); break;

                case 12: E50Parse(); break;   // 014 oct
                case 15: E50Format(); break;  // 017 oct

                case 54: // 066 oct — смена страницы плоттера.
                    _machine.Plotter.ChangePage();
                    cpu.SetA(0);
                    break;

                case 55:  // 067 oct — DATE*, ОС Дубна.
                {
                    //   если machine.is_entropy_enabled() — реальное текущее местное
                    //   время (localtime), иначе фиксированная дата для тестов.
                    // только -r отключает её (ref/main.cpp:193-196).
                    // Раскладка union E50_Date_Time (ref/extracode.h):
                    //   decisec  b0-3,  sec_lo  b4-7,  sec_hi  b8-11, min_lo  b12-15,
                    //   min_hi   b16-19, hour_lo b20-23, hour_hi b24-25 (2 бита),
                    //   year_lo  b26-29, year_hi b30-33, month_lo b34-37, month_hi b38-41,
                    //   day_lo   b42-45, day_hi  b46-47 (2 бита)
                    ulong word;
                    if (UseWallClock)
                    {
                        var now = DateTime.Now;
                        word = (ulong)((now.Day / 10) & 0x3) << 46
                            | (ulong)(now.Day % 10) << 42
                            | (ulong)((now.Month / 10) & 0xF) << 38
                            | (ulong)(now.Month % 10) << 34
                            | (ulong)(((now.Year % 100) / 10) & 0xF) << 30
                            | (ulong)((now.Year % 100) % 10) << 26
                            | (ulong)((now.Hour / 10) & 0x3) << 24
                            | (ulong)(now.Hour % 10) << 20
                            | (ulong)((now.Minute / 10) & 0xF) << 16
                            | (ulong)(now.Minute % 10) << 12
                            | (ulong)((now.Second / 10) & 0xF) << 8
                            | (ulong)(now.Second % 10) << 4;
                    }
                    else
                    {
                        //   day_hi=0, day_lo=4   → 04
                        //   month_hi=0, month_lo=7 → July (ИЮЛ)
                        //   year_hi=2, year_lo=4  → 2024
                        //   hour_hi=2, hour_lo=3  → 23
                        //   min_hi=4, min_lo=5    → 45
                        //   sec_hi=5, sec_lo=6    → 56
                        //   decisec=0
                        word = (4UL << 42)                // day_lo=4
                            | (7UL << 34)                 // month_lo=7
                            | (2UL << 30) | (4UL << 26)   // year_hi=2, year_lo=4
                            | (2UL << 24) | (3UL << 20)   // hour_hi=2, hour_lo=3
                            | (4UL << 16) | (5UL << 12)   // min_hi=4, min_lo=5
                            | (5UL << 8)  | (6UL << 4);   // sec_hi=5, sec_lo=6
                    }
                    cpu.SetA(word);
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

                case 137:
                    throw new ProcessorException("Task paused waiting for tape");

                case 28735: cpu.SetA(0); break;         // 070077 oct
                case 28800: cpu.SetA(0x8000UL); break;   // 070200 oct: 0'0010'0000 in dubna/e50.cpp
                case 28808: cpu.SetA(0); break;         // 070210 oct
                case 28812: cpu.SetA(System.Convert.ToUInt64("1234567012345670", 8)); break; // 070214 oct

                default:
                    throw new ProcessorException($"Unimplemented extracode *50 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E51-E56: элементарные функции (порт dubna/extracode.cpp e51..e56) ──
        // Диспетчеризация по M[16] (индексный регистр 16): addr=0 — основная функция.
        // Только *51 поддерживает addr=1 (cos).

        private void E51()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            switch (addr)
            {
                case 0: cpu.SetA(Besm6Math.Sin(cpu.GetA().Value)); return;
                case 1: cpu.SetA(Besm6Math.Cos(cpu.GetA().Value)); return;
                default: throw new ProcessorException($"Unimplemented extracode *51 {Convert.ToString(addr, 8)}");
            }
        }

        private void E52()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0) throw new ProcessorException($"Unimplemented extracode *52 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Cos(cpu.GetA().Value));
        }

        private void E53()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0) throw new ProcessorException($"Unimplemented extracode *53 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Atan(cpu.GetA().Value));
        }

        private void E54()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0) throw new ProcessorException($"Unimplemented extracode *54 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Asin(cpu.GetA().Value));
        }

        private void E55()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0) throw new ProcessorException($"Unimplemented extracode *55 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Log(cpu.GetA().Value));
        }

        private void E56()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0) throw new ProcessorException($"Unimplemented extracode *56 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Exp(cpu.GetA().Value));
        }

    }
}
