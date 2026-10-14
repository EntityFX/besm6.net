using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;
using Besm6.Runtime.Encoding;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Extracodes
{
    internal sealed partial class HostedTerminalService
    {
        // ─── E61: управление дисплеем VT-340 / плоттеры (порт dubna/e61) ────
        internal void E61()
        {
            var cpu = _cpu;
            long addr = cpu.GetM(M16);
            if (addr == 0x7FFF) // 077777 octal
            {
                // Вывод на плоттер Watanabe или Tektronix.
                // Адрес начала данных — в младших 15 битах A; тип плоттера — в старших 12 битах.
                var bp = new BytePointer(_memory, (uint)(cpu.A.Value & 0x7FFF));
                switch ((cpu.A.Value >> 36) & 0xFFF)
                {
                    case 0:
                        // Watanabe WX4675.
                        for (;;)
                        {
                            byte ch = bp.Get();
                            if (ch == 0)
                                break;
                            _plotter.WatanabePutCh((char)ch);
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
                                if (ch == 0)
                                    break;
                                _plotter.TektronixPutCh((char)ch);
                            }
                        }

                        break;
                    default:
                        throw new ProcessorException($"Extracode *61 77777: unknown target {Convert.ToString(((int)cpu.A.Value >> 36) & 0xFFF, 8)}");
                }

                cpu.SetA(0);
                return;
            }

            // Неизвестный адрес — сброс A.
            cpu.SetA(0);
        }

        // ─── E71: терминальный I/O и перфоратор (порт Processor::e71 из dubna/) ───
        // Контрольное слово (E64_Pointer) лежит по адресу M[16]:
        //   разряды 47-44 — start_reg, 43-39 — flags, 38-24 — start_addr,
        //   разряды 23-20 — end_reg,   19-15 — (рез.), 14-0  — end_addr.
        // start = (start_addr + M[start_reg]) & 077777, end = (end_addr + M[end_reg]) & 077777.
        // flags: 1 = перфоратор, 4 = вывод на терминал, 6 = ввод с терминала.
        internal void E71()
        {
            var cpu = _cpu;
            int ctlAddr = (int)(cpu.GetM(M16) & 0x7FFF);
            long word = (long)_memory.Read((uint)ctlAddr).Value;
            int startReg = (int)((word >> 44) & 0xF);
            int flags = (int)((word >> 39) & 0x1F);
            int startOff = (int)((word >> 24) & 0x7FFF);
            int endReg = (int)((word >> 20) & 0xF);
            int endOff = (int)(word & 0x7FFF);
            int start = (startOff + (int)cpu.GetM(startReg)) & 0x7FFF;
            int end = (endOff + (int)cpu.GetM(endReg)) & 0x7FFF;
            switch (flags)
            {
                case 1: // Перфоратор.
                    if ((end - start + 1) % 24 != 0)
                        throw new ProcessorException("Punched card buffer " + Convert.ToString(start, 8) + "-" + Convert.ToString(end, 8) + " has fractional cards");
                    _puncher.Punch(start, end);
                    return;
                case 4: // Вывод на терминал (KOI-7 -> Unicode, до NUL или до end).
                {
                    int a1 = start, a2 = end;
                    _finishPrint();
                    var bp = new BytePointer(_memory, (uint)a1);
                    byte c = 1;
                    var sb = new System.Text.StringBuilder();
                    while (c != 0)
                    {
                        if (a2 != 0 && a1 > a2)
                            break;
                        for (int i = 0; c != 0 && i < 6; i++)
                        {
                            c = bp.Get();
                            if (c == 0)
                                break;
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
                    _finishPrint();
                    _output("-\r"); // стандартный промпт
                    string inp = _input("");
                    string koi7 = CosyCodec.Utf8ToKoi7(inp, buflen);
                    if (koi7.Length < buflen)
                        koi7 += '\0'; // завершающий нулевой байт, если влезает
                    var bp = new BytePointer(_memory, (uint)start);
                    for (int i = 0; i < koi7.Length; i++)
                        bp.Put((byte)koi7[i]);
                    while (bp.ByteIndex != 0)
                        bp.Put(0); // дописать нулями до конца слова
                    return;
                }

                default:
                    return;
            }
        }
    }
}
