namespace Besm6.Runtime
{
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

    }
}
