using System;

namespace Besm6.Core
{
    /// <summary>
    /// Исполнитель инструкций БЭСМ-6. Вынесено из Processor.Step() (Этап 3).
    /// </summary>
    public class InstructionExecutor
    {
        private readonly Processor _p;
        private readonly bool _instrTrace = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BESM6_INSTR_TRACE"));
        private System.IO.StreamWriter? _instrWriter = null;

        public InstructionExecutor(Processor p)
        {
            _p = p;
        }

        private System.IO.StreamWriter GetInstrWriter()
        {
            if (_instrWriter == null)
            {
                var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "instr_trace.log");
                _instrWriter = new System.IO.StreamWriter(path, append: false) { AutoFlush = true };
            }
            return _instrWriter;
        }

        /// <summary>
        /// Выполняет одну инструкцию. Возвращает true при STOP.
        /// </summary>
        public bool Execute()
        {
            ref uint pc = ref _p._pc;
            ulong acc = _p._acc.Value;
            ulong rmr = _p._rmr.Value;
            ref uint rau = ref _p._rau;
            ref uint mod = ref _p._mod;
            ref uint rk = ref _p._rk;
            ref uint aex = ref _p._aex;
            var m = _p._m;
            ref bool rightFlag = ref _p._rightInstrFlag;
            ref bool applyMod = ref _p._applyModReg;

            pc &= 0x7FFFu;

            ulong word = _p.MemFetch(pc);
            if (rightFlag)
                rk = (uint)word;
            else
                rk = (uint)(word >> 24);

            rk &= 0xFFFFFFu;

            int reg = (int)((rk >> 20) & 0x0Fu);
            uint addr;
            uint opcode;

            if (((ulong)rk & OnBit(20)) != 0)
            {
                addr = rk & 0x7FFFu;
                opcode = (rk >> 12) & 0xF8u;
            }
            else
            {
                addr = rk & 0xFFFu;
                if (((ulong)rk & OnBit(19)) != 0)
                    addr |= 0x7000u;
                opcode = (rk >> 12) & 0x3Fu;
            }

            // Трассировка в формате C++ (аналог ref/processor.cpp:151 → Processor::print_instruction):
            // фиксируем pc/rightFlag/rk/opcode ДО advance PC, поэтому строка C# = строке C++
            // по PC/L-R/RK (без смещения и без преобразования hex↔oct). Фильтрация по экстракодам — в хуке.
            _p.TraceInstruction?.Invoke(pc, rightFlag, rk, opcode);

            // Канонический PRE-снимок (canonical TSV trace): ДО advance PC/half и
            // ДО применения модификатора — состояние ровно такое, как видит инструкция.
            // half = исполняемая половина (L = старшие 24 бита, R = младшие).
            _p.CanonPre(pc, rightFlag, word, rk, opcode, reg, addr);

            // Фиксируем PRE pc/half для legacy instr-trace: раньше лог печатал
            // pc/rightFlag ПОСЛЕ advance/toggle, что делало поле «R=» значением
            // УЖЕ СЛЕДУЮЩЕЙ инструкции и порождало ложное «смещение фазы» в diff.
            uint tPc = pc;
            bool tRight = rightFlag;

            uint nextPc = Addr(pc + 1);
            if (rightFlag)
            {
                pc += 1;
                rightFlag = false;
            }
            else
            {
                rightFlag = true;
            }

            if (applyMod)
                addr = Addr(addr + mod);

            uint nextMod = 0;
            Opcode op = (Opcode)opcode;

            // Instruction-level trace
            if (_instrTrace)
            {
                var w = GetInstrWriter();
                w.WriteLine($"{tPc:X5} R={(tRight?"R":"L")} op={opcode,3} reg={reg,2} addr={addr,5} " +
                    $"acc={acc:X12} rau={rau:X1} mod={mod,5} m14={m[14],5} {op}");
            }

            switch (op)
            {
                case Opcode.Zp:
                    aex = Addr(addr + m[reg]);
                    _p.MemStore(aex, acc);
                    if (addr == 0 && reg == 15) m[15] = Addr(m[15] + 1);
                    break;

                case Opcode.Zpm:
                    aex = Addr(addr + m[reg]);
                    _p.MemStore(aex, acc);
                    m[15] = Addr(m[15] - 1);
                    acc = _p.MemLoad(m[15]);
                    _p.SetLogical();
                    break;

                case Opcode.Reg:
                    // 002 рег/mod — привилегированная инструкция.
                    // C++ (processor.cpp:194-195): throw Exception("Illegal instruction 002 рег/mod");
                    throw new ProcessorException("Illegal instruction 002 рег/mod");

                case Opcode.Schm:
                    _p.MemStore(m[15], acc);
                    m[15] = Addr(m[15] + 1);
                    aex = Addr(addr + m[reg]);
                    acc = _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.Sl:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), false, false);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Vch:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), false, true);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Vchob:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), true, false);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Vchab:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), true, true);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Sch:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc = _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.I:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc &= _p.MemLoad(aex);
                    rmr = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Ntzh:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    rmr = acc;
                    acc ^= _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.Slc:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc += _p.MemLoad(aex);
                    if ((acc & BIT49) != 0) acc = (acc + 1) & BITS48;
                    rmr = 0;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Znak:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithChangeSign(((_p.MemLoad(aex) >> 40) & 1u) != 0);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Ili:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc |= _p.MemLoad(aex);
                    rmr = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Del:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithDivide(Word48.FromInt48(_p.MemLoad(aex)));
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Umn:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithMultiply(Word48.FromInt48(_p.MemLoad(aex)));
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Sbr:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc = Processor.Besm6Pack(acc, _p.MemLoad(aex));
                    rmr = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Rzb:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc = Processor.Besm6Unpack(acc, _p.MemLoad(aex));
                    rmr = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Ched:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    acc = (ulong)Processor.Besm6CountOnes(acc) + _p.MemLoad(aex);
                    if ((acc & BIT49) != 0) acc = (acc + 1) & BITS48;
                    rmr = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Ned:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    if (acc != 0)
                    {
                        int n = Processor.Besm6HighestBit(acc);
                        _p.ArithShift(48 - n);
                        rmr = _p._rmr.Value;
                        acc = (ulong)n + _p.MemLoad(aex);
                        if ((acc & BIT49) != 0) acc = (acc + 1) & BITS48;
                    }
                    else
                    {
                        rmr = 0;
                        acc = _p.MemLoad(aex);
                    }
                    _p.SetLogical();
                    break;

                case Opcode.Slep:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent((int)(_p.MemLoad(aex) >> 41) - 64);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Vchp:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(_p.MemLoad(aex) >> 41));
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Sd:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(_p.MemLoad(aex) >> 41) - 64);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetLogical();
                    break;

                case Opcode.Rzh:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    rau = (uint)((_p.MemLoad(aex) >> 41) & 0x3Fu);
                    break;

                case Opcode.Schrzh:
                    aex = Addr(addr + m[reg]);
                    acc = ((ulong)(rau & aex & 0x7Fu)) << 41;
                    _p.SetLogical();
                    break;

                case Opcode.Schmr:
                    aex = Addr(addr + m[reg]);
                    if (_p.IsLogical())
                    {
                        acc = rmr;
                    }
                    else
                    {
                        ulong x = rmr;
                        acc = (acc & ~BITS41) | (rmr & BITS40);
                        _p._acc = Word48.FromInt48(acc);
                        _p.ArithAddExponent((int)(aex & 0x7Fu) - 64);
                        acc = _p._acc.Value;
                        rmr = x;
                    }
                    break;

                case Opcode.Zpp:
                    throw new ProcessorException("Illegal instruction 032 зпп");

                case Opcode.Schp:
                    throw new ProcessorException("Illegal instruction 033 счп");

                case Opcode.Slpa:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent((int)(aex & 0x7Fu) - 64);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Vchpa:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(aex & 0x7Fu));
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Sda:
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(aex & 0x7Fu) - 64);
                    acc = _p._acc.Value;
                    rmr = _p._rmr.Value;
                    _p.SetLogical();
                    break;

                case Opcode.Rza:
                    aex = Addr(addr + m[reg]);
                    rau = aex & 0x3Fu;
                    break;

                case Opcode.Ui:
                    aex = Addr(addr + m[reg]);
                    m[aex & 0xFu] = Addr((uint)acc);
                    m[0] = 0;
                    break;

                case Opcode.Uim:
                {
                    aex = Addr(addr + m[reg]);
                    uint rg = aex & 0xFu;
                    uint ad = Addr((uint)acc);
                    if (rg != 15) m[15] = Addr(m[15] - 1);
                    acc = _p.MemLoad(rg != 15 ? m[15] : ad);
                    m[rg] = ad;
                    m[0] = 0;
                    _p.SetLogical();
                    break;
                }

                case Opcode.Schi:
                    aex = Addr(addr + m[reg]);
                    acc = Addr(m[aex & 0xFu]);
                    _p.SetLogical();
                    break;

                case Opcode.Schim:
                    _p.MemStore(m[15], acc);
                    m[15] = Addr(m[15] + 1);
                    goto load_modifier;

                case Opcode.Uii:
                    aex = addr;
                    m[aex & 0xFu] = m[reg];
                    m[0] = 0;
                    break;

                case Opcode.Sli:
                    aex = addr;
                    m[aex & 0xFu] = Addr(m[aex & 0xFu] + m[reg]);
                    m[0] = 0;
                    break;

                case Opcode.Sop:
                    throw new ProcessorException("Illegal instruction 046 соп");

                case Opcode.Op47:
                    throw new ProcessorException("Illegal instruction 047");

                case Opcode.Moda:
                    aex = Addr(addr + m[reg]);
                    nextMod = aex;
                    break;

                case Opcode.Mod:
                    if (addr == 0 && reg == 15) m[15] = Addr(m[15] - 1);
                    aex = Addr(addr + m[reg]);
                    nextMod = Addr((uint)_p.MemLoad(aex));
                    break;

                case Opcode.Uia:
                    aex = addr;
                    m[reg] = addr;
                    m[0] = 0;
                    break;

                case Opcode.Slia:
                    aex = Addr(addr + m[reg]);
                    m[reg] = aex;
                    m[0] = 0;
                    break;

                case Opcode.Po:
                    aex = Addr(addr + m[reg]);
                    rmr = acc;
                    if (_p.IsAdditive())
                    {
                        if ((acc & BIT41) != 0) break;
                    }
                    else if (_p.IsMultiplicative())
                    {
                        if ((acc & BIT48) == 0) break;
                    }
                    else if (_p.IsLogical())
                    {
                        if (acc != 0) break;
                    }
                    else
                        break;
                    pc = aex;
                    rightFlag = false;
                    break;

                case Opcode.Pe:
                    aex = Addr(addr + m[reg]);
                    rmr = acc;
                    if (_p.IsAdditive())
                    {
                        if ((acc & BIT41) == 0) break;
                    }
                    else if (_p.IsMultiplicative())
                    {
                        if ((acc & BIT48) != 0) break;
                    }
                    else if (_p.IsLogical())
                    {
                        if (acc == 0) break;
                    }
                    pc = aex;
                    rightFlag = false;
                    break;

                case Opcode.Pb:
                    aex = Addr(addr + m[reg]);
                    pc = aex;
                    rightFlag = false;
                    break;

                case Opcode.Pv:
                    aex = addr;
                    m[reg] = nextPc;
                    m[0] = 0;
                    pc = addr;
                    rightFlag = false;
                    break;

                case Opcode.Vypr:
                    // C++ (processor.cpp:734-735): throw Exception("Illegal instruction 32 выпр/iret");
                    throw new ProcessorException("Illegal instruction 320 выпр/iret");

                case Opcode.Stop:
                    _p._acc = Word48.FromInt48(acc);
                    _p._rmr = Word48.FromInt48(rmr);
                    return true;

                case Opcode.Pio:
                    aex = addr;
                    if (m[reg] == 0) { pc = addr; rightFlag = false; }
                    break;

                case Opcode.Pino:
                    aex = addr;
                    if (m[reg] != 0) { pc = addr; rightFlag = false; }
                    break;

                case Opcode.E36:
                    aex = addr;
                    if (m[reg] == 0) { pc = addr; rightFlag = false; }
                    break;

                case Opcode.Tsikl:
                    // 0370 цикл / vlm — точный порт C++ processor.cpp:762-769.
                    // C++ ИНКРЕМЕНТИРУЕТ M[reg] при каждом выполнении.
                    aex = addr;
                    if (m[reg] == 0) break;
                    m[reg] = Addr(m[reg] + 1);
                    pc = addr;
                    rightFlag = false;
                    break;

                default:
                    if (IsExtracode(opcode))
                    {
                        aex = Addr(addr + m[reg]);
                        m[14] = aex;
                        // Точный порт C++ Processor::extracode() (dubna/extracode.cpp:30-36):
                        // «Return from extracode to the next machine word» — экстракод
                        // потребляет всё 48-битное слово (левую и правую половины),
                        // поэтому если правая половина ещё не выполнена, пропускаем её
                        // и продолжаем с левой половины следующего слова.
                        // Без этой логики C# выполняет инструкцию из правой половины
                        // (например 02567 R после «*63 502» в a400), чего C++ не делает,
                        // и состояние (ACC/M[14]) расходится.
                        if (rightFlag)
                        {
                            pc += 1;
                            rightFlag = false;
                        }
                        // Сохраняем поля инструкции для трассировки в формате C++ (print_instruction).
                        _p.ExtracodeReg = reg;
                        _p.ExtracodeRawAddr = addr;
                        _p.ExtracodeRightFlag = rightFlag;
                        if (_p.ExtracodeHandler != null && _p.ExtracodeHandler((int)opcode, aex))
                        {
                            // Обработчик экстракода может изменить ACC/RMR напрямую
                            // (например E63: cpu.SetAcc(...)). Локальные копии acc/rmr
                            // захвачены в начале Execute() и «просрочены» — если их не
                            // обновить, финальная запись `_p._acc = Word48.FromInt48(acc)`
                            // внизу перезапишет изменения обработчика старым значением.
                            // (В C++ такой проблемы нет: там core.ACC используется
                            // напрямую, без локальной копии.)
                            acc = _p._acc.Value;
                            rmr = _p._rmr.Value;
                            // Точный порт C++ processor.cpp:639-640: после каждого
                            // экстракода вызывается core.set_logical() — RAU-режим
                            // приводится к ЛОГИЧЕСКОМУ. Влияет на условные переходы
                            // по/пе (Po/Pe) и весь дальнейший поток; без этого C#
                            // расходится с C++ по RAU-режиму и улетает в MONSYS-цикл.
                            _p.SetLogical();
                            break;
                        }
                        throw new ProcessorException($"Extracode {(int)opcode} not implemented");
                    }
                    throw new ProcessorException($"Unknown instruction {opcode}");
            }

            if (nextMod != 0) { mod = nextMod; applyMod = true; }
            else { applyMod = false; }

            _p._acc = Word48.FromInt48(acc);
            _p._rmr = Word48.FromInt48(rmr);
            _p.CanonPost(pc, rightFlag);
            return false;

        load_modifier:
            aex = Addr(addr + m[reg]);
            acc = Addr(m[aex & 0xFu]);
            _p.SetLogical();
            if (nextMod != 0) { mod = nextMod; applyMod = true; }
            else { applyMod = false; }
            _p._acc = Word48.FromInt48(acc);
            _p._rmr = Word48.FromInt48(rmr);
            _p.CanonPost(pc, rightFlag);
            return false;
        }

        private static void PrepareStack(ref uint addr, int reg, uint[] m)
        {
            if (addr == 0 && reg == 15)
                m[15] = Addr(m[15] - 1);
        }

        private static bool IsExtracode(uint opcode)
        {
            if (opcode >= 0x28 && opcode <= 0x3F) return true;
            if (opcode == 0x80 || opcode == 0x88) return true;
            return false;
        }

        private static uint Addr(uint x) => Besm6Constants.Addr(x);
        private static ulong OnBit(int n) => Besm6Constants.OnBit(n);

        private const ulong BIT41 = Besm6Constants.BIT41;
        private const ulong BIT48 = Besm6Constants.BIT48;
        private const ulong BIT49 = Besm6Constants.BIT49;
        private const ulong BITS40 = Besm6Constants.BITS40;
        private const ulong BITS41 = Besm6Constants.BITS41;
        private const ulong BITS48 = Besm6Constants.BITS48;
    }
}