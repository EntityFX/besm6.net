namespace Besm6.Asm
{
    /// <summary>
    /// Таблица мнемоник инструкций БЭСМ-6 (Madlen + Bemsh).
    /// </summary>
    public static class OpcodeTable
    {
        // Madlen mnemonics (short, opcode 0..63)
        public static readonly string[] ShortMadlen =
        {
            "atx",  "stx",  "mod",  "xts",  "a+x",  "a-x",  "x-a",  "amx",
            "xta",  "aax",  "aex",  "arx",  "avx",  "aox",  "a/x",  "a*x",
            "apx",  "aux",  "acx",  "anx",  "e+x",  "e-x",  "asx",  "xtr",
            "rte",  "yta",  "*32",  "ext",  "e+n",  "e-n",  "asn",  "ntr",
            "ati",  "sti",  "ita",  "its",  "mtj",  "j+m",  "*46",  "*47",
            "*50",  "*51",  "*52",  "*53",  "*54",  "*55",  "*56",  "*57",
            "*60",  "*61",  "*62",  "*63",  "*64",  "*65",  "*66",  "*67",
            "*70",  "*71",  "*72",  "*73",  "*74",  "*75",  "*76",  "*77",
        };

        // Madlen mnemonics (long, opcode 0200..0370)
        public static readonly string[] LongMadlen =
        {
            "*20", "*21", "utc", "wtc",  "vtm", "utm", "uza", "u1a",
            "uj",  "vjm", "ij",  "stop", "vzm", "v1m", "*36", "vlm",
        };

        // Bemsh mnemonics (short, opcode 0..63)
        public static readonly string[] ShortBemsh =
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

        // Bemsh mnemonics (long, opcode 0200..0370)
        public static readonly string[] LongBemsh =
        {
            "э20", "э21", "мода", "мод",  "уиа", "слиа", "по",    "пе",
            "пб",  "пв",  "выпр", "стоп", "пио", "пино", "втбрз", "цикл",
        };

        /// <summary>
        /// Получить мнемонику (Madlen) по коду инструкции.
        /// </summary>
        public static string GetOpName(int opcode)
        {
            if ((opcode & 0x80) != 0) // long opcode (0200 octal)
                return LongMadlen[(opcode >> 3) & 0xF];
            // Short opcode: clear extended bit (0x40), use 6-bit index
            return ShortMadlen[opcode & 0x3F];
        }

        /// <summary>
        /// Получить мнемонику (Bemsh) по коду инструкции.
        /// </summary>
        public static string GetOpNameBemsh(int opcode)
        {
            if ((opcode & 0x80) != 0)
                return LongBemsh[(opcode >> 3) & 0xF];
            return ShortBemsh[opcode & 0x3F];
        }

        /// <summary>
        /// Получить opcode по мнемонике (Madlen или Bemsh).
        /// </summary>
        public static bool TryGetOpcode(string opname, out int opcode)
        {
            for (int i = 0; i < 64; ++i)
            {
                if (ShortBemsh[i] == opname || ShortMadlen[i] == opname)
                {
                    opcode = i;
                    return true;
                }
            }
            for (int i = 0; i < 16; ++i)
            {
                if (LongBemsh[i] == opname || LongMadlen[i] == opname)
                {
                    opcode = (i << 3) | 0x80; // 0200 octal = 0x80
                    return true;
                }
            }
            opcode = 0;
            return false;
        }
    }
}