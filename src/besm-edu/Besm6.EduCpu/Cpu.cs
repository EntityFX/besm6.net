namespace Besm6.EduCpu;

/// <summary>
/// Учебная модель процессора БЭСМ-6.
/// Аппаратный аналог — аккумулятор, индекс-регистры, командная позиция и цикл выборки/исполнения (ядро — пайплайн Step()).
/// Зависит только от <see cref="Memory"/> и не знает о консоли.
/// </summary>
public sealed class Cpu
{
    private readonly Memory _memory;

    private Word48 _acc;
    private ushort[] _m = new ushort[16]; // M0..M15; чтение M0 всегда ноль
    private ushort _pc;                   // 15-разрядный адрес командного слова
    private Half _half = Half.Left;
    private bool _stopped;
    private int _steps;

    public Cpu(Memory memory, ushort entryAddress)
    {
        _memory = memory;
        if (entryAddress > Memory.MaxAddress)
        {
            throw new OutOfRangeAddressException($"Адрес входа 0{Oct.Pad(entryAddress, 5)} вне памяти.");
        }

        _pc = entryAddress;
    }

    public Word48 Acc => _acc;
    public ushort Pc => _pc;
    public Half Half => _half;
    public bool Stopped => _stopped;
    public int Steps => _steps;
    public ushort ReadM(int reg) => reg == 0 ? (ushort)0 : _m[reg];

    /// <summary>Только-чтение: 24-битная половина по текущей командной позиции (состояние не меняется).</summary>
    public uint ReadInstruction()
    {
        Word48 word = _memory.Read(_pc);
        return _half == Half.Left ? word.LeftHalf : word.RightHalf;
    }

    /// <summary>
    /// Один шаг процессора — цикл мини-пайплайна:
    /// 1. FETCH (половина 24 бита) → 2. DECODE (Op, регистр M, адрес) → 3. ADDR (база + M[рег], mod 2^15)
    /// → 4. EXEC (ACC/память/M/переход) → 5. COMMIT (позиция, шаг) → 6. TRACE (запись).
    /// До успешной проверки состояние не изменяется: ошибки не оставляют частичных изменений.
    /// </summary>
    public Trace Step()
    {
        // пайплайн: после STOP повторный вход запрещён.
        if (_stopped)
        {
            throw new StepAfterStopException(_pc, _half);
        }
        // Снимок «до» — для трассы и гарантии «без изменений на ошибке».
        ushort fromAddr = _pc;
        Half fromHalf = _half;
        Word48 accBefore = _acc;
        // 1. FETCH: текущая позиция (адрес + половина) определяет 24-битную команду.
        Word48 word = _memory.Read(_pc);
        uint raw24 = fromHalf == Half.Left ? word.LeftHalf : word.RightHalf;
        // 2. DECODE: неизвестный код операции бросает исключение — состояние ещё не менялось.
        Instruction ins = Instruction.Decode(raw24);
        // 3. Следующая позиция по умолчанию (L→R, R→следующее слово); переход на этапе 4 может её перезаписать.
        (ushort nextAddr, Half nextHalf) = NextPosition(_pc, _half);
        // 4. ADDR + EXEC (у VTM адресное поле — назначение регистра, не адрес).
        ushort effective = EffectiveAddress(ins);
        string effect = Execute(ins, effective, ref nextAddr, ref nextHalf);
        // 5. COMMIT: фиксация позиции и шага — только после успешного исполнения.
        _pc = nextAddr;
        _half = nextHalf;
        _steps++;

        // 6. TRACE: запись несёт оба снимка ACC — до и после.
        return new Trace(_steps, fromAddr, fromHalf, raw24, ins, effective, accBefore, _acc, nextAddr, nextHalf, effect);
    }

    /// <summary>Цикл пайплайна: повторяет Step() до STOP, ошибки или лимита шагов (проверка лимита — после каждого успешного шага).</summary>
    public void Run(int maxSteps)
    {
        if (maxSteps <= 0)
        {
            throw new StepLimitExceededException(0, maxSteps);
        }

        while (!_stopped)
        {
            try
            {
                Step();
            }
            catch (StepLimitExceededException)
            {
                throw;
            }
            catch (CpuException ex) when (ex is not StepLimitExceededException)
            {
                throw new CpuException($"Шаг {_steps + 1}: {ex.Message}", ex);
            }

            if (_steps >= maxSteps && !_stopped)
            {
                throw new StepLimitExceededException(_steps, maxSteps);
            }
        }
    }

    /// <summary>
    /// Этап адресации: эффективный адрес = базовый + M[регистр] (по модулю 2^15).
    /// Для VTM поле регистра — назначение, а не адрес, поэтому этап пропускается.
    /// </summary>
    private ushort EffectiveAddress(Instruction ins)
    {
        if (ins.Opcode == Op.Vtm)
        {
            return 0;
        }

        return (ushort)((ins.BaseAddress + ReadM(ins.Register)) & 32767); // 077777
    }

    /// <summary>Поток командной позиции: L→R внутри слова, R→следующее слово (адрес по модулю 2^15).</summary>
    private (ushort, Half) NextPosition(ushort addr, Half half)
        => half == Half.Left ? (addr, Half.Right) : ((ushort)((addr + 1) & 32767), Half.Left);

    /// <summary>
    /// Этап исполнения: диспетчеризация по коду операции; команда меняет ACC/память/M-регистры или перезаписывает следующую позицию (переходы).
    /// Возвращаемая строка — «эффект» в записи трассы.
    /// </summary>
    private string Execute(Instruction ins, ushort effective, ref ushort nextAddr, ref Half nextHalf)
    {
        switch (ins.Opcode)
        {
            case Op.Atx:
                _memory.Write(effective, _acc);
                return $"ЗП: mem[0{Oct.Pad(effective, 5)}] = ACC";

            case Op.Xta:
                _acc = _memory.Read(effective);
                return $"СЧ: ACC = mem[0{Oct.Pad(effective, 5)}]";

            case Op.Aax:
                _acc = _acc.And(_memory.Read(effective));
                return $"И: ACC &= mem[0{Oct.Pad(effective, 5)}]";

            case Op.Aex:
                _acc = _acc.Xor(_memory.Read(effective));
                return $"НТЖ: ACC ^= mem[0{Oct.Pad(effective, 5)}]";

            case Op.Arx:
                _acc = _acc.CyclicAdd(_memory.Read(effective));
                return $"СЛЦ: ACC += mem[0{Oct.Pad(effective, 5)}] (по модулю 2^48)";

            case Op.Sub:
                _acc = _acc.Subtract(_memory.Read(effective));
                return $"ВЫЧ: ACC = ACC - mem[0{Oct.Pad(effective, 5)}] (по модулю 2^48)";

            case Op.Mul:
                _acc = _acc.Multiply(_memory.Read(effective));
                return $"УМН: ACC = (ACC & 03777777) * (mem[0{Oct.Pad(effective, 5)}] & 03777777) (младшие 48 бит)";

            case Op.Aox:
                _acc = _acc.Or(_memory.Read(effective));
                return $"ИЛИ: ACC |= mem[0{Oct.Pad(effective, 5)}]";

            case Op.Vtm:
                _m[ins.Register] = ins.BaseAddress;
                return $"УИА: M0{Oct.Pad(ins.Register, 1)} = 0{Oct.Pad(ins.BaseAddress, 5)}";

            case Op.Uza:
                if (_acc.Raw == 0)
                {
                    nextAddr = effective;
                    nextHalf = Half.Left;
                    return "ПО: ACC == 0, переход взят";
                }

                return "ПО: ACC != 0, переход не взят";

            case Op.Uj:
                nextAddr = effective;
                nextHalf = Half.Left;
                return $"ПБ: переход на 0{Oct.Pad(effective, 5)}";

            case Op.Stop:
                _stopped = true;
                return "СТОП: процессор остановлен";

            default:
                throw new UnsupportedOpcodeException($"Неподдерживаемая команда: 0{Oct.Pad(ins.Raw24, 6)}.");
        }
    }
}
