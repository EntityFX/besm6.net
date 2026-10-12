using Besm6;

namespace Besm6.Tests;

[TestClass]
public sealed class FastAccountingTests
{
    private static Word48 Code(string text) => new(Besm6.Asm.Assembler.Asm(text));

    [TestMethod]
    public void AlreadyReachedCycleBoundaryDeliversDueEventsWithoutExecutingACommand()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("vtm 7(1), stop") }, 1);
        bool delivered = false;
        machine.Scheduler.Schedule(0, () => delivered = true);
        using var accounting = new ExecutionAccountingSession(machine.Cpu);
        long completed = 0;
        Assert.IsFalse(machine.ExecuteBlock(256, ref completed, 0));
        Assert.IsTrue(delivered);
        Assert.AreEqual(0L, completed);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0L, accounting.TotalInstructions);
        Assert.AreEqual(0u, machine.Cpu.GetM(1));
    }

    [TestMethod]
    public void CycleBoundaryStopsAfterTheCrossingInstructionWithoutAnExtraStep()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("vtm 1(1), vtm 2(2)"), Code("xta 20, stop") }, 1);
        using var accounting = new ExecutionAccountingSession(machine.Cpu);
        long completed = 0;
        Assert.IsFalse(machine.ExecuteBlock(256, ref completed, 3));
        Assert.AreEqual(3L, completed);
        Assert.AreEqual(3UL, machine.Clock.Tick);
        Assert.AreEqual(4L, accounting.TotalCycles);
        Assert.AreEqual(3L, accounting.TotalInstructions);
        Assert.AreEqual(2u, machine.Cpu.GetK());
        Assert.IsTrue(machine.Cpu.RightInstruction);
        Assert.IsTrue(machine.ExecuteBlock(256, ref completed));
        Assert.AreEqual(4L, completed);
        Assert.AreEqual(6L, accounting.TotalCycles);
    }

    [TestMethod]
    public void FastAndObservedAccountingMatchArithmeticStateAndMemory()
    {
        var machines = new[] { new MachineCore(), new MachineCore() };
        var seen = new List<Opcode>();
        machines[1].Cpu.InstructionExecuted = seen.Add;
        foreach (var machine in machines)
        {
            machine.LoadProgram(new[] { Code("xta 20, a+x 21"), Code("a*x 22, atx 23"), Code("vtm 7(2), stop") }, 1);
            machine.Memory.Write(16, Word48.FromDouble(1.25));
            machine.Memory.Write(17, Word48.FromDouble(0.5));
            machine.Memory.Write(18, Word48.FromDouble(2.0));
            var loop = new ExecutionLoop(machine) { CollectStatistics = true, InstructionLimit = 20 };
            Assert.IsTrue(loop.Run().Success);
            Assert.AreEqual(6L, loop.Statistics!.CompletedInstructions);
            Assert.AreEqual(16L, loop.Statistics.ModelCycles);
            Assert.IsNull(machine.Cpu.InstructionAccounting);
        }
        Assert.AreEqual(6, seen.Count);
        Assert.AreEqual(machines[0].Cpu.GetA(), machines[1].Cpu.GetA());
        Assert.AreEqual(machines[0].Cpu.GetY(), machines[1].Cpu.GetY());
        Assert.AreEqual(machines[0].Cpu.GetR(), machines[1].Cpu.GetR());
        Assert.AreEqual(machines[0].Cpu.C, machines[1].Cpu.C);
        Assert.AreEqual(machines[0].Cpu.ApplyC, machines[1].Cpu.ApplyC);
        for (int index = 0; index < 16; index++)
            Assert.AreEqual(machines[0].Cpu.GetM(index), machines[1].Cpu.GetM(index));
        for (uint address = 0; address < machines[0].Memory.Size; address++)
            Assert.AreEqual(machines[0].Memory.Read(address), machines[1].Memory.Read(address));
    }

    [TestMethod]
    public void EventCanModifyTheRightHalfAndInstallAListenerDuringCountedBlock()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("vtm 1(1), vtm 2(2)"), Code("vtm 3(3), vtm 4(4)"), Code("stop") }, 1);
        int observed = 0;
        machine.Scheduler.Schedule(3, () =>
        {
            machine.Memory.Write(2, Code("vtm 3(3), vtm 7(4)"));
            machine.Cpu.InstructionExecuted = _ => observed++;
        });
        var loop = new ExecutionLoop(machine) { CollectStatistics = true, InstructionLimit = 20 };
        Assert.IsTrue(loop.Run().Success);
        Assert.AreEqual(7u, machine.Cpu.GetM(4));
        Assert.AreEqual(2, observed);
        Assert.AreEqual(5UL, machine.Clock.Tick);
        Assert.AreEqual(5L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(6L, loop.Statistics.ModelCycles);
        Assert.IsNotNull(machine.Cpu.InstructionExecuted);
    }

    [TestMethod]
    public void ArithmeticFaultAndInterceptHaveNotificationCountsButNoExtraTick()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("a/x 2"), Word48.Zero, Code("stop") }, 1);
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 3;
        var loop = new ExecutionLoop(machine) { CollectStatistics = true, InstructionLimit = 20 };
        Assert.IsTrue(loop.Run().Success);
        Assert.AreEqual(1L, loop.InstructionsExecuted);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(2L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(14L, loop.Statistics.ModelCycles);
        Assert.IsNull(machine.Cpu.InstructionAccounting);
    }

    [TestMethod]
    public void ThrowingStopIsAccountedOnceAcrossBothCanonicalCompletions()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("*50 1") }, 1);
        machine.Cpu.ExtracodeDispatch = _ => throw new ProcessorException("");
        var loop = new ExecutionLoop(machine) { CollectStatistics = true, InstructionLimit = 20 };
        Assert.IsTrue(loop.Run().Success);
        Assert.AreEqual(0L, loop.InstructionsExecuted);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(1L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(50L, loop.Statistics.ModelCycles);
    }

    [TestMethod]
    public void AccountingIsNotADiagnosticSubscriptionAndRestoresCacheSelection()
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("*50 1"), Code("stop") }, 1);
        machine.Cpu.InstructionCacheEnabled = false;
        machine.Cpu.ExtracodeDispatch = _ =>
        {
            Assert.IsNull(machine.Cpu.InstructionExecuted);
            Assert.IsNotNull(machine.Cpu.InstructionAccounting);
            Assert.IsTrue(machine.Cpu.InstructionCacheEnabled);
            return true;
        };
        var loop = new ExecutionLoop(machine) { Speed = ExecutionSpeed.Original, InstructionLimit = 20 };
        Assert.IsTrue(loop.Run().Success);
        Assert.AreEqual(2L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(52L, loop.Statistics.ModelCycles);
        Assert.IsNull(machine.Cpu.InstructionAccounting);
        Assert.IsFalse(machine.Cpu.InstructionCacheEnabled);
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    [DataRow(ExecutionSpeed.Virtual)]
    public void CallbackFailureRetainsAccountingAndReleasesTheRunSession(ExecutionSpeed speed)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("vtm 7(1), stop") }, 1);
        machine.Cpu.InstructionCacheEnabled = false;
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 10;
        var cause = new InvalidOperationException("host callback");
        machine.Scheduler.Schedule(1, () => throw cause);
        var loop = new ExecutionLoop(machine) { Speed = speed, CollectStatistics = true };

        var failure = Assert.ThrowsExactly<SchedulerCallbackException>(() => loop.Run());
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(7u, machine.Cpu.GetM(1));
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1L, loop.InstructionsExecuted);
        Assert.AreEqual(1L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(1L, loop.Statistics.ModelCycles);
        Assert.AreEqual(1u, machine.Cpu.GetK());
        Assert.IsTrue(machine.Cpu.RightInstruction);
        Assert.AreEqual(1, machine.Cpu.InterceptCount);
        Assert.IsNull(machine.Cpu.InstructionAccounting);
        Assert.IsFalse(machine.Cpu.InstructionCacheEnabled);

        // The failed callback is consumed. Resume at the right-half STOP with a
        // fresh session; prior accounting is not carried into this run.
        Assert.IsTrue(loop.Run().Success);
        Assert.AreEqual(1L, loop.Statistics!.CompletedInstructions);
        Assert.AreEqual(2L, loop.Statistics.ModelCycles);
        Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.IsNull(machine.Cpu.InstructionAccounting);
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    [DataRow(ExecutionSpeed.Virtual)]
    public void RepeatedRunsHaveIndependentCountsAndPreserveTheExternalListener(ExecutionSpeed speed)
    {
        var machine = new MachineCore();
        machine.LoadProgram(new[] { Code("vtm 7(1), stop") }, 1);
        int observed = 0;
        Action<Opcode> listener = _ => observed++;
        machine.Cpu.InstructionExecuted = listener;
        var loop = new ExecutionLoop(machine) { Speed = speed, CollectStatistics = true };
        for (int run = 0; run < 2; run++)
        {
            machine.Cpu.StartAt(1);
            Assert.IsTrue(loop.Run().Success);
            Assert.AreEqual(2L, loop.Statistics!.CompletedInstructions);
            Assert.AreEqual(3L, loop.Statistics.ModelCycles);
            Assert.AreSame(listener, machine.Cpu.InstructionExecuted);
            Assert.IsNull(machine.Cpu.InstructionAccounting);
        }
        Assert.AreEqual(4, observed);
        Assert.AreEqual(4UL, machine.Clock.Tick);
    }
}
