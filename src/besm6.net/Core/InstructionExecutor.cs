using System;

namespace Besm6.Core
{
    /// <summary>
    /// Исполнитель инструкций БЭСМ-6. Вынесено из Processor.Step() (Этап 3).
    /// </summary>
    public class InstructionExecutor
    {
        private readonly Processor _p;

        public InstructionExecutor(Processor p)
        {
            _p = p;
        }

        /// <summary>
        /// Выполняет одну инструкцию. Возвращает true при STOP.
        /// </summary>
        public bool Execute()
        {
            ref long pc = ref _p._pc;
            ref long acc = ref _p._acc;
            ref long rmr = ref _p._rmr;
            ref long rau = ref _p._rau;
            ref long mod = ref _p._mod;
            ref long rk = ref _p._rk;
            ref long aex = ref _p._aex;
            var m = _p._m;
            ref bool rightFlag = ref _p._rightInstrFlag;
            ref bool applyMod = ref _p._applyModReg;

            pc &= 0x7FFF;

            long word = _p.MemFetch(pc);
            if (rightFlag)
                rk = word & 0xFFFFFF;
            else
                rk = (word >> 24) & 0xFFFFFF;

            rk &= 0xFFFFFF;

            int reg = (int)((rk >> 20) & 0x0F);
            long addr;
            long opcode;

            if ((rk & OnBit(20)) != 0)
            {
                addr = rk & 0x7FFF;
                opcode = (rk >> 12) & 0xF8;
            }
            else
            {
                addr = rk & 0xFFF;
                if ((rk & OnBit(19)) != 0)
                    addr |= 0x7000;
                opcode = (rk >> 12) & 0x3F;
            }

            long nextPc = Addr(pc + 1);
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

            long nextMod = 0;

            Opcode op = (Opcode)opcode;
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
                    _p.ArithAdd(_p.MemLoad(aex), false, false);
                    _p.SetAdditive();
                    break;

                case Opcode.Vch:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(_p.MemLoad(aex), false, true);
                    _p.SetAdditive();
                    break;

                case Opcode.Vchob:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(_p.MemLoad(aex), true, false);
                    _p.SetAdditive();
                    break;

                case Opcode.Vchab:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(_p.MemLoad(aex), true, true);
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
                    _p.ArithChangeSign((( _p.MemLoad(aex) >> 40) & 1) != 0);
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
                    _p.ArithDivide(_p.MemLoad(aex));
                    _p.SetMultiplicative();
                    break;

                case Opcode.Umn:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithMultiply(_p.MemLoad(aex));
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
                    acc = Processor.Besm6CountOnes(acc) + _p.MemLoad(aex);
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
                        acc = n + _p.MemLoad(aex);
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
                    _p.SetMultiplicative();
                    break;

                case Opcode.Vchp:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(_p.MemLoad(aex) >> 41));
                    _p.SetMultiplicative();
                    break;

                case Opcode.Sd:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(_p.MemLoad(aex) >> 41) - 64);
                    _p.SetLogical();
                    break;

                case Opcode.Rzh:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    rau = (_p.MemLoad(aex) >> 41) & 0x3F;
                    break;

                case Opcode.Schrzh:
                    aex = Addr(addr + m[reg]);
                    acc = (rau & aex & 0x7F) << 41;
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
                        long x = rmr;
                        acc = (acc & ~BITS41) | (rmr & BITS40);
                        _p.ArithAddExponent((int)(aex & 0x7F) - 64);
                        rmr = x;
                    }
                    break;

                case Opcode.Zpp:
                    throw new ProcessorException("Illegal instruction 032 зпп");

                case Opcode.Schp:
                    throw new ProcessorException("Illegal instruction 033 счп");

                case Opcode.Slpa:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent((int)(aex & 0x7F) - 64);
                    _p.SetMultiplicative();
                    break;

                case Opcode.Vchpa:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(aex & 0x7F));
                    _p.SetMultiplicative();
                    break;

                case Opcode.Sda:
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(aex & 0x7F) - 64);
                    _p.SetLogical();
                    break;

                case Opcode.Rza:
                    aex = Addr(addr + m[reg]);
                    rau = aex & 0x3F;
                    break;

                case Opcode.Ui:
                    aex = Addr(addr + m[reg]);
                    m[aex & 0xF] = Addr(acc);
                    m[0] = 0;
                    break;

                case Opcode.Uim:
                {
                    aex = Addr(addr + m[reg]);
                    long rg = aex & 0xF;
                    long ad = Addr(acc);
                    if (rg != 15) m[15] = Addr(m[15] - 1);
                    acc = _p.MemLoad(rg != 15 ? m[15] : ad);
                    m[rg] = ad;
                    m[0] = 0;
                    _p.SetLogical();
                    break;
                }

                case Opcode.Schi:
                    aex = Addr(addr + m[reg]);
                    acc = Addr(m[aex & 0xF]);
                    _p.SetLogical();
                    break;

                case Opcode.Schim:
                    _p.MemStore(m[15], acc);
                    m[15] = Addr(m[15] + 1);
                    goto load_modifier;

                case Opcode.Uii:
                    aex = addr;
                    m[aex & 0xF] = m[reg];
                    m[0] = 0;
                    break;

                case Opcode.Sli:
                    aex = addr;
                    m[aex & 0xF] = Addr(m[aex & 0xF] + m[reg]);
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
                    nextMod = Addr(_p.MemLoad(aex));
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
                    // 0320 выпр/iret — возврат из прерывания:
                    // PC = ACC (адрес возврата сохранён в аккумулятор),
                    // сброс флагов конвейера, потребить intercept.
                    pc = acc;
                    rightFlag = false;
                    applyMod = false;
                    mod = 0;
                    _p.ConsumeIntercept();
                    break;

                case Opcode.Stop:
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
                        if (_p.ExtracodeHandler != null && _p.ExtracodeHandler((int)opcode, aex))
                            break;
                        throw new ProcessorException($"Extracode {(int)opcode} not implemented");
                    }
                    throw new ProcessorException($"Unknown instruction {opcode}");
            }

            if (nextMod != 0) { mod = nextMod; applyMod = true; }
            else { applyMod = false; }

            return false;

        load_modifier:
            aex = Addr(addr + m[reg]);
            acc = Addr(m[aex & 0xF]);
            _p.SetLogical();
            if (nextMod != 0) { mod = nextMod; applyMod = true; }
            else { applyMod = false; }
            return false;
        }

        private static void PrepareStack(ref long addr, int reg, long[] m)
        {
            if (addr == 0 && reg == 15)
                m[15] = Addr(m[15] - 1);
        }

        private static bool IsExtracode(long opcode)
        {
            if (opcode >= 0x28 && opcode <= 0x3F) return true;
            if (opcode == 0x80 || opcode == 0x88) return true;
            return false;
        }

        private static long Addr(long x) => Besm6Constants.Addr(x);
        private static long OnBit(int n) => Besm6Constants.OnBit(n);

        private const long BIT41 = Besm6Constants.BIT41;
        private const long BIT48 = Besm6Constants.BIT48;
        private const long BIT49 = Besm6Constants.BIT49;
        private const long BITS40 = Besm6Constants.BITS40;
        private const long BITS41 = Besm6Constants.BITS41;
        private const long BITS48 = Besm6Constants.BITS48;
    }
}