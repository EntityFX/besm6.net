namespace Besm6.Runtime
{
    public sealed partial class ExtracodeHandler
    {
        // ─── E57: монтаж лент / файлов (порт dubna/e57.cpp) ───────────────────

        private void E57()
        {
            var cpu = _machine.Cpu;
            long addr = cpu.GetM(M16);

            // Детальная диагностика E57 (BESM6_TRACE).
            if (_traceExtracodes)
            {
                long a = (long)cpu.GetA().Value;
                long m13 = cpu.GetM(13);
                EnsureTraceWriter().WriteLine(
                    $"[E57] addr=0{Convert.ToString(addr, 8)} A=0{a:X} M[13]=0{m13:X}");
            }

            switch (addr)
            {
                case 0:
                    // floor(A) — уже в E50 case 14.
                    cpu.SetA(Besm6Math.Floor(cpu.GetA().Value));
                    return;
                case 2:
                    // Output to Calcomp plotter.
                    _machine.Plotter.CalcompPutCh((char)(cpu.GetA().Value & 0xFF));
                    cpu.SetA(0);
                    return;
                case 3:
                    // Delay 1 sec — no-op.
                    return;
                case 5:
                    // Forex unknown — return 0.
                    cpu.SetA(0);
                    return;
                case 7:
                    // Task paused waiting for tape.
                    throw new ProcessorException("E57: Task paused waiting for tape");
            }

            if (addr == 32767) // 077777 octal
            {
                E57File();
                return;
            }

            if (addr >= 8) // 010 octal
            {
                // E57 tape ops: ASSIGN / RELEASE / FIND (порт e57_tape).
                const long E57_WRITE   = 64;
                const long E57_READY   = 32;
                const long E57_ASSIGN  = 1024;
                const long E57_RELEASE = 2048;

                if ((addr & E57_ASSIGN) != 0)
                {
                    // Mount tape: tapeId in A, disk unit in M[15 octal] = M[13 decimal].
                    long tapeIdAssign = (long)cpu.GetA().Value;
                    int diskUnit = (int)(cpu.GetM(13) & 0x7F);
                    bool writePermit = (addr & E57_WRITE) != 0;
                    bool ok = _mountTapeWithMode != null
                        ? _mountTapeWithMode(tapeIdAssign, diskUnit, writePermit)
                        : _mountTape(tapeIdAssign, diskUnit);
                    if (!ok)
                        throw new ProcessorException($"E57 ASSIGN: cannot mount tape 0x{tapeIdAssign:X} on unit {diskUnit}");
                    if (_traceExtracodes)
                    {
                        TapeImage? mounted = _diskByUnit(diskUnit);
                        EnsureTraceWriter().WriteLine(
                            $"[E57] ASSIGN tape=0{tapeIdAssign:X} -> unit=0{Convert.ToString(diskUnit, 8)} " +
                            $"mounted_id=0{(mounted?.VolumeId.ToString("X") ?? "null")}");
                    }
                    cpu.SetA((ulong)diskUnit);
                    return;
                }

                if ((addr & E57_RELEASE) != 0)
                {
                    // Release tapes according to bitmask on accumulator.
                    if ((addr & E57_READY) == 0)
                        _releaseTapes((long)cpu.GetA().Value);
                    cpu.SetA(0);
                    return;
                }

                // Find mounted tape (by name and number).
                // Return disk number (unit) in A.
                long tapeIdFind = (long)cpu.GetA().Value;
                int unit = _findTape(tapeIdFind);
                cpu.SetA((ulong)unit);
            }
            else
            {
                // addr == 1 or 4: tape control by Gusev — unsupported.
                throw new ProcessorException($"E57: unimplemented extracode *57 {Convert.ToString((int)addr, 8)}");
            }
        }

        private void E57File()
        {
            const ulong keyValue = 0xD38EA0800000UL;
            const ulong keyMask = 0xFFFFE0F00000UL;
            const ulong discLocal = 0xB2F8E1B00000UL;
            const ulong discHome = 0xA2FB65000000UL;
            const ulong discTmp = 0xD2DC00000000UL;
            const ulong bit48 = 1UL << 47;
            const int noAccess = 8;
            const int notFound = 16;

            var cpu = _machine.Cpu;
            ulong request = cpu.GetA().Value;
            if ((request & keyMask) != keyValue)
                throw new ProcessorException("Wrong access key in *57 77777");

            int infoAddr = (int)(request & 0x7FFF);
            int operation = (int)((request >> 15) & 0x1F);
            ulong Read(int address) => _machine.Memory.Read((uint)(address & 0x7FFF)).Value;
            void Write(int address, ulong value) =>
                _machine.Memory.Write((uint)(address & 0x7FFF), new Word48(value));

            switch (operation)
            {
                case 0: // VOLUME_OPEN
                {
                    ulong disc = Read(infoAddr + 1) & 0xFFFFFFFFF000UL;
                    if (disc != discLocal && disc != discHome && disc != discTmp)
                        throw new ProcessorException($"Unsupported disc name: 0x{disc:X12}");
                    cpu.SetA(0);
                    return;
                }
                case 1:
                    throw new ProcessorException(
                        "Extracode *57 77777: operation 'Release Volume' not supported yet");
                case 2: // FILE_SEARCH
                {
                    ulong disc = Read(infoAddr);
                    for (int address = infoAddr + 1; ; address += 4)
                    {
                        if (Read(address) == bit48)
                            break;

                        ulong fileName = Read(address + 1);
                        ulong reply = Read(address + 2);
                        bool writeMode = ((reply >> 29) & 1) != 0;
                        uint offset = _fileSearch(disc, fileName, writeMode);
                        int error = offset == 0 ? (writeMode ? noAccess : notFound) : 0;
                        reply &= ~((1UL << 29) - 1);
                        reply &= ~(0x3FUL << 42);
                        reply |= offset & ((1U << 29) - 1U);
                        reply |= (ulong)error << 42;
                        Write(address + 2, reply);
                    }
                    cpu.SetA(0);
                    return;
                }
                case 3: // FILE_OPEN
                {
                    for (int address = infoAddr + 1; ; address++)
                    {
                        ulong item = Read(address);
                        if (item == 0)
                            break;
                        uint offset = (uint)(item & ((1UL << 29) - 1));
                        bool writeMode = ((item >> 29) & 1) != 0;
                        int unit = (int)((item >> 36) & 0x3F);
                        int error = _fileMount(unit, offset, writeMode, 0);
                        item = (item & ~(0x3FUL << 42)) | ((ulong)(error & 0x3F) << 42);
                        Write(address, item);
                    }
                    cpu.SetA(0);
                    return;
                }
                case 4: // SCRATCH_OPEN
                {
                    for (int address = infoAddr; ; address++)
                    {
                        ulong item = Read(address);
                        if (item == 0)
                            break;
                        int size = (int)(item & 0x1F);
                        int unit = (int)((item >> 36) & 0x3F);
                        _scratchMount(unit, size * 32);
                    }
                    cpu.SetA(0);
                    return;
                }
                case 5:
                    throw new ProcessorException(
                        "Extracode *57 77777: operation 'Release File' not supported yet");
                case 6:
                    throw new ProcessorException(
                        "Extracode *57 77777: operation 'Release All' not supported yet");
                case 31:
                    throw new ProcessorException(
                        "Extracode *57 77777: operation 'Change File Status' not supported yet");
                default:
                    throw new ProcessorException(
                        $"Extracode *57 77777: unknown operation {Convert.ToString(operation, 8)}");
            }
        }

    }
}
