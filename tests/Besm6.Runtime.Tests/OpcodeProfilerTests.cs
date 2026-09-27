using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

/// <summary>
/// Профайлер опкодов и таблица тактов БЭСМ-6 (<see cref="Besm6Timing"/>):
/// документированные значения тактов, накопление гистограммы и воспроизводимость
/// метрик Dhrystone (DMIPS) без зависимости от host-машины.
/// </summary>
[TestClass]
public sealed class OpcodeProfilerTests
{
    [TestMethod]
    public void CyclesOf_DocumentedValues_MatchInstructionSetTable()
    {
        // Источник: book/03-instruction-set.md §3.7.
        Assert.AreEqual(1, Besm6Timing.CyclesOf(Opcode.Aax));    // 011 и
        Assert.AreEqual(2, Besm6Timing.CyclesOf(Opcode.APlusX)); // 004 сл
        Assert.AreEqual(7, Besm6Timing.CyclesOf(Opcode.AMulX));  // 017 умн
        Assert.AreEqual(12, Besm6Timing.CyclesOf(Opcode.ADivX)); // 016 дел
        Assert.AreEqual(1, Besm6Timing.CyclesOf(Opcode.Ati));    // 040 уи
        Assert.AreEqual(2, Besm6Timing.CyclesOf(Opcode.Uj));     // 0300 пб
        Assert.AreEqual(2, Besm6Timing.CyclesOf(Opcode.Xta));    // 010 сч
    }

    [TestMethod]
    public void CyclesOf_AnyExtracode_UsesExtracodeCost()
    {
        // Опкоды экстракодов не имеют имён в enum Opcode (они в Besm6.Architecture.Extracode):
        // 050 oct = 40 dec = 0x28, 064 oct = 52 dec = 0x34, 076 oct = 62 dec = 0x3E.
        Assert.AreEqual(Besm6Timing.ExtracodeCycles, Besm6Timing.CyclesOf((Opcode)0x28));
        Assert.AreEqual(Besm6Timing.ExtracodeCycles, Besm6Timing.CyclesOf((Opcode)0x34));
        Assert.AreEqual(Besm6Timing.ExtracodeCycles, Besm6Timing.CyclesOf((Opcode)0x3E));
    }

    [TestMethod]
    public void NanosecondsPerCycle_AndReferenceBaseline_AreDocumentedConstants()
    {
        // Значения читаем через reflection: прямое сравнение двух compile-time
        // констант MSTest-анализатор помечает как «условие всегда истинно»
        // (MSTEST0032) — тот же приём, что в MachineCoreClockTests.
        Assert.AreEqual(100, Convert.ToInt32(ReadConst(nameof(Besm6Timing.NanosecondsPerCycle))));
        Assert.AreEqual(1757d, Convert.ToDouble(ReadConst(nameof(Besm6Timing.ReferenceDhrystonesPerSecond))));

    }

    private static object? ReadConst(string name) =>
        typeof(Besm6Timing).GetField(name)?.GetRawConstantValue();

    [TestMethod]
    public void Observe_AccumulatesInstructionsAndModeledCycles()
    {
        var profiler = new OpcodeProfiler();
        profiler.Observe((uint)Opcode.ADivX); // 12
        profiler.Observe((uint)Opcode.ADivX); // 12
        profiler.Observe((uint)Opcode.Aax);   // 1

        Assert.AreEqual(3L, profiler.TotalInstructions);
        Assert.AreEqual(25L, profiler.TotalCycles);
        Assert.AreEqual(2L, profiler.CountOf(Opcode.ADivX));
        Assert.AreEqual(1L, profiler.CountOf(Opcode.Aax));
        Assert.AreEqual(25.0 / 3.0, profiler.CyclesPerInstruction, 1e-9);
    }

    [TestMethod]
    public void Observe_OpcodeOutsideTable_IsCountedAsUnknown()
    {
        var profiler = new OpcodeProfiler();
        profiler.Observe(0x40); // 100 oct — не команда и не экстракод
        profiler.Observe((uint)Opcode.Xta);

        Assert.AreEqual(2L, profiler.TotalInstructions);
        Assert.AreEqual(1L, profiler.UnknownOpcodeInstructions);
    }

    [TestMethod]
    public void TopOpcodes_SortedByCountDescending()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 5; i++)
            profiler.Observe((uint)Opcode.Xta);
        for (int i = 0; i < 9; i++)
            profiler.Observe((uint)Opcode.Uj);
        profiler.Observe((uint)Opcode.APlusX);

        IReadOnlyList<OpcodeStat> top = profiler.TopOpcodes(2);
        Assert.AreEqual(2, top.Count);
        Assert.AreEqual(Opcode.Uj, top[0].Opcode);
        Assert.AreEqual(9L, top[0].Count);
        Assert.AreEqual(Opcode.Xta, top[1].Opcode);
        Assert.AreEqual(5L, top[1].Count);
    }

    [TestMethod]
    public void ComputeDhrystone_MatchesModeledTimeFormula()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 100; i++)
            profiler.Observe((uint)Opcode.ADivX); // 100 × 12 = 1200 тактов

        // workload = 1200 - 200 = 1000 тактов; 1000 × 100 нс / 10 проходов = 10 мкс/проход.
        DhrystoneMetrics metrics = profiler.ComputeDhrystone(passes: 10, baselineCycles: 200);

        Assert.IsTrue(metrics.Measurable);
        Assert.AreEqual(10L, metrics.Passes);
        Assert.AreEqual(1000L, metrics.Cycles);
        Assert.AreEqual(1e-5, metrics.SecondsPerPass, 1e-15);
        Assert.AreEqual(1e5, metrics.DhrystonesPerSecond, 1e-6);
        Assert.AreEqual(1e5 / 1757.0, metrics.VaxMips, 1e-9);
    }

    [TestMethod]
    public void ComputeWhetstone_MatchesModeledTimeFormula()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 100; i++)
            profiler.Observe((uint)Opcode.ADivX); // 100 × 12 = 1200 тактов

        // loopCycles=1000: 1000 × 100 нс = 1e-4 c; MOPS = 1500/1e6/1e-4 = 15.
        WhetstoneMetrics m = profiler.ComputeWhetstone(operations: 1500, loopCycles: 1000);

        Assert.IsTrue(m.Measurable);
        Assert.AreEqual(1500L, m.Operations);
        Assert.AreEqual(1000L, m.Cycles);
        Assert.AreEqual(1e-4, m.Seconds, 1e-15);
        Assert.AreEqual(15.0, m.Mops, 1e-9);
        Assert.AreEqual(150.0, m.Mwips, 1e-9);
        Assert.AreEqual(1.5, m.OperationsPerCycle, 1e-12);
        Assert.AreEqual(150.0 / Besm6Timing.ReferenceMwips, m.VaxMips, 1e-9);
    }

    [TestMethod]
    public void ComputeWhetstone_LoopCyclesTakesPrecedenceOverBaseline()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 100; i++)
            profiler.Observe((uint)Opcode.ADivX);

        // При заданных loopCycles базовая поправка не применяется.
        WhetstoneMetrics withLoops =
            profiler.ComputeWhetstone(1500, baselineCycles: 200, loopCycles: 1000);
        WhetstoneMetrics withoutLoops =
            profiler.ComputeWhetstone(1500, baselineCycles: 200);

        Assert.AreEqual(1000L, withLoops.Cycles);
        Assert.AreEqual(1000L, withoutLoops.Cycles);
    }

    [TestMethod]
    public void ComputeWhetstone_WithoutOperationsOrCycles_IsNotMeasurable()
    {
        var profiler = new OpcodeProfiler();
        Assert.IsFalse(profiler.ComputeWhetstone(operations: 0).Measurable);
        Assert.IsFalse(profiler.ComputeWhetstone(operations: 1500, baselineCycles: 100_000)
            .Measurable);
    }

    [TestMethod]
    public void FormatWhetstone_EmitsMipsAndMwipsLines()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 100; i++)
            profiler.Observe((uint)Opcode.ADivX);

        string text = profiler.FormatWhetstone(1500, loopCycles: 1000);

        StringAssert.Contains(text, "Whetstone Benchmark");
        StringAssert.Contains(text, "Total MOPS:");
        StringAssert.Contains(text, "MWIPS rating:");
        StringAssert.Contains(text, "15.0000");
    }

    [TestMethod]
    public void ComputeDhrystone_WithoutPassesOrCycles_IsNotMeasurable()
    {
        var profiler = new OpcodeProfiler();
        Assert.IsFalse(profiler.ComputeDhrystone(passes: 0).Measurable);
        Assert.IsFalse(profiler.ComputeDhrystone(passes: 10, baselineCycles: 100_000).Measurable);
    }

    [TestMethod]
    public void FormatDhrystone_EmitsOriginalStyleMetricLines()
    {
        var profiler = new OpcodeProfiler();
        for (int i = 0; i < 100; i++)
            profiler.Observe((uint)Opcode.ADivX);

        string text = profiler.FormatDhrystone(passes: 10, baselineCycles: 200);

        StringAssert.Contains(text, "Dhrystone Benchmark, Version 2.1");
        StringAssert.Contains(text, "Dhrystones per Second:");
        StringAssert.Contains(text, "VAX MIPS rating:");
        StringAssert.Contains(text, "Number of runs:");
    }

    [TestMethod]
    public void FormatDhrystone_ForNotMeasurable_ExplainsInsteadOfThrowing()
    {
        var profiler = new OpcodeProfiler();
        string text = profiler.FormatDhrystone(passes: 0);
        StringAssert.Contains(text, "недостаточно данных");
    }

    [TestMethod]
    public void FormatSummary_UsesInvariantFormatting()
    {
        // Регрессия на локаль/разделитель: формат должен быть invariant.
        var profiler = new OpcodeProfiler();
        profiler.Observe((uint)Opcode.ADivX);

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            string text = profiler.FormatSummary();

            StringAssert.Contains(text, "Инструкций: 1");
            StringAssert.Contains(text, "Тактов (модель): 12");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void Reset_ClearsAllStatistics()
    {
        var profiler = new OpcodeProfiler();
        profiler.Observe(0x40);
        profiler.Observe((uint)Opcode.ADivX);
        profiler.Reset();

        Assert.AreEqual(0L, profiler.TotalInstructions);
        Assert.AreEqual(0L, profiler.TotalCycles);
        Assert.AreEqual(0L, profiler.UnknownOpcodeInstructions);
        Assert.AreEqual(0L, profiler.CountOf(Opcode.ADivX));
        Assert.AreEqual(0, profiler.TopOpcodes(10).Count);
    }

    [TestMethod]
    public void Mnemonic_ReturnsBemshNames_AndOctalExtracodePrefix()
    {
        Assert.AreEqual("сч", OpcodeProfiler.Mnemonic(Opcode.Xta));
        Assert.AreEqual("дел", OpcodeProfiler.Mnemonic(Opcode.ADivX));
        Assert.AreEqual("выпр", OpcodeProfiler.Mnemonic(Opcode.Ij));
        // 064 oct (экстракод вывода) = 52 dec = 0x34.
        StringAssert.StartsWith(OpcodeProfiler.Mnemonic((Opcode)0x34), "эк");

    }
}
