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
            try
            {
                return ExecuteCore();
            }
            catch (Processor.DebugWatchAbortException)
            {
                return false;
            }
        }

        private bool ExecuteCore()
        {
            ref uint k = ref _p._k;
            ulong a = _p._a.Value;
            ulong y = _p._y.Value;
            ref uint r = ref _p._r;
            ref uint c = ref _p._c;
            ref uint rk = ref _p._rk;
            ref uint aex = ref _p._aex;
            var m = _p._m;
            ref bool rightFlag = ref _p._rightInstrFlag;
            ref bool applyC = ref _p._applyC;

            // КАЖДОЙ инструкции — поправка стека действует только для текущей инструкции
            // и потребляется StackCorrection() при её прерывании исключением.
            _p._corrStack = 0;

            k &= 0x7FFFu;

            ulong word = _p.MemFetch(k);
            if (rightFlag)
                rk = (uint)word;
            else
                rk = (uint)(word >> 24);

            rk &= 0xFFFFFFu;

            DecodedInstruction decoded = InstructionCodec.DecodeHalf(rk);
            int reg = decoded.Register;
            uint addr = decoded.Address;
            uint opcode = (uint)decoded.Opcode;

            if (!rightFlag && _p.DebugCheckFetch(k, opcode))
                return false;

            // по K/L-R/RK (без смещения и без преобразования hex↔oct). Фильтрация по экстракодам — в хуке.
            _p.TraceInstruction?.Invoke(k, rightFlag, rk, opcode);

            // Канонический PRE-снимок (canonical TSV trace): ДО advance K/half и
            // ДО применения модификатора — состояние ровно такое, как видит инструкция.
            // half = исполняемая половина (L = старшие 24 бита, R = младшие).
            _p.CanonPre(k, rightFlag, word, rk, opcode, reg, addr);

            // Фиксируем PRE k/half для legacy instr-trace: раньше лог печатал
            // k/rightFlag ПОСЛЕ advance/toggle, что делало поле «R=» значением
            // УЖЕ СЛЕДУЮЩЕЙ инструкции и порождало ложное «смещение фазы» в diff.
            uint tK = k;
            bool tRight = rightFlag;

            uint nextK = Addr(k + 1);
            if (rightFlag)
            {
                k += 1;
                rightFlag = false;
            }
            else
            {
                rightFlag = true;
            }

            if (applyC)
                addr = Addr(addr + c);

            uint nextC = 0;
            Opcode op = (Opcode)opcode;

            switch (op)
            {
                case Opcode.Atx:
                    aex = Addr(addr + m[reg]);
                    _p.MemStore(aex, a);
                    if (addr == 0 && reg == 15) m[15] = Addr(m[15] + 1);
                    break;

                case Opcode.Stx:
                    aex = Addr(addr + m[reg]);
                    _p.MemStore(aex, a);
                    m[15] = Addr(m[15] - 1);
                    _p._corrStack = 1;
                    a = _p.MemLoad(m[15]);
                    _p.SetLogical();
                    break;

                case Opcode.Mod:
                    // 002 РЕГ/MOD — привилегированная инструкция.
                    throw new ProcessorException("Illegal instruction 002 рег/mod");

                case Opcode.Xts:
                    _p.MemStore(m[15], a);
                    m[15] = Addr(m[15] + 1);
                    _p._corrStack = -1;
                    aex = Addr(addr + m[reg]);
                    a = _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.APlusX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), false, false);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.AMinusX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), false, true);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.XMinusA:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), true, false);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Amx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAdd(Word48.FromInt48(_p.MemLoad(aex)), true, true);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Xta:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a = _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.Aax:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a &= _p.MemLoad(aex);
                    y = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Aex:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    y = a;
                    a ^= _p.MemLoad(aex);
                    _p.SetLogical();
                    break;

                case Opcode.Arx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a += _p.MemLoad(aex);
                    if ((a & BIT49) != 0) a = (a + 1) & BITS48;
                    y = 0;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Avx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithChangeSign(((_p.MemLoad(aex) >> 40) & 1u) != 0);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetAdditive();
                    break;

                case Opcode.Aox:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a |= _p.MemLoad(aex);
                    y = 0;
                    _p.SetLogical();
                    break;

                case Opcode.ADivX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithDivide(Word48.FromInt48(_p.MemLoad(aex)));
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.AMulX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithMultiply(Word48.FromInt48(_p.MemLoad(aex)));
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Apx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a = Processor.Besm6Pack(a, _p.MemLoad(aex));
                    y = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Aux:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a = Processor.Besm6Unpack(a, _p.MemLoad(aex));
                    y = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Acx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    a = (ulong)Processor.Besm6CountOnes(a) + _p.MemLoad(aex);
                    if ((a & BIT49) != 0) a = (a + 1) & BITS48;
                    y = 0;
                    _p.SetLogical();
                    break;

                case Opcode.Anx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    if (a != 0)
                    {
                        int n = Processor.Besm6HighestBit(a);
                        _p.ArithShift(48 - n);
                        y = _p._y.Value;
                        a = (ulong)n + _p.MemLoad(aex);
                        if ((a & BIT49) != 0) a = (a + 1) & BITS48;
                    }
                    else
                    {
                        y = 0;
                        a = _p.MemLoad(aex);
                    }
                    _p.SetLogical();
                    break;

                case Opcode.EPlusX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent((int)(_p.MemLoad(aex) >> 41) - 64);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.EMinusX:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(_p.MemLoad(aex) >> 41));
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Asx:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(_p.MemLoad(aex) >> 41) - 64);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetLogical();
                    break;

                case Opcode.Xtr:
                    PrepareStack(ref addr, reg, m);
                    aex = Addr(addr + m[reg]);
                    r = (uint)((_p.MemLoad(aex) >> 41) & 0x3Fu);
                    break;

                case Opcode.Rte:
                    aex = Addr(addr + m[reg]);
                    a = ((ulong)(r & aex & 0x7Fu)) << 41;
                    _p.SetLogical();
                    break;

                case Opcode.Yta:
                    aex = Addr(addr + m[reg]);
                    if (_p.IsLogical())
                    {
                        a = y;
                    }
                    else
                    {
                        ulong x = y;
                        a = (a & ~BITS41) | (y & BITS40);
                        _p._a = Word48.FromInt48(a);
                        _p.ArithAddExponent((int)(aex & 0x7Fu) - 64);
                        a = _p._a.Value;
                        y = x;
                    }
                    break;

                case Opcode.Ext:
                    throw new ProcessorException("Illegal instruction 032 зпп");

                case Opcode.Op33:
                    throw new ProcessorException("Illegal instruction 033 счп");

                case Opcode.EPlusN:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent((int)(aex & 0x7Fu) - 64);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.EMinusN:
                    aex = Addr(addr + m[reg]);
                    _p.ArithAddExponent(64 - (int)(aex & 0x7Fu));
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetMultiplicative();
                    break;

                case Opcode.Asn:
                    aex = Addr(addr + m[reg]);
                    _p.ArithShift((int)(aex & 0x7Fu) - 64);
                    a = _p._a.Value;
                    y = _p._y.Value;
                    _p.SetLogical();
                    break;

                case Opcode.Ntr:
                    aex = Addr(addr + m[reg]);
                    r = aex & 0x3Fu;
                    break;

                case Opcode.Ati:
                    aex = Addr(addr + m[reg]);
                    m[aex & 0xFu] = Addr((uint)a);
                    m[0] = 0;
                    break;

                case Opcode.Sti:
                {
                    aex = Addr(addr + m[reg]);
                    uint rg = aex & 0xFu;
                    uint ad = Addr((uint)a);
                    if (rg != 15)
                    {
                        m[15] = Addr(m[15] - 1);
                        _p._corrStack = 1;
                    }
                    a = _p.MemLoad(rg != 15 ? m[15] : ad);
                    m[rg] = ad;
                    m[0] = 0;
                    _p.SetLogical();
                    break;
                }

                case Opcode.Ita:
                    aex = Addr(addr + m[reg]);
                    a = Addr(m[aex & 0xFu]);
                    _p.SetLogical();
                    break;

                case Opcode.Its:
                    _p.MemStore(m[15], a);
                    m[15] = Addr(m[15] + 1);
                    goto load_modifier;

                case Opcode.Mtj:
                    aex = addr;
                    m[aex & 0xFu] = m[reg];
                    m[0] = 0;
                    break;

                case Opcode.JPlusM:
                    aex = addr;
                    m[aex & 0xFu] = Addr(m[aex & 0xFu] + m[reg]);
                    m[0] = 0;
                    break;

                case Opcode.Op46:
                    throw new ProcessorException("Illegal instruction 046 соп");

                case Opcode.Op47:
                    throw new ProcessorException("Illegal instruction 047");

                case Opcode.Utc:
                    aex = Addr(addr + m[reg]);
                    nextC = aex;
                    break;

                case Opcode.Wtc:
                    if (addr == 0 && reg == 15)
                    {
                        m[15] = Addr(m[15] - 1);
                        _p._corrStack = 1;
                    }
                    aex = Addr(addr + m[reg]);
                    nextC = Addr((uint)_p.MemLoad(aex));
                    break;

                case Opcode.Vtm:
                    aex = addr;
                    m[reg] = addr;
                    m[0] = 0;
                    break;

                case Opcode.Utm:
                    aex = Addr(addr + m[reg]);
                    m[reg] = aex;
                    m[0] = 0;
                    break;

                case Opcode.Uza:
                    aex = Addr(addr + m[reg]);
                    y = a;
                    if (_p.IsAdditive())
                    {
                        if ((a & BIT41) != 0) break;
                    }
                    else if (_p.IsMultiplicative())
                    {
                        if ((a & BIT48) == 0) break;
                    }
                    else if (_p.IsLogical())
                    {
                        if (a != 0) break;
                    }
                    else
                        break;
                    k = aex;
                    rightFlag = false;
                    break;

                case Opcode.U1a:
                    aex = Addr(addr + m[reg]);
                    y = a;
                    if (_p.IsAdditive())
                    {
                        if ((a & BIT41) == 0) break;
                    }
                    else if (_p.IsMultiplicative())
                    {
                        if ((a & BIT48) != 0) break;
                    }
                    else if (_p.IsLogical())
                    {
                        if (a == 0) break;
                    }
                    k = aex;
                    rightFlag = false;
                    break;

                case Opcode.Uj:
                    aex = Addr(addr + m[reg]);
                    k = aex;
                    rightFlag = false;
                    break;

                case Opcode.Vjm:
                    aex = addr;
                    m[reg] = nextK;
                    m[0] = 0;
                    k = addr;
                    rightFlag = false;
                    break;

                case Opcode.Ij:
                    throw new ProcessorException("Illegal instruction 320 выпр/iret");

                case Opcode.Stop:
                    _p._a = Word48.FromInt48(a);
                    _p._y = Word48.FromInt48(y);
                    _p.CanonPost(k, rightFlag);
                    return true;

                case Opcode.Vzm:
                    aex = addr;
                    if (m[reg] == 0) { k = addr; rightFlag = false; }
                    break;

                case Opcode.V1m:
                    aex = addr;
                    if (m[reg] != 0) { k = addr; rightFlag = false; }
                    break;

                case Opcode.Op36:
                    aex = addr;
                    if (m[reg] == 0) { k = addr; rightFlag = false; }
                    break;

                case Opcode.Vlm:
                    aex = addr;
                    if (m[reg] == 0) break;
                    m[reg] = Addr(m[reg] + 1);
                    k = addr;
                    rightFlag = false;
                    break;

                default:
                    if (IsExtracode(opcode))
                    {
                        aex = Addr(addr + m[reg]);
                        m[14] = aex;
                        // «Return from extracode to the next machine word» — экстракод
                        // потребляет всё 48-битное слово (левую и правую половины),
                        // поэтому если правая половина ещё не выполнена, пропускаем её
                        // и продолжаем с левой половины следующего слова.
                        // Без этой логики C# выполняет инструкцию из правой половины
                        // и состояние (A/M[14]) расходится.
                        if (rightFlag)
                        {
                            k += 1;
                            rightFlag = false;
                        }
                        var call = new ExtracodeCall(
                            (Extracode)(int)opcode,
                            aex,
                            checked((byte)reg),
                            checked((ushort)addr),
                            tRight);
                        bool handled;
                        try
                        {
                            handled = _p.ExtracodeDispatch is not null
                                && _p.ExtracodeDispatch(call);
                        }
                        catch (ProcessorException exception)
                        {
                            // E74's empty exception is a complete terminal instruction
                            // even when Processor is used without DubnaLoader.  Other
                            // processor exceptions stay pending: the loader must first
                            // apply stack correction/interception, then finalize POST.
                            if (string.IsNullOrEmpty(exception.Message))
                                _p.CanonPost(k, rightFlag);
                            throw;
                        }
                        if (handled)
                        {
                            // Обработчик экстракода может изменить A/Y напрямую
                            // (например E63: cpu.SetA(...)). Локальные копии a/y
                            // захвачены в начале Execute() и «просрочены» — если их не
                            // обновить, финальная запись `_p._a = Word48.FromInt48(a)`
                            // внизу перезапишет изменения обработчика старым значением.
                            // напрямую, без локальной копии.)
                            a = _p._a.Value;
                            y = _p._y.Value;
                            // экстракода вызывается core.set_logical() — R-режим
                            // приводится к ЛОГИЧЕСКОМУ. Влияет на условные переходы
                            // ПО/ПЕ (UZA/U1A) и весь дальнейший поток; без этого C#
                            _p.SetLogical();
                            break;
                        }
                        throw new ProcessorException($"Extracode {(int)opcode} not implemented");
                    }
                    throw new ProcessorException($"Unknown instruction {opcode}");
            }

            if (nextC != 0) { c = nextC; applyC = true; }
            else { applyC = false; }

            _p._a = Word48.FromInt48(a);
            _p._y = Word48.FromInt48(y);
            _p.CanonPost(k, rightFlag);
            return false;

        load_modifier:
            aex = Addr(addr + m[reg]);
            a = Addr(m[aex & 0xFu]);
            _p.SetLogical();
            if (nextC != 0) { c = nextC; applyC = true; }
            else { applyC = false; }
            _p._a = Word48.FromInt48(a);
            _p._y = Word48.FromInt48(y);
            _p.CanonPost(k, rightFlag);
            return false;
        }

        private void PrepareStack(ref uint addr, int reg, uint[] m)
        {
            if (addr == 0 && reg == 15)
            {
                // 004-027): M[017] = ADDR(M[017] - 1); corr_stack = 1.
                // corr_stack позволяет StackCorrection() откатить декремент, если
                m[15] = Addr(m[15] - 1);
                _p._corrStack = 1;
            }
        }

        private static bool IsExtracode(uint opcode)
        {
            if (opcode >= 0x28 && opcode <= 0x3F) return true;
            if (opcode == 0x80 || opcode == 0x88) return true;
            return false;
        }

        private static uint Addr(uint x) => ArchitectureConstants.NormalizeAddress(x);
        private static ulong OnBit(int n) => ArchitectureConstants.OnBit(n);

        private const ulong BIT41 = ArchitectureConstants.BIT41;
        private const ulong BIT48 = ArchitectureConstants.BIT48;
        private const ulong BIT49 = 1UL << 48;
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS41 = ArchitectureConstants.BITS41;
        private const ulong BITS48 = ArchitectureConstants.BITS48;
    }
}
