using System.Text.Json;
using Besm6;

namespace Besm6.Tests;

[TestClass]
public sealed class ExecutionSpeedTests
{
    private sealed class FakeTime : IExecutionTimeSource
    {
        public double ElapsedSeconds { get; set; }
        public double OversleepSeconds { get; set; }
        public List<int> Sleeps { get; } = new();
        public void Sleep(int milliseconds)
        {
            Sleeps.Add(milliseconds);
            ElapsedSeconds += milliseconds / 1000.0 + OversleepSeconds;
        }
        public void Spin() => ElapsedSeconds += 0.00001;
    }

    [TestMethod]
    public void AbsoluteDeadlines_DoNotAccumulateOversleep()
    {
        var time = new FakeTime { OversleepSeconds = 0.010 };
        var pacer = new ExecutionPacer(time);
        Assert.IsTrue(pacer.Synchronize(100_000));
        double first = time.ElapsedSeconds;
        Assert.IsTrue(pacer.Synchronize(100_000));
        Assert.AreEqual(first, time.ElapsedSeconds);
        Assert.IsTrue(pacer.Synchronize(200_000));
        Assert.IsTrue(time.ElapsedSeconds >= 0.020 && time.ElapsedSeconds < 0.030);
        Assert.IsTrue(time.Sleeps.All(ms => ms is > 0 and <= 50));
    }

    [TestMethod]
    public void BehindSchedule_DoesNotWait()
    {
        var time = new FakeTime { ElapsedSeconds = 1 };
        Assert.IsTrue(new ExecutionPacer(time).Synchronize(100_000));
        Assert.AreEqual(1d, time.ElapsedSeconds);
        Assert.IsEmpty(time.Sleeps);
    }

    [TestMethod]
    public void Wait_IsBoundedByWallLimit()
    {
        var time = new FakeTime();
        Assert.IsFalse(new ExecutionPacer(time).Synchronize(10_000_000, 0.025));
        Assert.IsTrue(time.ElapsedSeconds >= 0.025 && time.ElapsedSeconds < 0.026);
    }

    [TestMethod]
    public void Config_DefaultsToMax_AndRejectsInvalidSpeed()
    {
        Assert.AreEqual(ExecutionSpeed.Max, new Config().Speed);
        Assert.AreEqual(ExecutionSpeed.Original, JsonSerializer.Deserialize<Config>("{\"speed\":\"original\"}")!.Speed);
        foreach (string value in new[] { "\"fast\"", "1", "null", "\"1\"", "\"max,original\"" })
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Config>("{\"speed\":" + value + "}"));
    }

    [TestMethod]
    public void RepeatedRuns_DetachObservers_AndPreserveInstructionClockUnits()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { CollectStatistics = true, InstructionLimit = 100 };
        int outside = 0, inside = 0;
        machine.Cpu.InstructionExecuted = _ => outside++;
        loader.InstructionExecuted = _ => inside++;
        ulong word = Besm6.Asm.Assembler.Asm("vtm 1(1), stop");
        for (int run = 0; run < 2; run++)
        {
            loader.Speed = run == 0 ? ExecutionSpeed.Original : ExecutionSpeed.Max;
            machine.LoadProgram(new[] { new Word48(word) }, 1);
            Assert.IsTrue(loader.RunLoaded().Success);
            Assert.AreEqual(2L, loader.Statistics!.CompletedInstructions);
            Assert.AreEqual(3L, loader.Statistics.ModelCycles);
            Assert.AreEqual((ulong)(2 * (run + 1)), machine.Clock.Tick);
            Assert.AreEqual(2 * (run + 1), outside);
            Assert.AreEqual(outside, inside);
        }
        machine.LoadProgram(new[] { new Word48(word) }, 1);
        machine.Step();
        Assert.AreEqual(5, outside);
        Assert.AreEqual(4, inside);
    }

    [TestMethod]
    public void MaxWithoutStats_DoesNotCollectTiming_AndInstructionLimitStillStops()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { InstructionLimit = 4 };
        machine.LoadProgram(new[] { new Word48(Besm6.Asm.Assembler.Asm("uj 1")) }, 1);
        Assert.IsTrue(loader.RunLoaded().LimitExceeded);
        Assert.IsNull(loader.Statistics);
        Assert.AreEqual(4L, loader.InstructionsExecuted);
        Assert.AreEqual(4UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void FailedInstructionAndIntercept_MatchFullProfiler()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { CollectStatistics = true, InstructionLimit = 10 };
        var profiler = new OpcodeProfiler();
        loader.TypedInstructionTrace = profiler.Observe;
        machine.LoadProgram(new[] { new Word48(Besm6.Asm.Assembler.Asm("a/x 2")),
            Word48.Zero, new Word48(Besm6.Asm.Assembler.Asm("stop")) }, 1);
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 3;
        Assert.IsTrue(loader.RunLoaded().Success);
        Assert.AreEqual(2L, loader.Statistics!.CompletedInstructions);
        Assert.AreEqual(profiler.TotalInstructions, loader.Statistics.CompletedInstructions);
        Assert.AreEqual(profiler.TotalCycles, loader.Statistics.ModelCycles);
    }
}
