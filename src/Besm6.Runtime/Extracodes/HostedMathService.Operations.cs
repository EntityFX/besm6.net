using Besm6.Architecture.Numerics;
using Besm6.Processor.Cpu;
using Besm6.Runtime.Extracodes;

namespace Besm6.Runtime.Extracodes
{
    internal sealed partial class HostedMathService
    {
        // ─── E50: математика + сервисы (fn из M[16]) ─────────────────────────
        // Точный порт Processor::e50 из dubna/e50.cpp.
        // case 0-7 — математика (A = input = output).
        // case 014/017 — parse/format (требуют записи Y + байтовый I/O, не в C# API).
        // Остальные case — сервисы ОС Дубна (no-op / DATE* / фиксированные ответы).
        internal void E50()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16) & 0x7FFF;
            ulong arg = cpu.A.Value;
            switch (addr)
            {
                case 0:
                    cpu.SetA(Besm6Math.Sqrt(arg));
                    break;
                case 1:
                    cpu.SetA(Besm6Math.Sin(arg));
                    break;
                case 2:
                    cpu.SetA(Besm6Math.Cos(arg));
                    break;
                case 3:
                    cpu.SetA(Besm6Math.Atan(arg));
                    break;
                case 4:
                    cpu.SetA(Besm6Math.Asin(arg));
                    break;
                case 5:
                    cpu.SetA(Besm6Math.Log(arg));
                    break;
                case 6:
                    cpu.SetA(Besm6Math.Exp(arg));
                    break;
                case 7:
                    cpu.SetA(Besm6Math.Floor(arg));
                    break;
                case 12:
                    E50Parse();
                    break; // 014 oct
                case 15:
                    E50Format();
                    break; // 017 oct
                case 54: // 066 oct — смена страницы плоттера.
                    _plotter.ChangePage();
                    cpu.SetA(0);
                    break;
                case 55:
                    cpu.SetA(_clock.DateWord());
                    break;
                case 52: // 064 oct
                case 57: // 071 oct
                case 61: // 075 oct
                case 62: // 076 oct
                case 66: // 0102 oct
                case 67: // 0103 oct
                case 130: // 0202 oct
                case 131: // 0203 oct
                case 133: // 0205 oct
                case 136: // 0210 oct
                case 139: // 0213 oct
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
                case 28735: // 070077 oct: CTIME commercial seconds.
                    cpu.SetA(Word48.FromDouble(_clock.GuestTimeSeconds?.Invoke() ?? 0).Value);
                    break;
                case 28800:
                    cpu.SetA(0x8000UL);
                    break; // 070200 oct: 0'0010'0000 in dubna/e50.cpp
                case 28808: // 070210 oct: CTIME CPU seconds.
                    cpu.SetA(Word48.FromDouble(_clock.GuestTimeSeconds?.Invoke() ?? 0).Value);
                    break;
                case 28812:
                    cpu.SetA(System.Convert.ToUInt64("1234567012345670", 8));
                    break; // 070214 oct
                default:
                    throw new ProcessorException($"Unimplemented extracode *50 {Convert.ToString(addr, 8)}");
            }
        }

        // ─── E51-E56: элементарные функции (порт dubna/extracode.cpp e51..e56) ──
        // Диспетчеризация по M[16] (индексный регистр 16): addr=0 — основная функция.
        // Только *51 поддерживает addr=1 (cos).
        internal void E51()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            switch (addr)
            {
                case 0:
                    cpu.SetA(Besm6Math.Sin(cpu.A.Value));
                    return;
                case 1:
                    cpu.SetA(Besm6Math.Cos(cpu.A.Value));
                    return;
                default:
                    throw new ProcessorException($"Unimplemented extracode *51 {Convert.ToString(addr, 8)}");
            }
        }

        internal void E52()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0)
                throw new ProcessorException($"Unimplemented extracode *52 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Cos(cpu.A.Value));
        }

        internal void E53()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0)
                throw new ProcessorException($"Unimplemented extracode *53 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Atan(cpu.A.Value));
        }

        internal void E54()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0)
                throw new ProcessorException($"Unimplemented extracode *54 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Asin(cpu.A.Value));
        }

        internal void E55()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0)
                throw new ProcessorException($"Unimplemented extracode *55 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Log(cpu.A.Value));
        }

        internal void E56()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr != 0)
                throw new ProcessorException($"Unimplemented extracode *56 {Convert.ToString(addr, 8)}");
            cpu.SetA(Besm6Math.Exp(cpu.A.Value));
        }
    }
}
