namespace Besm6.EduCpu;

/// <summary>
/// Результат декодирования 24-разрядной команды — выход этапа DECODE пайплайна Step().
/// Encode* — обратное преобразование с проверкой round-trip через Decode.
/// </summary>
public readonly struct Instruction
{
    public const ushort MaxShortOpcode = 63;    // 077 восьмерично, 6 бит
    public const ushort MaxLongOpcode = 248;    // 0370 восьмерично, 7 бит (включая бит формата)
    public const ushort MaxShortAddress = 4095; // 07777 восьмерично, 12 бит
    public const ushort MaxLongAddress = 32767; // 077777 восьмерично, 15 бит

    private static readonly Dictionary<Op, (string En, string Ru)> Mnemonics = new()
    {
        [Op.Atx] = ("atx", "зп"), [Op.Sub] = ("sub", "выч"), [Op.Mul] = ("mul", "умн"),
        [Op.Xta] = ("xta", "сч"), [Op.Aax] = ("aax", "и"),
        [Op.Aex] = ("aex", "нтж"), [Op.Arx] = ("arx", "слц"), [Op.Aox] = ("aox", "или"),
        [Op.Vtm] = ("vtm", "уиа"), [Op.Uza] = ("uza", "по"), [Op.Uj] = ("uj", "пб"),
        [Op.Stop] = ("stop", "стоп"),
    };

    public uint Raw24 { get; }
    public InstructionFormat Format { get; }
    public Op Opcode { get; }
    public byte Register { get; }
    public ushort BaseAddress { get; }
    public string Disassembly { get; }

    public Instruction(uint raw24, InstructionFormat format, Op opcode, byte register, ushort baseAddress, string disassembly)
    {
        Raw24 = raw24;
        Format = format;
        Opcode = opcode;
        Register = register;
        BaseAddress = baseAddress;
        Disassembly = disassembly;
    }

    /// <summary>
    /// Декодирование 24 бит. Сначала читается форматный признак (бит 19),
    /// затем извлекаются код операции, номер индекс-регистра и адрес.
    /// </summary>
    public static Instruction Decode(uint raw24)
    {
        if (raw24 > 0xFF_FFFFu) // 24 бита
        {
            throw new InvalidInstructionException($"Команда шире 24 бит: 0{Oct.Pad(raw24, 6)}.");
        }

        // Разрядка 24 бит: [017 регистр][19 формат][18 X/код][17..12 код оп.][адрес].
        byte reg = (byte)((raw24 >> 20) & 15); // 017
        bool isLong = (raw24 & (1u << 19)) != 0;
        // Длинный формат: 7-битный код (включая признак формата), 15-разрядный адрес.
        // Короткий: 6-битный код, 12-разрядный адрес; признак X дополняет адрес 111 (070000).
        ushort op = isLong ? (ushort)((raw24 >> 12) & 248) : (ushort)((raw24 >> 12) & 63); // 0370 / 077
        ushort ad = isLong ? (ushort)(raw24 & 32767) : (ushort)(raw24 & 4095); // 077777 / 07777
        if (!isLong && (raw24 & (1u << 18)) != 0)
        {
            ad |= 28672; // 070000 восьмерично: старшие три бита адреса = 111
        }

        if (!Enum.IsDefined(typeof(Op), op))
        {
            throw new UnsupportedOpcodeException($"Неподдерживаемый код операции: 0{Oct.Pad(op, 4)}.");
        }

        var format = isLong ? InstructionFormat.Long : InstructionFormat.Short;
        return new Instruction(raw24, format, (Op)op, reg, ad, Disassemble((Op)op, reg, ad));
    }

    /// <summary>
    /// Кодирование короткоадресной команды (12-разрядный адрес);
    /// результат проверяется round-trip через Decode.
    /// </summary>
    public static uint EncodeShort(Op opcode, byte register, ushort address)
    {
        CheckRegister(register);
        if ((ushort)opcode > MaxShortOpcode)
        {
            throw new InvalidInstructionException($"Опкод 0{Oct.Pad((ushort)opcode, 3)} шире 6 бит.");
        }

        if (address > MaxShortAddress)
        {
            throw new InvalidInstructionException($"Адрес 0{Oct.Pad(address, 5)} не помещается в 12 разрядов.");
        }

        return ValidateRoundTrip(((uint)register << 20) | ((uint)(ushort)opcode << 12) | address, opcode, register, address);
    }

    /// <summary>
    /// Кодирование длинноадресной команды (15-разрядный адрес);
    /// опкод обязан быть кодом длинного формата (бит 19 = 1).
    /// </summary>
    public static uint EncodeLong(Op opcode, byte register, ushort address)
    {
        CheckRegister(register);
        ushort op = (ushort)opcode;
        if (op > MaxLongOpcode || (op & 128) == 0 || (op & 7) != 0) // 0200, 07
        {
            throw new InvalidInstructionException($"Опкод 0{Oct.Pad(op, 4)} не является кодом длинного формата.");
        }

        if (address > MaxLongAddress)
        {
            throw new InvalidInstructionException($"Адрес 0{Oct.Pad(address, 6)} шире 15 разрядов.");
        }

        return ValidateRoundTrip(((uint)register << 20) | ((uint)op << 12) | address, opcode, register, address);
    }

    private static void CheckRegister(byte register)
    {
        if (register > 15)
        {
            throw new InvalidInstructionException($"Номер индекс-регистра 0{Oct.Pad(register, 1)} вне диапазона M0..M15.");
        }
    }

    /// <summary>Round-trip: декодируем скомбинированное слово и сверяем поля (страховка от ошибки разрядки).</summary>
    private static uint ValidateRoundTrip(uint raw24, Op opcode, byte register, ushort address)
    {
        if (raw24 > 0xFF_FFFFu) // 24 бита
        {
            throw new InvalidInstructionException("Скомбинированная команда шире 24 бит.");
        }

        Instruction d = Decode(raw24);
        if (d.Opcode != opcode || d.Register != register || d.BaseAddress != address)
        {
            throw new InvalidInstructionException("Составные поля не совпали после кодирования.");
        }

        return raw24;
    }

    private static string Disassemble(Op opcode, byte reg, ushort addr)
    {
        string en = Mnemonics[opcode].En;
        if (addr != 0 || reg != 0)
        {
            if (addr != 0)
            {
                return reg == 0 ? $"{en} 0{Oct.Pad(addr, 5)}" : $"{en} 0{Oct.Pad(addr, 5)}(0{Oct.Pad(reg, 1)})";
            }

            return $"{en} 00000(0{Oct.Pad(reg, 1)})";
        }

        return en;
    }
}