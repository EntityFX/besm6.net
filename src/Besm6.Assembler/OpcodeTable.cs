namespace Besm6.Assembler;

/// <summary>
/// Таблица мнемоник инструкций БЭСМ-6 (MADLEN + БЭМШ) с учётом выбранного
/// диалекта. Кодирование полуслов выполняется движком поверх кода опкода.
/// </summary>
internal static class OpcodeTable
{
    /// <summary>MADLEN-мнемоники коротких команд (опкод 0..63).</summary>
    internal static readonly string[] ShortMadlen =
    {
        "atx",  "stx",  "mod",  "xts",  "a+x",  "a-x",  "x-a",  "amx",
        "xta",  "aax",  "aex",  "arx",  "avx",  "aox",  "a/x",  "a*x",
        "apx",  "aux",  "acx",  "anx",  "e+x",  "e-x",  "asx",  "xtr",
        "rte",  "yta",  "ext",  "*33",  "e+n",  "e-n",  "asn",  "ntr",
        "ati",  "sti",  "ita",  "its",  "mtj",  "j+m",  "*46",  "*47",
        "*50",  "*51",  "*52",  "*53",  "*54",  "*55",  "*56",  "*57",
        "*60",  "*61",  "*62",  "*63",  "*64",  "*65",  "*66",  "*67",
        "*70",  "*71",  "*72",  "*73",  "*74",  "*75",  "*76",  "*77",
    };

    /// <summary>MADLEN-мнемоники длинных команд (опкод 0200..0370).</summary>
    internal static readonly string[] LongMadlen =
    {
        "*20", "*21", "utc", "wtc",  "vtm", "utm", "uza", "u1a",
        "uj",  "vjm", "ij",  "stop", "vzm", "v1m", "*36", "vlm",
    };

    /// <summary>БЭМШ-мнемоники коротких команд (опкод 0..63).</summary>
    internal static readonly string[] ShortBemsh =
    {
        "зп",  "зпм", "рег", "счм", "сл",  "вч",  "вчоб","вчаб",
        "сч",  "и",   "нтж", "слц", "знак","или", "дел", "умн",
        "сбр", "рзб", "чед", "нед", "слп", "вчп", "сд",  "рж",
        "счрж","счмр","зпп", "счп", "слпа","вчпа","сда", "ржа",
        "уи",  "уим", "счи", "счим","уии", "сли", "соп", "э47",
        "э50", "э51", "э52", "э53", "э54", "э55", "э56", "э57",
        "э60", "э61", "э62", "э63", "э64", "э65", "э66", "э67",
        "э70", "э71", "э72", "э73", "э74", "э75", "э76", "э77",
    };

    /// <summary>БЭМШ-мнемоники длинных команд (опкод 0200..0370).</summary>
    internal static readonly string[] LongBemsh =
    {
        "э20", "э21", "мода", "мод",  "уиа", "слиа", "по",    "пе",
        "пб",  "пв",  "выпр", "стоп", "пио", "пино", "втбрз", "цикл",
    };

    /// <summary>
    /// Получить код опкода по мнемонике в указанном диалекте.
    /// Псевдоним "*32" и "ext" всегда дают EXT (032). Для строгих диалектов
    /// ищется только соответствующая таблица; для Auto — обе.
    /// </summary>
    internal static bool TryGetOpcode(string opname, AssemblyDialect dialect, out int opcode)
    {
        opcode = 0;

        // Совместимый числовой псевдоним прежней таблицы для документированного EXT.
        if (opname == "*32")
        {
            opcode = (int)Besm6.Architecture.Opcode.Ext;
            return true;
        }

        bool allowMadlen = dialect != AssemblyDialect.Bemsh;
        bool allowBemsh = dialect != AssemblyDialect.Madlen;

        if (allowMadlen || allowBemsh)
        {
            for (int i = 0; i < 64; ++i)
            {
                if ((allowBemsh && ShortBemsh[i] == opname) ||
                    (allowMadlen && ShortMadlen[i] == opname))
                {
                    opcode = i;
                    return true;
                }
            }
            for (int i = 0; i < 16; ++i)
            {
                if ((allowBemsh && LongBemsh[i] == opname) ||
                    (allowMadlen && LongMadlen[i] == opname))
                {
                    opcode = (i << 3) | 0x80; // 0200 octal = 0x80
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Возвращает MADLEN-мнемонику по коду опкода (для disassembler-совместимости).</summary>
    internal static string GetOpName(int opcode)
    {
        if ((opcode & 0x80) != 0) // long opcode (0200 octal)
            return LongMadlen[(opcode >> 3) & 0xF];
        return ShortMadlen[opcode & 0x3F];
    }

    /// <summary>Возвращает БЭМШ-мнемонику по коду опкода.</summary>
    internal static string GetOpNameBemsh(int opcode)
    {
        if ((opcode & 0x80) != 0)
            return LongBemsh[(opcode >> 3) & 0xF];
        return ShortBemsh[opcode & 0x3F];
    }
}