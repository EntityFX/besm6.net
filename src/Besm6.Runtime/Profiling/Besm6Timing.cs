namespace Besm6.Runtime
{
    /// <summary>
    /// Документированная модель времени выполнения команд БЭСМ-6 в тактах.
    /// Источник: <c>book/03-instruction-set.md</c> §3.4 («Полная таблица команд»)
    /// и §3.7 («Временные характеристики команд»).
    ///
    /// Модель используется профайлером <see cref="OpcodeProfiler"/> для расчёта
    /// модельного (детерминированного) времени работы задания и метрик Dhrystone,
    /// не зависящих от скорости host-машины.
    /// </summary>
    public static class Besm6Timing
    {
        /// <summary>Длительность одного такта процессора БЭСМ-6, нс (≈10 МГц).</summary>
        public const int NanosecondsPerCycle = 100;

        /// <summary>Такты экстракода (050–077): документально «~50–100». Нижняя граница.</summary>
        public const int ExtracodeCycles = 50;

        /// <summary>Такты по умолчанию для опкода, отсутствующего в таблице.</summary>
        public const int DefaultCycles = 2;

        /// <summary>Эталонная производительность DEC VAX-11/780 в Dhrystones/sec (1.0 DMIPS).</summary>
        public const double ReferenceDhrystonesPerSecond = 1757.0;

        /// <summary>
        /// Число тактов процессора БЭСМ-6 для заданного опкода (по таблице учебника).
        /// </summary>
        public static int CyclesOf(Opcode opcode)
        {
            // Экстракоды 050–077 (oct) = 0x28–0x3F, а также длинные 0200/0210.
            if (IsExtracode(opcode))
                return ExtracodeCycles;

            return opcode switch
            {
                // ── Пересылки и работа с памятью (2 такта) ──
                Opcode.Atx => 2,      // 000 зч/зп
                Opcode.Stx => 2,      // 001 зпм
                Opcode.Xts => 2,      // 003 счм
                Opcode.Xta => 2,      // 010 сч

                // ── Арифметика с плавающей точкой ──
                Opcode.APlusX => 2,   // 004 сл
                Opcode.AMinusX => 2,  // 005 вч
                Opcode.XMinusA => 2,  // 006 вчоб
                Opcode.Amx => 2,      // 007 вчаб
                Opcode.Avx => 1,      // 014 знáк / изменение знака
                Opcode.ADivX => 12,   // 016 дел
                Opcode.AMulX => 7,    // 017 умн
                Opcode.EPlusX => 4,   // 024 слп
                Opcode.EMinusX => 4,  // 025 вчп
                Opcode.EPlusN => 4,   // 034 слпа
                Opcode.EMinusN => 4,  // 035 вчпа

                // ── Логические и побитовые операции ──
                Opcode.Aax => 1,      // 011 и
                Opcode.Aex => 1,      // 012 нтж
                Opcode.Arx => 4,      // 013 слц
                Opcode.Aox => 1,      // 015 или
                Opcode.Apx => 2,      // 020 сбр
                Opcode.Aux => 2,      // 021 рзб
                Opcode.Acx => 4,      // 022 чед
                Opcode.Anx => 4,      // 023 нед
                Opcode.Asx => 2,      // 026 сд
                Opcode.Asn => 2,      // 036 сда

                // ── Индекс-регистры (1 такт) ──
                Opcode.Ati => 1,      // 040 уи
                Opcode.Sti => 1,      // 041 уим
                Opcode.Ita => 1,      // 042 счи
                Opcode.Its => 1,      // 043 счим
                Opcode.Mtj => 1,      // 044 уии
                Opcode.JPlusM => 1,   // 045 сли

                // ── Привилегированные и режимные ──
                Opcode.Mod => 2,      // 002 рег (привилегированная)
                Opcode.Xtr => 1,      // 027 рж
                Opcode.Rte => 1,      // 030 счрж
                Opcode.Yta => 1,      // 031 счмр
                Opcode.Ext => 2,      // 032 увв (привилегированная)
                Opcode.Op33 => 2,     // 033 ост/счп
                Opcode.Ntr => 1,      // 037 ржа
                Opcode.Op46 => 2,     // 046 соп (привилегированная)
                Opcode.Op47 => 2,     // 047 зарезервированная

                // ── Модификация и адресация (1 такт) ──
                Opcode.Utc => 1,      // 0220 мода
                Opcode.Wtc => 1,      // 0230 мод
                Opcode.Vtm => 1,      // 0240 уиа
                Opcode.Utm => 1,      // 0250 слиа

                // ── Переходы (2 такта) ──
                Opcode.Uza => 2,      // 0260 по
                Opcode.U1a => 2,      // 0270 пе
                Opcode.Uj => 2,       // 0300 пб
                Opcode.Vjm => 2,      // 0310 пп
                Opcode.Ij => 2,       // 0320 выпр
                Opcode.Stop => 2,     // 0330 стоп
                Opcode.Vzm => 2,      // 0340 пио
                Opcode.V1m => 2,      // 0350 пино
                Opcode.Op36 => 2,     // 0360 э36
                Opcode.Vlm => 2,      // 0370 цикл

                _ => DefaultCycles,
            };
        }

        /// <summary>Признак экстракода (050–077 oct, а также длинные 0200/0210).</summary>
        public static bool IsExtracode(Opcode opcode)
        {
            uint value = (uint)opcode;
            return value is >= 0x28 and <= 0x3F or 0x80 or 0x88;
        }
    }
}
