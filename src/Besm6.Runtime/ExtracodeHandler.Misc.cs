namespace Besm6.Runtime
{
    public sealed partial class ExtracodeHandler
    {
        // ─── E63: ОС Дубна ───────────────────────────────────────────────────
        //
        // Extracode 063 — «manage time limit» / служебные запросы ОС (порт dubna/extracode.cpp).
        // M[16] (индекс-регистр 14) = подкоманда. Реализованы подкоманды 1, 3, 4.
        //
        // Диагностика (27.08.2026, tests-run + BESM6_TRACE): MONSYS при настройке сессии
        // вызывает серию э63, затем подкоманду 0:
        //     [EC] 063 M16=0765  — имя организации (йоксел)      K=02561
        //     [EC] 063 M16=07    — номер машины                   K=02563
        //     [EC] 063 M16=0502  — адрес процессного дескриптора  K=02567
        //     [EC] 063 M16=00    — НЕ РЕАЛИЗОВАНО                 K=02571  ← сбой
        //
        // Э63(0) не реализован в референс-обработчике (dubna/extracode.cpp:
        // case default → throw) и в этом порте. Если workload, успешно завершающийся
        // в dubna, доходит до Э63(0) в C# — это доказательство РАНЕЕ возникшего
        // архитектурного расхождения (ранее исполненной инструкции или её состояния),
        // а не повод додумывать поведение «наугад»: любое поведение э63(0) здесь —

        private void E63()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            switch (addr)
            {
                case 1: cpu.SetA(206L); return;
                case 3: return;
                case 4: cpu.SetA(206L); return;
                case 7: cpu.SetA(5L << 33); return;
                case 322: cpu.SetA(1024L); return;
                case 324: cpu.SetA(0L); return;  // 504 oct — OS status/no-op
                case 379: cpu.SetA(2048L); return;
                case 381: cpu.SetA(2560L); return;
                case 450: cpu.SetA(0); return;
                case 452: cpu.SetA(1L << 43); return;
                case 496: cpu.SetA(1536L); return;
                case 497: cpu.SetA(1536L); return;
                case 501: cpu.SetA(116888797660524L); return;
                case 502: cpu.SetA(87149724850530L); return;
                case 1024: cpu.SetA(342391L); return;
                case 1536: cpu.SetA(0); return;
                case 1537: cpu.SetA(0); return;
                case 1544: cpu.SetA(0); return;
                case 1545: cpu.SetA(0); return;
                case 2048: cpu.SetA(0); return;
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
                    cpu.SetA(0); return;
                case 322: cpu.SetA(1024L); return;   // 0502
                case 342: cpu.SetA(3072L); return;   // 0526 — адрес таблицы ALLTOISO
                case 368: cpu.SetA(2560L); return;   // 0560
                case 372: cpu.SetA(512L); return;
                case 381: cpu.SetA(4608L); return;
                case 382: cpu.SetA(3584L); return;
                case 496: cpu.SetA(0x800000600L); return;
                case 497: cpu.SetA(2048L); return;
                case 498: cpu.SetA(4096L); return;
                case 500: //0764 Get version of Dubna OS.
                    cpu.SetA(0x82828F5C28F6L); return; //0'4050'1217'2702'4366
                case 502: //0766
                    cpu.SetA(0x4F4320645962L); return;
                case 514: cpu.SetA(233475L); return;
                case 1024: cpu.SetA(0); return;
                case 1536: cpu.SetA(0); return;
                case 1537: cpu.SetA(0); return;
                case 1541: cpu.SetA(0); return;
                case 2048: cpu.SetA(0); return;
                case 2561: cpu.SetA(0); return;
                case 4608: case 4609: case 4610: case 4611:
                case 4612: case 4613: case 4614: case 4615:
                case 4616: case 4617: case 4618: case 4619:
                case 4620: case 4621: case 4622: case 4623:
                    cpu.SetA(0); return;
                default:
                    if (addr >= 448 && addr < 496) // 0700..0757 oct — выключатели пульта
                    {
                        cpu.SetA((ulong)(1L << ((int)(495 - addr))));
                        return;
                    }
                    if (addr >= 3072 && addr < 3072 + 128) // 06000..06000+127 oct — таблица ALLTOISO
                    {
                        cpu.SetA((ulong)CosyCodec.AllToIso[(int)(addr - 3072)]);
                        return;
                    }
                    throw new ProcessorException($"Unimplemented extracode *65 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E67: отладка (jump) ─────────────────────────────────────────────

        private void E67()
        {
            var cpu = _machine.Cpu;
            ulong word = (ulong)_machine.Memory.Read((uint)(cpu.GetM(M16) & 0x7FFF)).Value;
            uint xfer = (uint)(word >> 24) & 0x7FFF;
            bool printInfo = ((word >> 23) & 1) != 0;
            uint mode = (uint)(word >> 20) & 3;
            uint watch = (uint)word & 0x7FFF;
            uint cont = cpu.GetK();

            cpu.ArmDebugWatch(xfer, printInfo, mode, watch, cont);
        }

        // ─── E72: ОС Дубна (страницы памяти) ─────────────────────────────────

        private void E72()
        {
            uint addr = _machine.Cpu.GetM(M16) & 0x7FFF;
            if (addr == 4 || addr >= 8)
                return;

            throw new ProcessorException(
                $"Unimplemented extracode *72 {Convert.ToString(addr, 8)}");
        }

        // ─── E75: запись аккумулятора в память ───────────────────────────────

        private void E75()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            if (addr > 0)
            {
                _machine.Memory.Write((uint)addr, new Word48(cpu.GetA().Value));

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

    }
}
