using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class HostedOriginalTimingTests
{
    private static Word48 Code(string source) => new(Besm6.Asm.Assembler.Asm(source));

    [TestMethod]
    public void JsonCoefficientsReachLoaderAndAffectOnlyOriginal()
    {
        double Run(ExecutionSpeed speed, double timeFactor, double mathFactor)
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<Besm6.Config>(
                "{\"originalTimeFactor\":" + timeFactor.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"originalMathFactor\":" + mathFactor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")!;
            config.Speed = speed;
            var machine = new MachineCore();
            var loader = Besm6.MachineFactory.CreateLoader(config, machine);
            Assert.AreEqual(timeFactor, loader.OriginalTimeFactor);
            Assert.AreEqual(mathFactor, loader.OriginalMathFactor);
            loader.CollectStatistics = true;
            loader.Output = _ => { };
            machine.LoadProgram([Code("xta 20, *50 1"), Code("stop")], 1);
            machine.Memory.Write(16, Word48.FromDouble(.5));
            Assert.IsTrue(loader.RunLoaded().Success);
            Assert.AreEqual(3L, loader.Statistics!.CompletedInstructions);
            return loader.Statistics.ModelSeconds!.Value;
        }
        double original = Run(ExecutionSpeed.Original, 1, 2);
        Assert.IsTrue(original is > .0001 and < .000105);
        Assert.AreEqual(original * 2, Run(ExecutionSpeed.Original, 2, 2), 1e-12);
        Assert.AreEqual(.00005, Run(ExecutionSpeed.Original, 1, 3) - original, 1e-12);
        foreach (var speed in new[] { ExecutionSpeed.Max, ExecutionSpeed.Virtual })
            Assert.AreEqual(Run(speed, 1, 1), Run(speed, 3, 4));
    }

    [TestMethod]
    public void ConfigAndLoaderRejectNonpositiveOrNonfiniteCoefficients()
    {
        foreach (double value in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Besm6.Config { OriginalTimeFactor = value });
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Besm6.Config { OriginalMathFactor = value });
            var loader = new DubnaLoader(new MachineCore());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => loader.OriginalTimeFactor = value);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => loader.OriginalMathFactor = value);
        }
    }

    [TestMethod]
    public void OperandReturnDoesNotBlockIndependentControlWhenOverlapIsSelected()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters(OperandTransfersOverlapControl: true));
        ulong ready = calendar.Read(8);
        calendar.Execute(330, 1210, ready);
        Assert.AreEqual(330UL, calendar.ControlNanoseconds);
        Assert.AreEqual(2110UL, calendar.Nanoseconds);
        calendar.Execute(440, 0, calendar.ControlNanoseconds);
        Assert.AreEqual(770UL, calendar.ControlNanoseconds);
        calendar.WaitForArithmetic();
        Assert.AreEqual(2110UL, calendar.ControlNanoseconds);
    }

    [TestMethod]
    public void RetainedStoreRemainsABufferHitAfterWritebackCompletes()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters(RetainStoredOperands: true));
        calendar.Store(8);
        calendar.Execute(3000, 0, 0);
        Assert.AreEqual(3220UL, calendar.Read(8));
    }

    [TestMethod]
    public void ImpossibleCalibrationParametersAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HostedTimingCalendar(new HostedTimingParameters(ModelTimeFactor: double.NaN)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HostedTimingCalendar(new HostedTimingParameters(OperandBufferCapacity: 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HostedTimingCalendar(new HostedTimingParameters(InstructionFetchOverlapNanoseconds: 901)));
    }

    [TestMethod]
    public void IndependentControlOverlapsArithmeticButConsumerWaits()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters());
        calendar.Execute(330, 5500, 0); // Book divide: 3 UU / 50 AU cycles, 110 ns.
        Assert.AreEqual(5500UL, calendar.Nanoseconds);
        calendar.Execute(440, 0, calendar.ControlNanoseconds); // independent modifier
        Assert.AreEqual(770UL, calendar.ControlNanoseconds);
        Assert.AreEqual(5500UL, calendar.Nanoseconds);
        calendar.WaitForArithmetic();
        Assert.AreEqual(5500UL, calendar.ControlNanoseconds);
        calendar.Execute(330, 1210, calendar.ControlNanoseconds); // dependent addition
        Assert.AreEqual(6710UL, calendar.Nanoseconds);
    }

    [TestMethod]
    public void SameBankWaitsWhileAnotherBankCanReturnEarlier()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters());
        Assert.AreEqual(900UL, calendar.Read(8));
        Assert.AreEqual(2900UL, calendar.Read(16));
        Assert.AreEqual(900UL, calendar.Read(9));
        Assert.AreEqual(220UL, calendar.Read(8)); // operand buffer hit
    }

    [TestMethod]
    public void PendingStoreForwardsAndInvalidatesInstructionBuffer()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters());
        calendar.Fetch(8);
        Assert.AreEqual(900UL, calendar.ControlNanoseconds);
        calendar.Store(8);
        Assert.AreEqual(1120UL, calendar.Read(8));
        calendar.Fetch(8); // store forwarding, no stale instruction-buffer hit
        Assert.AreEqual(1120UL, calendar.ControlNanoseconds);
        calendar.Execute(4000, 0, calendar.ControlNanoseconds);
        calendar.Fetch(8); // no forwarded entry was installed as an instruction hit
        Assert.AreEqual(6020UL, calendar.ControlNanoseconds);
    }

    [TestMethod]
    public void FullStoreBufferWaitsForCapacity()
    {
        var calendar = new HostedTimingCalendar(new HostedTimingParameters());
        for (uint address = 8; address < 16; address++) calendar.Store(address);
        Assert.AreEqual(0UL, calendar.ControlNanoseconds);
        calendar.Store(16);
        Assert.AreEqual(2000UL, calendar.ControlNanoseconds);
    }

    [TestMethod]
    public void MathAliasesHaveSameEstimatedCostAndTimerIsAService()
    {
        Assert.AreEqual(50_000UL, HostedMathTiming.Nanoseconds((Opcode)0x28, 1));
        Assert.AreEqual(50_000UL, HostedMathTiming.Nanoseconds((Opcode)0x29, 0));
        Assert.AreEqual(50_000UL, HostedMathTiming.Nanoseconds((Opcode)0x29, 1));
        Assert.AreEqual(50_000UL, HostedMathTiming.Nanoseconds((Opcode)0x2a, 0));
        Assert.AreEqual(80_000UL, HostedMathTiming.Nanoseconds((Opcode)0x2b, 0));
        Assert.AreEqual(5_000UL, HostedMathTiming.Nanoseconds((Opcode)0x28, 28808));
        Assert.AreEqual(5_000UL, HostedMathTiming.Nanoseconds((Opcode)0x29, 2));
    }

    [TestMethod]
    public void IndexedModifiedMathCallUsesItsEffectiveFunctionAndDetachesHooks()
    {
        var machine = new MachineCore();
        machine.LoadProgram([Code("vtm 1(1), utc 1"), Code("*50 (1), stop")], 1);
        var handler = new ExtracodeHandler(machine, _ => null, _ => null, _ => null);
        machine.Cpu.ExtracodeDispatch = handler.Handle;
        Action<uint, bool, uint, uint> outside = (_, _, _, _) => { };
        machine.Cpu.TraceInstruction = outside;
        using (var timing = new HostedOriginalTiming(machine.Cpu))
        {
            machine.Cpu.SetA(Word48.FromDouble(.5).Value);
            machine.Step(); machine.Step();
            ulong before = timing.Nanoseconds;
            machine.Step();
            Assert.IsTrue(timing.Nanoseconds - before is >= 59_000 and < 61_000);
        }
        Assert.AreEqual(outside, machine.Cpu.TraceInstruction);
        Assert.IsNull(machine.Cpu.InstructionExecuted);
    }

    [TestMethod]
    public void CompletedOriginalRunReportsStatisticalTimeAndLegacyModesDoNot()
    {
        foreach (var speed in new[] { ExecutionSpeed.Original, ExecutionSpeed.Max, ExecutionSpeed.Virtual })
        {
            var machine = new MachineCore();
            machine.LoadProgram([Code("vtm 1(1), stop")], 1);
            var loader = new DubnaLoader(machine) { Speed = speed, CollectStatistics = true };
            Assert.IsTrue(loader.RunLoaded().Success);
            Assert.AreEqual(3L, loader.Statistics!.ModelCycles);
            Assert.AreEqual(2L, loader.Statistics.CompletedInstructions);
            if (speed == ExecutionSpeed.Original)
            {
                Assert.AreEqual(429e-9, loader.Statistics.StatisticalModelSeconds!.Value, 1e-12);
                Assert.AreEqual(loader.Statistics.StatisticalModelSeconds, loader.Statistics.ModelSeconds);
            }
            else Assert.IsNull(loader.Statistics.StatisticalModelSeconds);
            Assert.IsNull(machine.Cpu.TraceInstruction);
            Assert.IsNull(machine.Cpu.InstructionExecuted);
        }
    }
}
