using System.Text.Json;

namespace Besm6.Tests;

[TestClass]
public sealed class PreparedInstructionTests
{
    private static Processor Create(string command, bool right = false)
    {
        var cpu = new Processor(new CoreMemory());
        cpu.MemStore(1, Besm6.Asm.Assembler.Asm(command + ", " + command));
        cpu.MemStore(8, Word48.FromDouble(0.75).Value);
        cpu.SetA(Word48.FromDouble(1.25).Value);
        cpu.SetY(0x123456789);
        cpu.SetM(15, 9);
        cpu.State.IsRightHalf = right;
        return cpu;
    }

    private static string? Failure(Action action)
    {
        try { action(); return null; }
        catch (ProcessorException failure) { return failure.GetType().Name + ":" + failure.Message; }
    }

    [TestMethod]
    [DataRow("a+x 10")]
    [DataRow("a-x 10")]
    [DataRow("x-a 10")]
    [DataRow("amx 10")]
    [DataRow("a*x 10")]
    [DataRow("a/x 10")]
    [DataRow("a/x (17)")]
    [DataRow("xta 10")]
    [DataRow("atx 10")]
    [DataRow("aex 10")]
    [DataRow("aax 10")]
    [DataRow("aox 10")]
    [DataRow("anx 10")]
    [DataRow("asn 100")]
    [DataRow("yta 100")]
    [DataRow("xtr 10")]
    [DataRow("ntr 3")]
    [DataRow("utc 3")]
    [DataRow("wtc 10")]
    [DataRow("vtm 7(2)")]
    [DataRow("utm 7(2)")]
    [DataRow("ita 2")]
    [DataRow("ati 2")]
    [DataRow("vjm 10(2)")]
    [DataRow("uj 10")]
    [DataRow("stop")]
    public void SerialAndPreparedExecutionHaveIdenticalStateAndDiagnostics(string command)
    {
        foreach (bool right in new[] { false, true })
        foreach (uint mode in new[] { 0u, 1u, 2u, 3u })
        {
            var serial = Create(command, right);
            var phased = Create(command, right);
            var serialTrace = new List<string>();
            var phasedTrace = new List<string>();
            foreach (var cpu in new[] { serial, phased })
            {
                var records = ReferenceEquals(cpu, serial) ? serialTrace : phasedTrace;
                cpu.SetR(mode);
                cpu.State.C = 0;
                cpu.State.ApplyC = true; // Pending zero modification must retain its policy.
                cpu.InstructionExecuted = op => records.Add(op.ToString());
                cpu.InstructionTrace = record => records.Add(JsonSerializer.Serialize(record));
            }
            bool serialStop = false, phasedStop = false;
            string? serialFailure = Failure(() => serialStop = serial.Step());
            string? phasedFailure = Failure(() =>
            {
                var prepared = phased.PrepareInstruction();
                Assert.IsFalse(phased.LastStepCompleted);
                phasedStop = phased.CompleteInstruction(in prepared);
            });
            Assert.AreEqual(serialFailure, phasedFailure, command);
            Assert.AreEqual(serialStop, phasedStop);
            serial.CanonPost(serial.GetK(), serial.RightInstruction);
            phased.CanonPost(phased.GetK(), phased.RightInstruction);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(phased.State)));
            Assert.AreEqual(serial.LastStepCompleted, phased.LastStepCompleted);
            Assert.AreEqual(serial.State.StackCorrection, phased.State.StackCorrection);
            for (uint address = 1; address < 32; address++)
                Assert.AreEqual(serial.MemLoad(address), phased.MemLoad(address));
            CollectionAssert.AreEqual(serialTrace, phasedTrace);
            Assert.IsFalse(phased.HasPreparedInstruction);
        }
    }

    [TestMethod]
    public void FetchDoesNotPublishEffectsAndCompletionUsesTheLatchedCommand()
    {
        var cpu = Create("xta 10");
        int executed = 0;
        cpu.InstructionExecuted = _ => executed++;
        var initial = new ProcessorSnapshot(cpu.State);
        var prepared = cpu.PrepareInstruction();
        Assert.AreEqual(Opcode.Xta, prepared.Instruction!.Value.Opcode);
        Assert.AreEqual(initial.A, cpu.GetA());
        Assert.AreEqual(initial.Y, cpu.GetY());
        Assert.AreEqual(1u, cpu.GetK());
        Assert.IsFalse(cpu.RightInstruction);
        Assert.AreEqual(0, executed);
        cpu.MemStore(1, Besm6.Asm.Assembler.Asm("vtm 7(2), vtm 11(2)"));
        // Operands remain evaluated at completion until the memory driver is integrated.
        cpu.MemStore(8, 123);
        Assert.IsFalse(cpu.CompleteInstruction(in prepared));
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(0u, cpu.GetM(2));
        Assert.AreEqual(1, executed);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(9u, cpu.GetM(2)); // New right half was fetched from modified memory.
    }

    [TestMethod]
    public void LeasesRejectForeignStaleRepeatedAndConcurrentExecution()
    {
        var cpu = Create("vtm 7(2)");
        var other = Create("stop");
        var prepared = cpu.PrepareInstruction();
        var foreign = other.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in foreign));
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.Step());
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.PrepareInstruction());
        long completed = 0; ulong ticks = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.ExecuteUnobservedBlock(3, ref completed, ref ticks));
        Assert.AreEqual(0L, completed);
        Assert.AreEqual(0UL, ticks);
        Assert.IsFalse(cpu.CompleteInstruction(in prepared));
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in prepared));
        var cancelled = cpu.PrepareInstruction();
        cpu.CancelInstruction(in cancelled);
        var restarted = cpu.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CancelInstruction(in cancelled));
        cpu.Reset();
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in restarted));
        var afterReset = cpu.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in prepared));
        cpu.CompleteInstruction(in afterReset);
        Assert.AreEqual(7u, cpu.GetM(2));
    }

    [TestMethod]
    public void CancellationDropsDiagnosticRecordsAndDoesNotApplyEffects()
    {
        var cpu = Create("atx 10");
        int traces = 0;
        cpu.InstructionExecuted = _ => traces++;
        var original = cpu.MemLoad(8);
        var pending = cpu.PrepareInstruction();
        cpu.CancelInstruction(in pending);
        cpu.CanonPost(cpu.GetK(), cpu.RightInstruction);
        Assert.AreEqual(0, traces);
        Assert.AreEqual(original, cpu.MemLoad(8));
        Assert.IsFalse(cpu.RightInstruction);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(1, traces);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    [DataRow(2u)]
    public void FetchAndOperandWatchpointsAgreeWithSerialExecution(uint mode)
    {
        var serial = Create(mode == 1 ? "atx 10" : "xta 10");
        var phased = Create(mode == 1 ? "atx 10" : "xta 10");
        foreach (var cpu in new[] { serial, phased })
            cpu.ArmDebugWatch(1, false, mode, mode == 0 ? 1u : 8u, 3);
        Assert.IsFalse(serial.Step());
        var pending = phased.PrepareInstruction();
        Assert.IsFalse(phased.CompleteInstruction(in pending));
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.State)),
            JsonSerializer.Serialize(new ProcessorSnapshot(phased.State)));
    }

    [TestMethod]
    public void DiagnosticCallbacksCannotResetOrCancelInsideAPreparationTransition()
    {
        var cpu = Create("vtm 7(2)");
        cpu.TraceInstruction = (_, _, _, _) =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.Reset());
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.Step());
        };
        var prepared = cpu.PrepareInstruction();
        cpu.InstructionExecuted = _ =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.Reset());
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CancelInstruction(in prepared));
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in prepared));
        };
        // CanonPre only captures subscriptions present during preparation.
        cpu.CancelInstruction(in prepared);
        prepared = cpu.PrepareInstruction();
        Assert.IsFalse(cpu.CompleteInstruction(in prepared));
        Assert.AreEqual(7u, cpu.GetM(2));
        Assert.IsFalse(cpu.HasPreparedInstruction);
        cpu.Reset();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Division by zero")]
    public void ThrowingExtracodeKeepsTheSameInterceptAndTraceCompletion(string message)
    {
        foreach (bool right in new[] { false, true })
        {
            var cpu = Create("*50 3", right);
            cpu.InterceptCount = 1; cpu.InterceptAddr = 4;
            int count = 0;
            cpu.InstructionExecuted = _ => count++;
            cpu.ExtracodeDispatch = _ => throw new ProcessorException(message);
            var pending = cpu.PrepareInstruction();
            var failure = Assert.ThrowsExactly<ProcessorException>(() => cpu.CompleteInstruction(in pending));
            if (message.Length > 0) Assert.IsTrue(cpu.Intercept(failure.Message));
            cpu.CanonPost(cpu.GetK(), cpu.RightInstruction);
            cpu.CanonPost(cpu.GetK(), cpu.RightInstruction);
            Assert.AreEqual(1, count);
            Assert.IsFalse(cpu.HasPreparedInstruction);
        }
    }
}
