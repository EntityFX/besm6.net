namespace Besm6.Tests;

[TestClass]
[TestCategory("Supervisor")]
public sealed class SupervisorRunTests
{
    private static uint Half(Opcode opcode, uint address = 0, uint register = 0) =>
        (register << 20) | ((uint)opcode << 12) | address;

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    [Timeout(5000)]
    public void FaultingVectorExhaustsHostAttemptLimitWithoutInventingInstructionsOrTicks(ExecutionSpeed speed)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new(ControlUnitFlags.None);
        machine.Cpu.Supervisor.Mode = SupervisorMode.Mathematical;
        machine.Cpu.StartAt(8); // RP0 is closed; vector00500 is a numeric word, not commands.
        int observed = 0;
        machine.Cpu.InstructionExecuted += _ => observed++;
        var result = machine.RunInstructions(3, speed, true);
        Assert.IsTrue(result.Outcome.LimitExceeded);
        Assert.AreEqual(0L, result.Outcome.Instructions);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0, observed);
        Assert.AreEqual(0L, result.Statistics!.CompletedInstructions);
        Assert.AreEqual(0L, result.Statistics.ModelCycles);
        Assert.AreEqual(0x140u, machine.Cpu.GetK());
        Assert.AreEqual((1UL << 13) | (1UL << 14), machine.Cpu.Supervisor.InternalInterrupts);
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    public void MixedSuccessfulAndFaultingAttemptsCountOnlyCompletedGuestCommands(ExecutionSpeed speed)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new(((ulong)Half(Opcode.Vtm, 99, 1) << 24) | Half(Opcode.Xta, 100)));
        machine.MappedMemory!.PhysicalMemory.WriteRaw(100, new MemoryWord50(0));
        machine.Cpu.Supervisor!.Status = new(ControlUnitFlags.None);
        machine.Cpu.StartAt(8);
        var result = machine.RunInstructions(3, speed, true);
        Assert.IsTrue(result.Outcome.LimitExceeded);
        Assert.AreEqual(1L, result.Outcome.Instructions);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1L, result.Statistics!.CompletedInstructions);
        Assert.AreEqual((long)Besm6Timing.CyclesOf(Opcode.Vtm), result.Statistics.ModelCycles);
        Assert.AreEqual(99u, machine.Cpu.GetM(1));
    }

    [TestMethod]
    public void StopAtTheExactAttemptBoundIsSuccessful()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new((ulong)Half(Opcode.Stop) << 24));
        machine.Cpu.StartAt(8);
        var result = machine.RunInstructions(1, ExecutionSpeed.Max, true);
        Assert.IsTrue(result.Outcome.Success);
        Assert.IsTrue(result.Outcome.Stopped);
        Assert.IsFalse(result.Outcome.LimitExceeded);
        Assert.AreEqual(1L, result.Outcome.Instructions);
        Assert.AreEqual(1UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void CallbackFailurePreservesCompletedCommandAndAllowsAnotherBoundedRun()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new(((ulong)Half(Opcode.Vtm, 99, 1) << 24) | Half(Opcode.Stop)));
        machine.Cpu.StartAt(8);
        var cause = new InvalidOperationException("Test callback failure");
        int observed = 0;
        machine.Cpu.InstructionExecuted += _ => observed++;
        machine.Scheduler.Schedule(1, () => throw cause);
        var failure = Assert.ThrowsExactly<SchedulerCallbackException>(() => machine.RunInstructions(5, ExecutionSpeed.Max, true));
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1, observed);
        Assert.AreEqual(99u, machine.Cpu.GetM(1));
        var result = machine.RunInstructions(1, ExecutionSpeed.Max, true);
        Assert.IsTrue(result.Outcome.Stopped);
        Assert.AreEqual(1L, result.Outcome.Instructions);
        Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.AreEqual(2, observed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HooksInstalledByCallbackSurviveSuccessfulAndExceptionalLoopCleanup(bool fail)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new(((ulong)Half(Opcode.Vtm, 99, 1) << 24) | Half(Opcode.Stop)));
        machine.Cpu.StartAt(8);
        int steps = 0, cpuTraces = 0;
        Action<int, ulong> stepHook = (_, _) => steps++;
        Action<uint, bool, uint, uint> cpuHook = (_, _, _, _) => cpuTraces++;
        Action<string, ulong> registerHook = (_, _) => { };
        var cause = new InvalidOperationException("Hook installation callback failed");
        machine.Scheduler.Schedule(1, () =>
        {
            machine.StepTrace = stepHook;
            machine.Cpu.TraceInstruction = cpuHook;
            machine.RegisterTrace = registerHook;
            if (fail) throw cause;
        });
        if (fail)
            Assert.ThrowsExactly<SchedulerCallbackException>(() => machine.RunInstructions(5));
        else
            Assert.IsTrue(machine.RunInstructions(5).Outcome.Stopped);
        Assert.AreSame(stepHook, machine.StepTrace);
        Assert.AreSame(cpuHook, machine.Cpu.TraceInstruction);
        Assert.AreSame(registerHook, machine.RegisterTrace);
        if (fail)
            Assert.IsTrue(machine.RunInstructions(1).Outcome.Stopped);
        Assert.AreEqual(1, steps);
        Assert.AreEqual(1, cpuTraces);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CallbackReplacementOrRemovalOfCallerHooksIsNotUndone(bool remove)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(8, new((ulong)Half(Opcode.Stop) << 24));
        machine.Cpu.StartAt(8);
        machine.StepTrace = (_, _) => Assert.Fail("Removed caller hook was resurrected");
        machine.Cpu.TraceInstruction = (_, _, _, _) => Assert.Fail("Removed CPU hook was resurrected");
        machine.RegisterTrace = (_, _) => Assert.Fail("Removed register hook was resurrected");
        Action<int, ulong>? stepHook = remove ? null : (_, _) => { };
        Action<uint, bool, uint, uint>? cpuHook = remove ? null : (_, _, _, _) => { };
        Action<string, ulong>? registerHook = remove ? null : (_, _) => { };
        machine.Scheduler.Schedule(0, () =>
        {
            machine.StepTrace = stepHook;
            machine.Cpu.TraceInstruction = cpuHook;
            machine.RegisterTrace = registerHook;
        });
        Assert.IsTrue(machine.RunInstructions(1).Outcome.Stopped);
        Assert.AreSame(stepHook, machine.StepTrace);
        Assert.AreSame(cpuHook, machine.Cpu.TraceInstruction);
        Assert.AreSame(registerHook, machine.RegisterTrace);
    }

    [TestMethod]
    [DoNotParallelize]
    public void TraceWriterInitializationFailureDoesNotRemoveUnownedCallerHooks()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        Action<int, ulong> stepHook = (_, _) => { };
        Action<uint, bool, uint, uint> cpuHook = (_, _, _, _) => { };
        Action<string, ulong> registerHook = (_, _) => { };
        machine.StepTrace = stepHook;
        machine.Cpu.TraceInstruction = cpuHook;
        machine.RegisterTrace = registerHook;
        string directory = Path.Combine(Path.GetTempPath(), "besm6-hook-init-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");
        Directory.CreateDirectory(directory);
        try
        {
            // Opening an existing directory as a trace file must fail before loop hooks attach.
            Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", directory);
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => machine.RunInstructions(1));
            Assert.AreSame(stepHook, machine.StepTrace);
            Assert.AreSame(cpuHook, machine.Cpu.TraceInstruction);
            Assert.AreSame(registerHook, machine.RegisterTrace);
            Assert.AreEqual(0UL, machine.Clock.Tick);

            // These delegates already belong to the caller. The loader has not
            // subscribed its copy when the trace writer constructor throws.
            Action<Opcode> observer = _ => { };
            Action<InstructionTraceRecord> typed = _ => { };
            machine.Cpu.InstructionExecuted = observer;
            machine.Cpu.InstructionTrace = typed;
            var loader = new DubnaLoader(machine)
            {
                InstructionExecuted = observer,
                TypedInstructionTrace = typed
            };
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => loader.RunLoaded());
            Assert.AreSame(observer, machine.Cpu.InstructionExecuted);
            Assert.AreSame(typed, machine.Cpu.InstructionTrace);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BESM6_CANON_TRACE", previous);
            Directory.Delete(directory);
        }
    }
}
