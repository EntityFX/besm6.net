using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// Собирает гистограмму исполненных опкодов и считает модельное (детерминированное)
    /// время работы задания по документированной таблице тактов <see cref="Besm6Timing"/>.
    ///
    /// Профайлер подписывается на read-only хук <c>Processor.InstructionTrace</c> и
    /// НЕ изменяет исполнительный путь процессора (Gate A сохраняется).
    /// </summary>
    public sealed class OpcodeProfiler
    {
        private const int OpcodeSlots = 256;
        private readonly long[] _counts = new long[OpcodeSlots];
        private long _unknownOpcodeInstructions;

        /// <summary>Число проанализированных инструкций.</summary>
        public long TotalInstructions { get; private set; }

        /// <summary>Суммарное модельное число тактов.</summary>
        public long TotalCycles { get; private set; }

        /// <summary>Среднее число тактов на инструкцию (CPI модели).</summary>
        public double CyclesPerInstruction =>
            TotalInstructions == 0 ? 0.0 : (double)TotalCycles / TotalInstructions;

        /// <summary>Модельное время работы, секунды (такты × 100 нс).</summary>
        public double ModeledSeconds =>
            TotalCycles * (Besm6Timing.NanosecondsPerCycle / 1_000_000_000.0);

        /// <summary>Число инструкций, для опкода которых не нашлось записи в таблице.</summary>
        public long UnknownOpcodeInstructions => _unknownOpcodeInstructions;

        /// <summary>Сбросить накопленную статистику.</summary>
        public void Reset()
        {
            Array.Clear(_counts, 0, _counts.Length);
            _unknownOpcodeInstructions = 0;
            TotalInstructions = 0;
            TotalCycles = 0;
        }

        /// <summary>Учесть одну исполненную инструкцию по её опкоду.</summary>
        public void Observe(uint opcode)
        {
            TotalInstructions++;
            int cycles = Besm6Timing.CyclesOf((Opcode)opcode);
            TotalCycles += cycles;

            int slot = (int)(opcode & 0xFF);
            _counts[slot]++;
            if (!HasTableEntry(opcode))
                _unknownOpcodeInstructions++;
        }

        /// <summary>Учесть одну исполненную инструкцию из типизированной записи трассировки.</summary>
        public void Observe(InstructionTraceRecord record) => Observe((uint)record.Instruction.Opcode);

        /// <summary>Число исполнений конкретного опкода.</summary>
        public long CountOf(Opcode opcode) => _counts[(uint)opcode & 0xFF];

        /// <summary>Топ-N опкодов по числу исполнений.</summary>
        public IReadOnlyList<OpcodeStat> TopOpcodes(int top)
        {
            var stats = new List<OpcodeStat>();
            for (int slot = 0; slot < OpcodeSlots; slot++)
            {
                long count = _counts[slot];
                if (count == 0)
                    continue;
                var opcode = (Opcode)slot;
                stats.Add(new OpcodeStat(opcode, count, Besm6Timing.CyclesOf(opcode)));
            }

            stats.Sort(static (a, b) => b.Count.CompareTo(a.Count));
            if (top > 0 && stats.Count > top)
                stats.RemoveRange(top, stats.Count - top);
            return stats;
        }

        /// <summary>
        /// Метрики Dhrystone на основе модельного числа тактов.
        /// <paramref name="passes"/> — число проходов тела бенчмарка;
        /// <paramref name="baselineCycles"/> / <paramref name="baselineInstructions"/> —
        /// накладные расходы (компиляция/загрузка/инициализация), которые вычитаются,
        /// чтобы остался чистый вклад тела бенчмарка.
        /// </summary>
        public DhrystoneMetrics ComputeDhrystone(
            long passes, long baselineCycles = 0, long baselineInstructions = 0)
        {
            long workloadCycles = TotalCycles - baselineCycles;
            long workloadInstructions = TotalInstructions - baselineInstructions;
            if (passes <= 0 || workloadCycles <= 0)
                return DhrystoneMetrics.NotMeasurable;

            double secondsPerPass =
                workloadCycles * (Besm6Timing.NanosecondsPerCycle / 1_000_000_000.0) / passes;
            if (secondsPerPass <= 0.0)
                return DhrystoneMetrics.NotMeasurable;

            double dhrystonesPerSecond = 1.0 / secondsPerPass;
            return new DhrystoneMetrics(
                Passes: passes,
                Cycles: workloadCycles,
                Instructions: workloadInstructions,
                SecondsPerPass: secondsPerPass,
                DhrystonesPerSecond: dhrystonesPerSecond,
                VaxMips: dhrystonesPerSecond / Besm6Timing.ReferenceDhrystonesPerSecond,
                Measurable: true);
        }

        /// <summary>
        /// Метрики mpmflops (MFLOPS) на основе модельного числа тактов.
        /// <paramref name="flops"/> — точное число операций, выполненных ядром
        /// (его печатает сама программа: words × ops/word × repeat passes).
        /// <paramref name="baselineCycles"/>/<paramref name="baselineInstructions"/> —
        /// накладные расходы (монитор + компиляция + инициализация + валидация).
        /// </summary>
        public MflopsMetrics ComputeMflops(
            long flops, long baselineCycles = 0, long baselineInstructions = 0)
        {
            long workloadCycles = TotalCycles - baselineCycles;
            long workloadInstructions = TotalInstructions - baselineInstructions;
            if (flops <= 0 || workloadCycles <= 0)
                return MflopsMetrics.NotMeasurable;

            double seconds = workloadCycles * (Besm6Timing.NanosecondsPerCycle / 1_000_000_000.0);
            if (seconds <= 0.0)
                return MflopsMetrics.NotMeasurable;

            double mflops = flops / 1_000_000.0 / seconds;
            return new MflopsMetrics(
                Flops: flops,
                Cycles: workloadCycles,
                Instructions: workloadInstructions,
                Seconds: seconds,
                Mflops: mflops,
                FlopsPerCycle: flops / (double)workloadCycles,
                FlopsPerInstruction: workloadInstructions == 0
                    ? 0.0
                    : flops / (double)workloadInstructions,
                Measurable: true);
        }

        /// <summary>Блок метрик в стиле оригинального mpmflops.</summary>
        public string FormatMflops(
            long flops, long baselineCycles = 0, long baselineInstructions = 0)
        {
            MflopsMetrics m = ComputeMflops(flops, baselineCycles, baselineInstructions);
            var sb = new StringBuilder();
            if (!m.Measurable)
            {
                sb.AppendLine("MP-MFLOPS: недостаточно данных для расчёта (нужны flops > 0 и такты > 0).");
                return sb.ToString();
            }

            sb.AppendLine("MP-MFLOPS Benchmark (model time, BESM-6 cycle table, 100 ns/cycle)");
            sb.AppendLine(Invariant($"Total MFLOPS:                      {m.Mflops:F4}"));
            sb.AppendLine(Invariant($"Instructions per flop:            {1.0 / m.FlopsPerInstruction:F5}"));
            sb.AppendLine(Invariant($"Flops per cycle:                  {m.FlopsPerCycle:F5}"));
            sb.AppendLine(Invariant($"Model time:                       {m.Seconds:F6} c"));
            sb.AppendLine(Invariant($"Flops:                            {m.Flops}"));
            return sb.ToString();
        }

        /// <summary>Сводка профайлера: итоги + топ-N опкодов.</summary>
        public string FormatSummary(int top = 15)
        {
            var sb = new StringBuilder();
            sb.AppendLine("── Профиль исполнения (модель тактов БЭСМ-6, 100 нс/такт) ──");
            sb.AppendLine(Invariant($"Инструкций: {TotalInstructions}"));
            sb.AppendLine(Invariant($"Тактов (модель): {TotalCycles}"));
            sb.AppendLine(Invariant($"CPI модели: {CyclesPerInstruction:F3}"));
            sb.AppendLine(Invariant($"Модельное время: {ModeledSeconds:F6} c"));
            if (_unknownOpcodeInstructions > 0)
                sb.AppendLine(Invariant($"Опкодов вне таблицы: {_unknownOpcodeInstructions}"));

            IReadOnlyList<OpcodeStat> stats = TopOpcodes(top);
            if (stats.Count > 0)
            {
                sb.AppendLine(Invariant(
                    $"{"Код",-6}{"Мнемоника",-10}{"Команд",14}{"Такты/ком",11}{"Тактов",16}{"Доля",10}"));
                foreach (OpcodeStat stat in stats)
                {
                    string percent = TotalInstructions == 0
                        ? "0.00%"
                        : ((double)stat.Count / TotalInstructions)
                            .ToString("P2", CultureInfo.InvariantCulture);
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-6}{1,-10}{2,14}{3,11}{4,16}{5,10}",
                        Convert.ToString((uint)stat.Opcode, 8).PadLeft(3, '0'),
                        Mnemonic(stat.Opcode),
                        stat.Count,
                        stat.Cycles,
                        stat.Count * stat.Cycles,
                        percent));
                }
            }

            return sb.ToString();
        }

        /// <summary>Блок метрик в стиле оригинального Dhrystone 2.1.</summary>
        public string FormatDhrystone(long passes, long baselineCycles = 0, long baselineInstructions = 0)
        {
            DhrystoneMetrics m = ComputeDhrystone(passes, baselineCycles, baselineInstructions);
            var sb = new StringBuilder();
            if (!m.Measurable)
            {
                sb.AppendLine("Dhrystone: недостаточно данных для расчёта (нужны проходы > 0 и такты > 0).");
                return sb.ToString();
            }

            sb.AppendLine("Dhrystone Benchmark, Version 2.1 (Language: Pascal for BESM-6)");
            sb.AppendLine(Invariant($"Number of runs:                     {m.Passes}"));
            sb.AppendLine(Invariant($"Instructions per run (model):       {(double)m.Instructions / m.Passes:F1}"));
            sb.AppendLine(Invariant($"Cycles per run (model):             {(double)m.Cycles / m.Passes:F1}"));
            sb.AppendLine(Invariant($"Time per run (model, sec):          {m.SecondsPerPass:F9}"));
            sb.AppendLine(Invariant($"Dhrystones per Second:              {m.DhrystonesPerSecond:F2}"));
            sb.AppendLine(Invariant($"VAX MIPS rating:                    {m.VaxMips:F4}"));
            return sb.ToString();
        }

        private static bool HasTableEntry(uint opcode)
        {
            var opcodeValue = (Opcode)opcode;
            // Опкоды, для которых документированная стоимость совпадает со значением
            // по умолчанию (2 такта), всё равно присутствуют в таблице.
            return Besm6Timing.CyclesOf(opcodeValue) != Besm6Timing.DefaultCycles
                || opcodeValue is Opcode.Atx or Opcode.Stx or Opcode.Xts or Opcode.Xta
                    or Opcode.APlusX or Opcode.AMinusX or Opcode.XMinusA or Opcode.Amx
                    or Opcode.Apx or Opcode.Aux or Opcode.Asx or Opcode.Asn
                    or Opcode.Mod or Opcode.Ext or Opcode.Op33 or Opcode.Op46
                    or Opcode.Op47 or Opcode.Uza or Opcode.U1a or Opcode.Uj
                    or Opcode.Vjm or Opcode.Ij or Opcode.Stop or Opcode.Vzm
                    or Opcode.V1m or Opcode.Op36 or Opcode.Vlm;
        }

        private static string Invariant(FormattableString text) =>
            text.ToString(CultureInfo.InvariantCulture);

        /// <summary>Мнемоника БЕМШ (для читаемости гистограммы).</summary>
        public static string Mnemonic(Opcode opcode) => opcode switch
        {
            Opcode.Atx => "зп",
            Opcode.Stx => "зпм",
            Opcode.Mod => "рег",
            Opcode.Xts => "счм",
            Opcode.APlusX => "сл",
            Opcode.AMinusX => "вч",
            Opcode.XMinusA => "вчоб",
            Opcode.Amx => "вчаб",
            Opcode.Xta => "сч",
            Opcode.Aax => "и",
            Opcode.Aex => "нтж",
            Opcode.Arx => "слц",
            Opcode.Avx => "знак",
            Opcode.Aox => "или",
            Opcode.ADivX => "дел",
            Opcode.AMulX => "умн",
            Opcode.Apx => "сбр",
            Opcode.Aux => "рзб",
            Opcode.Acx => "чед",
            Opcode.Anx => "нед",
            Opcode.EPlusX => "слп",
            Opcode.EMinusX => "вчп",
            Opcode.Asx => "сд",
            Opcode.Xtr => "рж",
            Opcode.Rte => "счрж",
            Opcode.Yta => "счмр",
            Opcode.Ext => "зпп",
            Opcode.Op33 => "счп",
            Opcode.EPlusN => "слпа",
            Opcode.EMinusN => "вчпа",
            Opcode.Asn => "сда",
            Opcode.Ntr => "ржа",
            Opcode.Ati => "уи",
            Opcode.Sti => "уим",
            Opcode.Ita => "счи",
            Opcode.Its => "счим",
            Opcode.Mtj => "уии",
            Opcode.JPlusM => "сли",
            Opcode.Op46 => "соп",
            Opcode.Op47 => "э47",
            Opcode.Utc => "мода",
            Opcode.Wtc => "мод",
            Opcode.Vtm => "уиа",
            Opcode.Utm => "слиа",
            Opcode.Uza => "по",
            Opcode.U1a => "пе",
            Opcode.Uj => "пб",
            Opcode.Vjm => "пв",
            Opcode.Ij => "выпр",
            Opcode.Stop => "стоп",
            Opcode.Vzm => "пио",
            Opcode.V1m => "пино",
            Opcode.Op36 => "э36",
            Opcode.Vlm => "цикл",
            _ => Besm6Timing.IsExtracode(opcode)
                ? "эк" + Convert.ToString((uint)opcode, 8).PadLeft(3, '0')
                : "?",
        };
    }

    /// <summary>Строка гистограммы по одному опкоду.</summary>
    public readonly record struct OpcodeStat(Opcode Opcode, long Count, int Cycles);

    /// <summary>Метрики Dhrystone, посчитанные по модельному времени.</summary>
    public readonly record struct DhrystoneMetrics(
        long Passes,
        long Cycles,
        long Instructions,
        double SecondsPerPass,
        double DhrystonesPerSecond,
        double VaxMips,
        bool Measurable)
    {
        /// <summary>Недостаточно данных для расчёта.</summary>
        public static DhrystoneMetrics NotMeasurable => new(0, 0, 0, 0.0, 0.0, 0.0, false);
    }

    /// <summary>Метрики mpmflops, посчитанные по модельному времени.</summary>
    public readonly record struct MflopsMetrics(
        long Flops,
        long Cycles,
        long Instructions,
        double Seconds,
        double Mflops,
        double FlopsPerCycle,
        double FlopsPerInstruction,
        bool Measurable)
    {
        /// <summary>Недостаточно данных для расчёта.</summary>
        public static MflopsMetrics NotMeasurable =>
            new(0, 0, 0, 0.0, 0.0, 0.0, 0.0, false);
    }
}
