using Besm6.Runtime.Loading;
using Besm6;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Cpu;

namespace Besm6.Tests;

[TestClass]
public sealed class ExecutionBlockTests
{
    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    [DataRow(2u)]
    public void WatchpointInsideBlockAbortsBeforeTheWatchedEffect(uint mode)
    {
        var machine = new MachineCore();
        string operation = mode == 2 ? "xta 2" : "atx 2";
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm(operation + ", vtm 7(2)")),
            new Word48(123),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")),
        }, 1);
        machine.Cpu.SetA(456);
        machine.Cpu.ArmDebugWatch(1, false, mode, mode == 0 ? 1u : 2u, 3);
        long completed = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        Assert.AreEqual(2L, completed);
        Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.AreEqual(456UL, machine.Cpu.A.Value);
        Assert.AreEqual(123UL, machine.Memory.Read(2).Value);
        Assert.AreEqual(0u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("typed")]
    [DataRow("legacy")]
    public void CpuObserversReceiveEveryInstructionIncludingStop(string observer)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("vtm 7(2), stop")) }, 1);
        int observed = 0;
        switch (observer)
        {
            case "light": machine.Cpu.InstructionExecuted = _ => observed++; break;
            case "typed": machine.Cpu.InstructionTrace = _ => observed++; break;
            case "legacy": machine.Cpu.TraceInstruction = (_, _, _, _) => observed++; break;
        }
        long completed = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        Assert.AreEqual(2L, completed);
        Assert.AreEqual(2, observed);
    }

    [TestMethod]
    public void ArithmeticInterceptAndRepeatedBareRunsPreserveCountsAndRegisters()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { InstructionLimit = 10, CollectStatistics = false };
        for (int run = 0; run < 2; run++)
        {
            machine.Reset();
            machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("a/x 2")),
                Word48.Zero, new Word48(Assembler.Legacy.Assembler.Asm("stop")) }, 1);
            machine.Cpu.InterceptCount = 1;
            machine.Cpu.InterceptAddr = 3;
            Assert.IsTrue(loader.RunLoaded().Success);
            Assert.AreEqual(1L, loader.InstructionsExecuted);
            Assert.AreEqual((ulong)(run + 1), machine.Clock.Tick);
            Assert.AreEqual(0, machine.Cpu.InterceptCount);
            Assert.AreEqual(3u, machine.Cpu.K);
            Assert.IsNull(loader.Statistics);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnobservedBlockMatchesEveryRegisterAndMemory(bool cached)
    {
        var single = new MachineCore();
        var block = new MachineCore();
        var program = new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm -7(1), vtm 3(2)")),
            new Word48(Assembler.Legacy.Assembler.Asm("xta 20, arx 21")),
            new Word48(Assembler.Legacy.Assembler.Asm("atx 20, utm 2(2)")),
            new Word48(Assembler.Legacy.Assembler.Asm("utc 1, ati 2")),
            new Word48(Assembler.Legacy.Assembler.Asm("vlm 2(1), stop")),
        };
        single.LoadProgram(program, 1); block.LoadProgram(program, 1);
        foreach (var machine in new[] { single, block })
        {
            machine.Memory.Write(16, new Word48(123));
            machine.Memory.Write(17, new Word48(7));
            machine.Cpu.InstructionCacheEnabled = cached;
        }
        long count = 0;
        while (++count < 1000 && !single.Step()) { }
        long actual = 0;
        Assert.IsTrue(block.Cpu.CanExecuteUnobservedBlock);
        Assert.IsTrue(block.ExecuteBlock(1000, ref actual));
        Assert.AreEqual(count, actual);
        Assert.AreEqual(single.Clock.Tick, block.Clock.Tick);
        Assert.AreEqual(single.Cpu.K, block.Cpu.K);
        Assert.AreEqual(single.Cpu.RightInstruction, block.Cpu.RightInstruction);
        Assert.AreEqual(single.Cpu.A, block.Cpu.A);
        Assert.AreEqual(single.Cpu.Y, block.Cpu.Y);
        Assert.AreEqual(single.Cpu.R, block.Cpu.R);
        Assert.AreEqual(single.Cpu.C, block.Cpu.C);
        Assert.AreEqual(single.Cpu.ApplyC, block.Cpu.ApplyC);
        for (int i = 0; i < 16; i++) Assert.AreEqual(single.Cpu.GetM(i), block.Cpu.GetM(i));
        for (uint i = 0; i < single.Memory.Size; i++)
            Assert.AreEqual(single.Memory.Read(i), block.Memory.Read(i));
    }

    [TestMethod]
    public void ExtracodeCanInstallMachineTraceDuringFastBlock()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 7(2), *50 1")),
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 3(2), stop")),
        }, 1);
        var ticks = new List<ulong>();
        machine.Cpu.ExtracodeDispatch = _ =>
        {
            Assert.AreEqual(1UL, machine.Clock.Tick);
            machine.StepTrace = (_, _) => ticks.Add(machine.Clock.Tick);
            return true;
        };
        long completed = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        Assert.AreEqual(4L, completed);
        CollectionAssert.AreEqual(new[] { 2UL, 3UL, 4UL }, ticks);
        Assert.AreEqual(3u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    public void SelfModificationIsSeenInsideFastBlock()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("atx 1, vtm 3(2)")),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")),
        }, 1);
        machine.Cpu.SetA(Assembler.Legacy.Assembler.Asm("atx 1, vtm 7(2)"));
        machine.Cpu.InstructionCacheEnabled = true;
        long completed = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        Assert.AreEqual(3L, completed);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    public void BlockMatchesSingleStepsIncludingTraceClockAndStop()
    {
        var single = new MachineCore();
        var block = new MachineCore();
        var program = new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm -4(1), vtm 1(2)")),
            new Word48(Assembler.Legacy.Assembler.Asm("utm 2(2), vlm 2(1)")),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")),
        };
        single.LoadProgram(program, 1);
        block.LoadProgram(program, 1);
        block.Cpu.InstructionCacheEnabled = true;
        var expected = new List<string>();
        var actual = new List<string>();
        single.StepTrace = (k, word) => expected.Add($"{single.Clock.Tick}:{k}:{word}:{single.Cpu.GetM(2)}");
        block.StepTrace = (k, word) => actual.Add($"{block.Clock.Tick}:{k}:{word}:{block.Cpu.GetM(2)}");
        long count = 0;
        do { count++; } while (!single.Step());
        long executed = 0;
        Assert.IsTrue(block.ExecuteBlock(256, ref executed));
        Assert.AreEqual(count, executed);
        Assert.AreEqual(single.Clock.Tick, block.Clock.Tick);
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("Division by zero")]
    [DataRow("")]
    public void ExceptionPreservesCountAndDoesNotExecuteFollowingInstruction(string message)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 1(2), *50 1")),
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 7(2), stop")),
        }, 1);
        machine.Cpu.ExtracodeDispatch = _ => throw new ProcessorException(message);
        long count = 0;
        try
        {
            machine.ExecuteBlock(256, ref count);
            Assert.Fail("Expected an instruction exception.");
        }
        catch (ProcessorException exception)
        {
            Assert.AreEqual(message, exception.Message);
        }
        Assert.AreEqual(1L, count);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    [DataRow(255L)]
    [DataRow(256L)]
    [DataRow(257L)]
    [DataRow(4097L)]
    public void MaxLimitIsExactAtAndBetweenBlockBoundaries(long limit)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("uj 1")) }, 1);
        var loader = new DubnaLoader(machine) { InstructionLimit = limit };
        var result = loader.RunLoaded();
        Assert.IsTrue(result.LimitExceeded);
        Assert.AreEqual(limit, loader.InstructionsExecuted);
        Assert.AreEqual((ulong)limit, machine.Clock.Tick);
        Assert.IsFalse(machine.Cpu.InstructionCacheEnabled); // Caller setting restored after Run.
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrdinaryExceptionInsideBareBlockPreservesCountsAndAllowsResume(bool cacheEnabled)
    {
        var machine = new MachineCore();
        machine.Cpu.InstructionCacheEnabled = cacheEnabled;
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 1(2), a/x 0")),
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 7(2), stop")),
        }, 1);
        long count = 0;
        var exception = Assert.Throws<ProcessorException>(() => machine.ExecuteBlock(256, ref count));
        Assert.AreEqual("Division by zero", exception.Message);
        Assert.AreEqual(1L, count);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1u, machine.Cpu.GetM(2));
        Assert.AreEqual(2u, machine.Cpu.K);
        Assert.IsFalse(machine.Cpu.RightInstruction);

        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(3L, count);
        Assert.AreEqual(3UL, machine.Clock.Tick);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    public void ExtracodeCallbackObservesAllPriorBlockTicksAndCounts()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[]
        {
            new Word48(Assembler.Legacy.Assembler.Asm("vtm 1(2), *50 1")),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")),
        }, 1);
        long count = 0;
        bool observed = false;
        machine.Cpu.ExtracodeDispatch = _ =>
        {
            Assert.AreEqual(1L, count);
            Assert.AreEqual(1UL, machine.Clock.Tick);
            observed = true;
            return true;
        };
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.IsTrue(observed);
        Assert.AreEqual(3L, count);
        Assert.AreEqual(3UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void TraceInstalledDuringBlockObservesSubsequentSteps()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("vtm 1(1), stop")) }, 1);
        int traces = 0;
        machine.Cpu.InstructionExecuted = _ => machine.StepTrace = (_, _) => traces++;
        long count = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(2L, count);
        Assert.AreEqual(2, traces);
    }
}
