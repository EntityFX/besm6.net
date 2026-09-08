namespace Besm6.Runtime
{
    public sealed partial class ExtracodeHandler
    {
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
