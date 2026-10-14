using Besm6.Runtime.Profiling;
using System.Text;

namespace Besm6.Runtime
{
    // Compatibility-only rating API. Active CLI diagnostics use FormatSummary;
    // guest benchmark programs own their operation counts and timing intervals.
    public sealed partial class OpcodeProfiler
    {
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
        /// Метрики Whetstone (MWIPS) на основе модельного числа тактов.
        /// <paramref name="operations"/> — число операций, выполненных восемью
        /// циклами (печатает сама программа: сумма n&#215;ops_per_iter по N1..N8).
        /// <paramref name="loopCycles"/> — такты, приходящиеся именно на циклы,
        /// если они измерены посегментно; иначе используется полная стоимость
        /// прогона за вычетом накладных расходов.
        /// </summary>
        public WhetstoneMetrics ComputeWhetstone(
            long operations, long baselineCycles = 0, long baselineInstructions = 0,
            long loopCycles = 0)
        {
            long workloadCycles = loopCycles > 0
                ? loopCycles
                : TotalCycles - baselineCycles;
            long workloadInstructions = loopCycles > 0
                ? 0
                : TotalInstructions - baselineInstructions;
            if (operations <= 0 || workloadCycles <= 0)
                return WhetstoneMetrics.NotMeasurable;

            double seconds = workloadCycles * (Besm6Timing.NanosecondsPerCycle / 1_000_000_000.0);
            if (seconds <= 0.0)
                return WhetstoneMetrics.NotMeasurable;

            double mops = operations / 1_000_000.0 / seconds;
            // MWIPS по оригиналу: WIPS = ops/сек, нормировка на 10 и на 100 проходов x100.
            double mwips = mops * 10.0;
            return new WhetstoneMetrics(
                Operations: operations,
                Cycles: workloadCycles,
                Instructions: workloadInstructions,
                Seconds: seconds,
                Mops: mops,
                Mwips: mwips,
                OperationsPerCycle: (double)operations / workloadCycles,
                VaxMips: mwips / Besm6Timing.ReferenceMwips,
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

        /// <summary>Блок метрик в стиле оригинального Whetstone (Curnow 1972).</summary>
        public string FormatWhetstone(
            long operations, long baselineCycles = 0, long baselineInstructions = 0,
            long loopCycles = 0)
        {
            WhetstoneMetrics m = ComputeWhetstone(
                operations, baselineCycles, baselineInstructions, loopCycles);
            var sb = new StringBuilder();
            if (!m.Measurable)
            {
                sb.AppendLine("Whetstone: недостаточно данных для расчёта (нужны операции > 0 и такты > 0).");
                return sb.ToString();
            }

            sb.AppendLine("Whetstone Benchmark (model time, BESM-6 cycle table, 100 ns/cycle)");
            sb.AppendLine(Invariant($"Operations (loops N1..N8):         {m.Operations}"));
            sb.AppendLine(Invariant($"Loops only cycles:                {m.Cycles}"));
            sb.AppendLine(Invariant($"Model time:                       {m.Seconds:F6} c"));
            sb.AppendLine(Invariant($"Total MOPS:                       {m.Mops:F4}"));
            sb.AppendLine(Invariant($"MWIPS rating:                     {m.Mwips:F4}"));
            sb.AppendLine(Invariant($"Operations per cycle:             {m.OperationsPerCycle:F5}"));
            sb.AppendLine(Invariant($"VAX MIPS rating:                   {m.VaxMips:F4}"));
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

    }

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

    /// <summary>Метрики Whetstone, посчитанные по модельному времени.</summary>
    public readonly record struct WhetstoneMetrics(
        long Operations,
        long Cycles,
        long Instructions,
        double Seconds,
        double Mops,
        double Mwips,
        double OperationsPerCycle,
        double VaxMips,
        bool Measurable)
    {
        /// <summary>Недостаточно данных для расчёта.</summary>
        public static WhetstoneMetrics NotMeasurable =>
            new(0, 0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, false);
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
