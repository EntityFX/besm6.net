using Besm6;

namespace Besm6.Tests;

[TestClass]
public sealed class MachineEventTests
{
    private static Word48 Word(string instructions) => new(Besm6.Asm.Assembler.Asm(instructions));

    [TestMethod]
    public void EventsObserveCommittedCountsAndStopBeforeReturning()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 1(2), stop") }, 1);
        long completed = 0;
        var seen = new List<string>();
        machine.Scheduler.Schedule(0, () => seen.Add($"before:{completed}:{machine.Clock.Tick}"));
        machine.Scheduler.Schedule(1, () => seen.Add($"first:{completed}:{machine.Cpu.GetM(2)}"));
        machine.Scheduler.Schedule(2, () => seen.Add($"stop:{completed}:{machine.Clock.Tick}"));
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        CollectionAssert.AreEqual(new[] { "before:0:0", "first:1:1", "stop:2:2" }, seen);
        Assert.AreEqual(2L, completed);
    }

    [TestMethod]
    public void StepAndBlockMatchEventsRegistersAndMemory()
    {
        static (List<string> events, ulong a, ulong word, uint m, ulong tick, long count) Run(bool block)
        {
            var machine = new MachineCore();
            machine.LoadProgram(new[] { Word("vtm 1(2), xta 4"), Word("atx 5, stop") }, 1);
            long count = 0;
            var events = new List<string>();
            machine.Scheduler.Schedule(1, () =>
            {
                events.Add($"a:{machine.Clock.Tick}:{count}");
                machine.Memory.Write(4, new Word48(123));
                machine.Scheduler.Schedule(0, () => events.Add($"b:{machine.Clock.Tick}:{count}"));
            });
            machine.Scheduler.Schedule(4, () => events.Add($"stop:{machine.Clock.Tick}:{count}"));
            if (block) Assert.IsTrue(machine.ExecuteBlock(256, ref count));
            else while (!machine.Step(ref count)) { }
            return (events, machine.Cpu.GetA().Value, machine.Memory.Read(5).Value,
                machine.Cpu.GetM(2), machine.Clock.Tick, count);
        }
        var single = Run(false);
        var block = Run(true);
        CollectionAssert.AreEqual(single.events, block.events);
        Assert.AreEqual(single.a, block.a);
        Assert.AreEqual(single.word, block.word);
        Assert.AreEqual(single.m, block.m);
        Assert.AreEqual(single.tick, block.tick);
        Assert.AreEqual(single.count, block.count);
        Assert.AreEqual(123UL, block.word);
    }

    [TestMethod]
    public void EventCanModifyCachedRightHalfAndInstallTrace()
    {
        var machine = new MachineCore();
        machine.Cpu.InstructionCacheEnabled = true;
        machine.LoadProgram(new[] { Word("vtm 1(2), vtm 2(2)"), Word("stop") }, 1);
        // Populate the original right-half entry before replacing it.
        machine.Step(); machine.Step();
        machine.ResetCpu();
        long count = 0;
        int traces = 0;
        machine.Scheduler.Schedule(1, () =>
        {
            machine.Memory.Write(1, Word("vtm 1(2), vtm 7(2)"));
            machine.StepTrace = (_, _) => traces++;
        });
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.AreEqual(2, traces);
        Assert.AreEqual(3L, count);
        Assert.AreEqual(5UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void DiagnosticsPrecedeEventsAtTheCommittedTick()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("stop") }, 1);
        var order = new List<string>();
        long count = 0;
        machine.StepTrace = (_, _) => order.Add($"trace:{count}:{machine.Clock.Tick}");
        machine.Scheduler.Schedule(1, () => order.Add($"event:{count}:{machine.Clock.Tick}"));
        Assert.IsTrue(machine.Step(ref count));
        CollectionAssert.AreEqual(new[] { "trace:1:1", "event:1:1" }, order);
    }

    [TestMethod]
    public void EventAtBlockBoundaryChangesTheNextFetch()
    {
        var machine = new MachineCore();
        machine.Cpu.InstructionCacheEnabled = true;
        machine.LoadProgram(new[] { Word("uj 1") }, 1);
        long count = 0;
        machine.Scheduler.Schedule(256, () =>
        {
            Assert.AreEqual(256L, count);
            machine.Memory.Write(1, Word("stop"));
        });
        Assert.IsFalse(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(256UL, machine.Clock.Tick);
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(257L, count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZeroDelayScheduledByCpuHookRunsAfterInstructionCommit(bool extracode)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word(extracode ? "*50 1, stop" : "vtm 7(2), stop"), Word("stop") }, 1);
        long count = 0;
        int fired = 0;
        void Schedule()
        {
            machine.Scheduler.Schedule(0, () =>
            {
                Assert.AreEqual(1UL, machine.Clock.Tick);
                Assert.AreEqual(1L, count);
                fired++;
            });
        }
        if (extracode) machine.Cpu.ExtracodeDispatch = _ => { Schedule(); return true; };
        else machine.Cpu.InstructionExecuted = _ =>
        {
            machine.Cpu.InstructionExecuted = null;
            Schedule();
        };
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(1, fired);
        Assert.AreEqual(2L, count);
        Assert.AreEqual(2UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void EventCanArmWatchpointBeforeFollowingStore()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 1(2), utc"), Word("atx 4, stop"), Word("vtm 7(2), stop") }, 1);
        machine.Memory.Write(4, new Word48(123));
        machine.Cpu.SetA(456);
        // Arming a guest watchpoint transfers control to xfer's left half.
        machine.Scheduler.Schedule(1, () => machine.Cpu.ArmDebugWatch(2, false, 1, 4, 3));
        long count = 0;
        Assert.IsTrue(machine.ExecuteBlock(256, ref count));
        Assert.AreEqual(123UL, machine.Memory.Read(4).Value);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.AreEqual(4L, count);
        Assert.AreEqual(4UL, machine.Clock.Tick);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CpuFaultDoesNotAdvanceClockOrConsumeFutureEvent(bool block)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 1(2), a/x 4") }, 1);
        long count = 0;
        int fired = 0;
        machine.Scheduler.Schedule(2, () => fired++);
        Assert.ThrowsExactly<ProcessorException>(() =>
        {
            if (block) machine.ExecuteBlock(256, ref count);
            else { machine.Step(ref count); machine.Step(ref count); }
        });
        Assert.AreEqual(1L, count);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(0, fired);
        machine.Cpu.StartAt(3);
        machine.Memory.Write(3, Word("stop"));
        Assert.IsTrue(machine.Step(ref count));
        Assert.AreEqual(1, fired);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CallbackFailurePreservesCommittedInstructionAndRemainingEvents(bool block)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 7(2), stop") }, 1);
        long count = 0;
        var cause = new ProcessorException("Division by zero");
        var token = machine.Scheduler.Schedule(1, () => throw cause);
        int following = 0;
        machine.Scheduler.Schedule(1, () => following++);
        var exception = Assert.ThrowsExactly<SchedulerCallbackException>(() =>
        {
            if (block) machine.ExecuteBlock(256, ref count);
            else machine.Step(ref count);
        });
        Assert.AreSame(cause, exception.InnerException);
        Assert.AreEqual(token, exception.Token);
        Assert.AreEqual(1UL, exception.Tick);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1L, count);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.IsFalse(machine.Scheduler.Cancel(token));
        Assert.IsTrue(machine.Step(ref count));
        Assert.AreEqual(1, following);
        Assert.AreEqual(2L, count);
    }

    [TestMethod]
    [DataRow("step")]
    [DataRow("block")]
    [DataRow("cpu")]
    [DataRow("scheduler")]
    [DataRow("clock")]
    public void CallbackRejectsNestedExecutionAndAdvancement(string operation)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("stop") }, 1);
        long count = 0;
        machine.Scheduler.Schedule(0, () =>
        {
            switch (operation)
            {
                case "step": machine.Step(); break;
                case "block": machine.ExecuteBlock(1, ref count); break;
                case "cpu": machine.Cpu.Step(); break;
                case "scheduler": machine.Scheduler.AdvanceTo(0); break;
                case "clock": ((SimulationClock)machine.Clock).Advance(1); break;
            }
        });
        var exception = Assert.ThrowsExactly<SchedulerCallbackException>(() => machine.Step());
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0L, count);
        Assert.IsTrue(machine.Step());
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    public void CallbackProcessorExceptionDoesNotInvokeGuestIntercept(ExecutionSpeed speed)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 7(2), stop") }, 1);
        var loader = new DubnaLoader(machine) { Speed = speed, CollectStatistics = true };
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 3;
        machine.Scheduler.Schedule(1, () =>
        {
            Assert.AreEqual(1L, loader.InstructionsExecuted);
            throw new ProcessorException("Division by zero");
        });
        Assert.ThrowsExactly<SchedulerCallbackException>(() => loader.RunLoaded());
        Assert.AreEqual(1L, loader.InstructionsExecuted);
        Assert.AreEqual(1L, loader.Statistics!.CompletedInstructions);
        Assert.AreEqual(1, machine.Cpu.InterceptCount);
        Assert.AreEqual(1u, machine.Cpu.GetK());
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    [DataRow("loaded")]
    [DataRow("raw")]
    [DataRow("assembler")]
    [DataRow("job")]
    [DataRow("boot")]
    [DataRow("script")]
    public void NestedLoaderRunIsRejectedBeforeChangingCpuAndCounters(string entry)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 7(2), stop") }, 1);
        var loader = new DubnaLoader(machine) { CollectStatistics = true };
        var job = new DubJob { TransMain = 3 };
        job.RawWords.Add((long)Word("stop").Value);
        machine.Scheduler.Schedule(1, () =>
        {
            switch (entry)
            {
                case "loaded": loader.RunLoaded(); break;
                case "raw": loader.RunRawWords(job); break;
                case "assembler": loader.RunAssem(job); break;
                case "job": loader.RunJob(job, Array.Empty<string>()); break;
                case "boot": loader.BootAndRun(job); break;
                case "script": loader.RunScript("missing-nested-script.dub"); break;
            }
        });
        var exception = Assert.ThrowsExactly<SchedulerCallbackException>(() => loader.RunLoaded());
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
        Assert.AreEqual(1L, loader.InstructionsExecuted);
        Assert.AreEqual(1L, loader.Statistics!.CompletedInstructions);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.AreEqual(1u, machine.Cpu.GetK());
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ResetCpuPreservesMemoryTimeEventsDevicesAndLegacyResetContract()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 7(2), stop") }, 1);
        machine.Step();
        var device = machine.Devices.GetDevice(0x1000);
        int fired = 0;
        machine.Scheduler.Schedule(1, () => fired++);
        machine.Memory.Write(4, new Word48(123));
        machine.ResetCpu();
        Assert.AreEqual(0u, machine.Cpu.GetM(2));
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(123UL, machine.Memory.Read(4).Value);
        Assert.AreSame(device, machine.Devices.GetDevice(0x1000));
        machine.Step();
        Assert.AreEqual(1, fired);
        machine.Reset();
        Assert.AreEqual(0u, machine.Cpu.GetM(2));
        Assert.AreEqual(2UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void LoadProgramAfterRightHalfStartsOnLeftAndCanRunTwice()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 1(2), stop") }, 1);
        machine.Step();
        for (int run = 0; run < 2; run++)
        {
            machine.LoadProgram(new[] { Word("vtm 7(2), stop") }, 3);
            Assert.IsFalse(machine.Step());
            Assert.AreEqual(7u, machine.Cpu.GetM(2));
            Assert.IsTrue(machine.Step());
        }
        Assert.AreEqual(5UL, machine.Clock.Tick);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RawAndAssemblerLoadingSelectLeftWithoutResettingRegisters(bool raw)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Word("vtm 1(2), stop") }, 1);
        machine.Step();
        var programLoader = new JobProgramLoader(machine, new TapeMountService(null), null);
        DubJob job;
        if (raw)
        {
            job = new DubJob { TransMain = 3 };
            job.RawWords.Add((long)Word("vtm 7(2), stop").Value);
            programLoader.LoadRawWords(job);
        }
        else
        {
            job = JobParser.Parse(new[] { "*trans-main:3", "*assem", "vtm 7(2), stop", "*end file" });
            programLoader.LoadAssembler(job);
        }
        Assert.AreEqual(1u, machine.Cpu.GetM(2));
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.IsTrue(machine.Step());
    }
}
