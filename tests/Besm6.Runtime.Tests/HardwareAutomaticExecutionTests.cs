using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareAutomaticExecutionTests
{
    private static MramPortConfiguration MemoryTiming => new(new(2000), new(900), new(1500),
        MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);
    private static HardwareExecutionConfiguration Configuration(ulong control = 300) => new(MemoryTiming, new(100), new(100),
        Enum.GetValues<Opcode>().ToDictionary(opcode => opcode, _ => new HardwareDuration(control)),
        Enum.GetValues<Opcode>().ToDictionary(opcode => opcode, _ => new HardwareDuration(1200)));
    private static MachineCore Machine(string code = "xta 20, stop")
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm(code)), false, false));
        for (uint address = 16; address < 40; address++)
            machine.MappedMemory.PhysicalMemory.WriteRaw(address, MemoryWord50.Form(Word48.FromDouble(0.75), true, true));
        machine.Cpu.StartAt(8); machine.Cpu.SetA(Word48.FromDouble(1.25).Value);
        machine.Cpu.SetY(0x12345); machine.Cpu.SetM(15, 24);
        return machine;
    }
    private static string CpuState(MachineCore machine) => JsonSerializer.Serialize(new ProcessorSnapshot(machine.Cpu.State));

    [TestMethod]
    [DataRow("xta 20")] [DataRow("atx 20")] [DataRow("stx 20")] [DataRow("xts 20")]
    [DataRow("arx 20")] [DataRow("acx 20")] [DataRow("anx 20")] [DataRow("asx 20")]
    [DataRow("xtr 20")] [DataRow("sti 7")] [DataRow("its 7")] [DataRow("wtc 20")]
    [DataRow("a+x 20")] [DataRow("a-x 20")] [DataRow("x-a 20")] [DataRow("amx 20")]
    [DataRow("aax 20")] [DataRow("aex 20")] [DataRow("aox 20")] [DataRow("avx 20")]
    [DataRow("a/x 20")] [DataRow("a*x 20")] [DataRow("apx 20")] [DataRow("aux 20")]
    [DataRow("e+x 20")] [DataRow("e-x 20")] [DataRow("e+n 100")] [DataRow("e-n 100")]
    [DataRow("ntr 3")] [DataRow("asn 100")] [DataRow("rte 77")] [DataRow("yta")]
    [DataRow("ati 7")] [DataRow("ita 7")] [DataRow("mtj 7(6)")] [DataRow("j+m 7(6)")]
    [DataRow("utc 1")] [DataRow("vtm 2(7)")] [DataRow("utm 2(7)")]
    [DataRow("uza 20")] [DataRow("u1a 20")] [DataRow("uj 20")] [DataRow("vjm 20(7)")]
    [DataRow("vzm 20(7)")] [DataRow("v1m 20(7)")] [DataRow("vlm 20(7)")]
    public void AutomaticRoutingMatchesSharedStepForBothHalves(string code)
    {
        foreach (bool right in new[] { false, true })
        {
            var expected = Machine(code + ", " + code); var actual = Machine(code + ", " + code);
            expected.Cpu.State.IsRightHalf = actual.Cpu.State.IsRightHalf = right;
            expected.Cpu.Step(); actual.HardwareModel.ConfigureAutomaticExecution(Configuration());
            var result = actual.HardwareModel.AdvanceAutomaticTo(new(100000), 1);
            Assert.AreEqual(HardwareRunBoundary.InstructionLimit, result.Boundary, code);
            Assert.AreEqual(1UL, result.Completed); Assert.AreEqual(CpuState(expected), CpuState(actual), code);
            Assert.AreEqual(JsonSerializer.Serialize(expected.Cpu.Supervisor!.Snapshot()), JsonSerializer.Serialize(actual.Cpu.Supervisor!.Snapshot()), code);
            Assert.AreEqual(0UL, actual.Clock.Tick);
        }
    }

    [TestMethod]
    public void TimeLimitPreservesFetchAndContinuationRunsThroughStop()
    {
        var machine = Machine(); var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        var first = model.AdvanceAutomaticTo(new(450), 20);
        Assert.AreEqual(HardwareRunBoundary.TimeLimit, first.Boundary); Assert.AreEqual(0UL, first.Completed);
        Assert.IsTrue(model.FetchWaiting); Assert.IsTrue(machine.Cpu.HasPreparedInstruction);
        var result = model.AdvanceAutomaticTo(new(100000), 20);
        Assert.AreEqual(HardwareRunBoundary.Stop, result.Boundary); Assert.AreEqual(2UL, result.Completed);
        Assert.IsFalse(machine.Cpu.HasPreparedInstruction); Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ControlPermissionRetiresBeforeLaterSameTimeEvents()
    {
        var machine = Machine("stop, stop"); var model = machine.HardwareModel;
        model.ConfigureAutomaticExecution(Configuration(1000)); var events = new List<string>();
        machine.Cpu.TraceInstruction = (_, _, _, _) => model.Timeline.ScheduleAt(new(1900), () => events.Add("earlier"));
        model.InstructionCompleted = _ => events.Add("retire");
        // Decode at 900 schedules the earlier event, then the permission.
        Assert.AreEqual(HardwareRunBoundary.TimeLimit, model.AdvanceAutomaticTo(new(901), 1).Boundary);
        model.Timeline.ScheduleAt(new(1900), () => events.Add("later"));
        Assert.AreEqual(HardwareRunBoundary.Stop, model.AdvanceAutomaticTo(new(5000), 1).Boundary);
        CollectionAssert.AreEqual(new[] { "earlier", "retire", "later" }, events);
    }

    [TestMethod]
    public void DataReadCannotRetireBeforeControlPermission()
    {
        var machine = Machine(); var original = machine.Cpu.GetA(); var model = machine.HardwareModel;
        model.ConfigureAutomaticExecution(Configuration(10000));
        Assert.AreEqual(HardwareRunBoundary.TimeLimit, model.AdvanceAutomaticTo(new(4000), 1).Boundary);
        Assert.AreEqual(original, machine.Cpu.GetA()); Assert.IsTrue(model.HasPendingInstruction);
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, model.AdvanceAutomaticTo(new(10900), 1).Boundary);
        Assert.AreEqual(Word48.FromDouble(0.75), machine.Cpu.GetA());
    }

    [TestMethod]
    public void ResetDuringControlWaitCancelsInsteadOfPublishing()
    {
        var machine = Machine(); var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration(10000));
        model.Timeline.ScheduleAt(new(5000), machine.ResetCpu);
        var result = model.AdvanceAutomaticTo(new(20000), 5);
        Assert.AreEqual(HardwareRunBoundary.Cancelled, result.Boundary); Assert.AreEqual(0UL, result.Completed);
        Assert.AreEqual(1UL, model.CancelledInstructions); Assert.IsFalse(machine.Cpu.HasPreparedInstruction);
        Assert.AreEqual(Word48.Zero, machine.Cpu.GetA());
    }

    [TestMethod]
    public void FaultedFetchReturnsGuestBoundaryWithoutStartingStages()
    {
        var machine = Machine(); machine.MappedMemory!.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(0), true, true));
        var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        var result = model.AdvanceAutomaticTo(new(10000), 10);
        Assert.AreEqual(HardwareRunBoundary.GuestFault, result.Boundary); Assert.AreEqual(0UL, result.Completed);
        Assert.AreEqual(0x140U, machine.Cpu.GetK()); Assert.IsFalse(machine.Cpu.HasPreparedInstruction);
    }

    [TestMethod]
    public void MissingVariableTimingIsRejectedBeforeOperandEffects()
    {
        var machine = Machine("a/x 20, stop"); var before = CpuState(machine); var model = machine.HardwareModel;
        model.ConfigureAutomaticExecution(new(MemoryTiming, new(100), new(100), new Dictionary<Opcode, HardwareDuration> { [Opcode.ADivX] = new(300) }));
        Assert.ThrowsExactly<ArgumentException>(() => model.AdvanceAutomaticTo(new(10000), 1));
        Assert.AreEqual(before, CpuState(machine)); Assert.IsTrue(model.HasPendingInstruction);
        Assert.IsNull(model.LastIssuedArithmeticCommand);
    }

    [TestMethod]
    public void ConfigurationCopiesMutableTimingTables()
    {
        var controls = new Dictionary<Opcode, HardwareDuration> { [Opcode.Stop] = new(1000) };
        var configuration = new HardwareExecutionConfiguration(MemoryTiming, new(100), new(100), controls);
        controls[Opcode.Stop] = new(9000);
        var model = Machine("stop, stop").HardwareModel; model.ConfigureAutomaticExecution(configuration);
        var result = model.AdvanceAutomaticTo(new(20000), 1);
        Assert.AreEqual(new HardwareInstant(1900), result.Time);
    }

    [TestMethod]
    public void AutomaticDriverRejectsNestedRunsAndManualLease()
    {
        var machine = Machine("stop, stop"); var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        machine.Cpu.TraceInstruction = (_, _, _, _) =>
            Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceAutomaticTo(new(10000), 1));
        model.InstructionCompleted = _ => Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceAutomaticTo(new(10000), 1));
        Assert.AreEqual(HardwareRunBoundary.Stop, model.AdvanceAutomaticTo(new(10000), 1).Boundary);
        var manual = model.PrepareNextInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => model.AdvanceAutomaticTo(new(20000), 1));
        Assert.IsTrue(model.TryRequestCancellation(in manual)); model.AdvanceNextEvent();
    }

    [TestMethod]
    public void UnsupportedSpecialMemoryRouteDoesNotRunSynchronously()
    {
        var machine = Machine("mod 1, stop"); var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        Assert.ThrowsExactly<NotSupportedException>(() => model.AdvanceAutomaticTo(new(10000), 1));
        Assert.AreEqual(0UL, model.CompletedInstructions); Assert.IsTrue(model.HasPendingInstruction);
    }
    [TestMethod]
    public void MixedProgramRetiresConsecutivelyAndForwardsAcceptedStores()
    {
        var expected = Machine("xta 20, aax 21"); var actual = Machine("xta 20, aax 21");
        foreach (var machine in new[] { expected, actual })
        {
            machine.MappedMemory!.PhysicalMemory.WriteRaw(9, MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm("atx 22, xta 22")), false, false));
            machine.MappedMemory.PhysicalMemory.WriteRaw(10, MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm("e+n 100, stop")), false, false));
        }
        for (int i = 0; i < 6; i++) expected.Cpu.Step();
        var model = actual.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        var first = model.AdvanceAutomaticTo(new(100000), 3);
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, first.Boundary);
        var last = model.AdvanceAutomaticTo(new(200000), 20);
        Assert.AreEqual(HardwareRunBoundary.Stop, last.Boundary); Assert.AreEqual(3UL, last.Completed);
        Assert.AreEqual(6UL, model.CompletedInstructions); Assert.AreEqual(CpuState(expected), CpuState(actual));
        Assert.AreEqual(expected.MappedMemory!.Read(18), actual.MappedMemory!.Read(18));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Cpu.Supervisor!.Snapshot()), JsonSerializer.Serialize(actual.Cpu.Supervisor!.Snapshot()));
    }

    [TestMethod]
    public void ConfirmedFixedArithmeticDurationNeedsNoConfiguredFallback()
    {
        var machine = Machine("aax 20, stop"); var model = machine.HardwareModel;
        model.ConfigureAutomaticExecution(new(MemoryTiming, new(100), new(100),
            new Dictionary<Opcode, HardwareDuration> { [Opcode.Aax] = new(300) }));
        HardwareInstant? started = null;
        model.CreateArithmeticController(new(100)).Transitioned = transition =>
        {
            if (transition.Kind == ArithmeticStageTransitionKind.Started) started = transition.Time;
        };
        var result = model.AdvanceAutomaticTo(new(10000), 1);
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, result.Boundary); Assert.IsNotNull(started);
        Assert.AreEqual(started.Value + new HardwareDuration(400), result.Time);
    }

    [TestMethod]
    public void TimedArithmeticFaultReturnsWithoutExecutingFollowingCommand()
    {
        var machine = Machine("a/x 20, stop");
        machine.MappedMemory!.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(Word48.Zero, true, true));
        var model = machine.HardwareModel; model.ConfigureAutomaticExecution(Configuration());
        var result = model.AdvanceAutomaticTo(new(100000), 10);
        Assert.AreEqual(HardwareRunBoundary.GuestFault, result.Boundary); Assert.AreEqual(0UL, result.Completed);
        Assert.AreEqual(1UL, model.IssuedInstructions); Assert.IsFalse(machine.Cpu.HasPreparedInstruction);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void SuppressedDiagnosticFetchNeverStartsOperandOrArithmetic(bool arithmetic)
    {
        string code = arithmetic ? "a/x 20, stop" : "xta 20, stop";
        var expected = Machine(code); var actual = Machine(code);
        foreach (var machine in new[] { expected, actual }) machine.Cpu.ArmDebugWatch(8, false, 0, 8, 12);
        expected.Cpu.Step(); var model = actual.HardwareModel;
        model.ConfigureAutomaticExecution(new(MemoryTiming, new(100), new(100),
            new Dictionary<Opcode, HardwareDuration> { [arithmetic ? Opcode.ADivX : Opcode.Xta] = new(300) }));
        var result = model.AdvanceAutomaticTo(new(10000), 1);
        Assert.AreEqual(HardwareRunBoundary.InstructionLimit, result.Boundary);
        Assert.AreEqual(CpuState(expected), CpuState(actual)); Assert.IsNull(model.LastIssuedArithmeticCommand);
        Assert.AreEqual(0, model.CreateBufferedMemory(MemoryTiming, new(100)).PendingRequests);
    }

}
