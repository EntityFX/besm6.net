using Besm6.Processor.Execution;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.State;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;
using System.Text.Json;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;


[TestClass]
public sealed class CapturedArithmeticInstructionTests
{
    private static Processor Create(string command, bool right = false)
    {
        var cpu = new Processor(new CoreMemory());
        cpu.MemStore(1, Assembler.Legacy.Assembler.Asm(command + ", " + command));
        cpu.MemStore(8, Word48.FromDouble(0.75).Value);
        cpu.SetA(Word48.FromDouble(1.25).Value);
        cpu.SetY(0x123456789);
        cpu.SetM(15, 9);
        cpu.MutableState.IsRightHalf = right;
        return cpu;
    }
    private static string? Failure(Action action)
    {
        try { action(); return null; }
        catch (ProcessorException failure) { return failure.GetType().Name + ":" + failure.Message; }
    }
    private static void Finish(Processor cpu, in PreparedInstruction command)
    {
        var operation = cpu.CaptureArithmeticOperand(in command);
        if (operation is { } input)
        {
            NormalizedArithmeticResult? result = null;
            ProcessorException? failure = null;
            try { result = input.Evaluate(cpu.A, cpu.Y); }
            catch (ProcessorException cause) { failure = cause; }
            cpu.SupplyArithmeticResult(in command, result, failure);
        }
        cpu.CompleteInstruction(in command);
    }

    [TestMethod]
    [DataRow("a+x 10")]
    [DataRow("a-x 10")]
    [DataRow("x-a 10")]
    [DataRow("amx 10")]
    [DataRow("avx 10")]
    [DataRow("a*x 10")]
    [DataRow("a/x 10")]
    [DataRow("a/x (17)")]
    [DataRow("e+x 10")]
    [DataRow("e-x 10")]
    [DataRow("e+n 101")]
    [DataRow("e-n 101")]
    public void ImmediateAndCapturedPathsShareStateAndDiagnostics(string instruction)
    {
        foreach (bool right in new[] { false, true })
        foreach (uint mode in new[] { 0u, 1u, 2u, 3u, (uint)RFlags.OvfDisable })
        {
            var serial = Create(instruction, right);
            var phased = Create(instruction, right);
            var a = new List<string>(); var b = new List<string>();
            foreach (var cpu in new[] { serial, phased })
            {
                var records = ReferenceEquals(cpu, serial) ? a : b;
                cpu.SetR(mode);
                cpu.MutableState.ApplyC = true;
                cpu.MutableState.C = 0;
                cpu.InstructionTrace = record => records.Add(JsonSerializer.Serialize(record));
                cpu.InstructionExecuted = op => records.Add(op.ToString());
            }
            var serialFailure = Failure(() => serial.Step());
            var command = phased.PrepareInstruction();
            var phasedFailure = Failure(() => Finish(phased, in command));
            Assert.AreEqual(serialFailure, phasedFailure, instruction);
            serial.CanonPost(serial.K, serial.RightInstruction);
            phased.CanonPost(phased.K, phased.RightInstruction);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(phased.State)), instruction);
            Assert.AreEqual(serial.MutableState.StackCorrection, phased.MutableState.StackCorrection);
            Assert.AreEqual(serial.LastStepCompleted, phased.LastStepCompleted);
            CollectionAssert.AreEqual(a, b);
        }
    }

    [TestMethod]
    public void AcceptedOperandAndModeAreLatchedUntilPublication()
    {
        var cpu = Create("a+x 10");
        var command = cpu.PrepareInstruction();
        var a = cpu.A; var y = cpu.Y; var r = cpu.R;
        var operation = cpu.CaptureArithmeticOperand(in command)!.Value;
        Assert.AreEqual(a, cpu.A); Assert.AreEqual(y, cpu.Y); Assert.AreEqual(r, cpu.R);
        cpu.MemStore(8, Word48.FromDouble(100).Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in command));
        Assert.IsTrue(cpu.HasPreparedInstruction);
        cpu.SupplyArithmeticResult(in command, operation.Evaluate(a, y), null);
        cpu.CompleteInstruction(in command);
        Assert.AreEqual(Word48.FromDouble(2), cpu.A);
    }

    [TestMethod]
    public void AddressModificationAndStackAreAppliedOnlyOnce()
    {
        var cpu = Create("a+x (17)");
        var command = cpu.PrepareInstruction();
        cpu.CaptureArithmeticOperand(in command);
        Assert.AreEqual(8u, cpu.GetM(15));
        Assert.AreEqual(1, cpu.MutableState.StackCorrection);
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CaptureArithmeticOperand(in command));
        cpu.SupplyArithmeticResult(in command, new(Word48.FromDouble(2), Word48.Zero, false, false), null);
        cpu.CompleteInstruction(in command);
        Assert.AreEqual(8u, cpu.GetM(15));
    }

    [TestMethod]
    public void OverflowPublishesBeforeFailureWithoutChangingFinalMode()
    {
        var cpu = Create("a*x 10");
        uint mode = cpu.R;
        var command = cpu.PrepareInstruction();
        cpu.CaptureArithmeticOperand(in command);
        var output = new NormalizedArithmeticResult(new(1UL << 39), new(123), true, true);
        cpu.SupplyArithmeticResult(in command, output, null);
        Assert.AreEqual("Arithmetic overflow", Assert.ThrowsExactly<ProcessorException>(() => cpu.CompleteInstruction(in command)).Message);
        Assert.AreEqual(output.A, cpu.A); Assert.AreEqual(output.Y, cpu.Y);
        Assert.AreEqual(mode, cpu.R);
        Assert.IsFalse(cpu.HasPreparedInstruction);
    }

    [TestMethod]
    public void CalculationFailurePreservesAccumulatorAndCanBeIntercepted()
    {
        var cpu = Create("a/x 10");
        cpu.InterceptCount = 1; cpu.InterceptAddr = 4;
        var a = cpu.A; var y = cpu.Y;
        var command = cpu.PrepareInstruction();
        cpu.CaptureArithmeticOperand(in command);
        var cause = new ProcessorException("Division by zero");
        cpu.SupplyArithmeticResult(in command, null, cause);
        Assert.AreSame(cause, Assert.ThrowsExactly<ProcessorException>(() => cpu.CompleteInstruction(in command)));
        Assert.AreEqual(a, cpu.A); Assert.AreEqual(y, cpu.Y);
        Assert.IsTrue(cpu.Intercept(cause.Message));
    }

    [TestMethod]
    public void OperandWatchpointFailureIsDeliveredOnlyAtCompletion()
    {
        var serial = Create("a+x 10"); var phased = Create("a+x 10");
        foreach (var cpu in new[] { serial, phased }) cpu.ArmDebugWatch(1, false, 2, 8, 3);
        serial.Step();
        var command = phased.PrepareInstruction();
        Assert.IsNull(phased.CaptureArithmeticOperand(in command));
        phased.CompleteInstruction(in command);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.State)),
            JsonSerializer.Serialize(new ProcessorSnapshot(phased.State)));
        Assert.AreEqual(serial.LastStepCompleted, phased.LastStepCompleted);
    }

    [TestMethod]
    public void ForeignStaleAndUnsupportedInputsCannotPublish()
    {
        var cpu = Create("a+x 10"); var other = Create("a+x 10");
        var command = cpu.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => other.CaptureArithmeticOperand(in command));
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.SupplyArithmeticResult(in command, new(), null));
        cpu.CaptureArithmeticOperand(in command);
        Assert.ThrowsExactly<ArgumentException>(() => cpu.SupplyArithmeticResult(in command, null, null));
        cpu.Reset();
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.SupplyArithmeticResult(in command, new(), null));
        var load = Create("xta 10"); var loadCommand = load.PrepareInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => load.CaptureArithmeticOperand(in loadCommand));
        load.CompleteInstruction(in loadCommand);
        Assert.AreEqual(Word48.FromDouble(0.75), load.A);
    }

    private sealed class ObservedMemory : IOperandMemory
    {
        private readonly CoreMemory _memory = new();
        internal int OperandReads;
        internal Exception? Failure;
        internal Action? Reading;
        public int Size => _memory.Size;
        public Word48 Read(uint address)
        {
            if (address == 8)
            {
                OperandReads++;
                Reading?.Invoke();
                if (Failure is { } cause) throw cause;
            }
            return _memory.Read(address);
        }
        public void Write(uint address, Word48 word) => _memory.Write(address, word);
    }

    [TestMethod]
    [DataRow("a+x 10", false)]
    [DataRow("a+x 10", true)]
    [DataRow("a*x 10", false)]
    [DataRow("a*x 10", true)]
    public void RepeatedTransferredCapturesUseEachAcceptedWordInsteadOfBackingMemory(string instruction, bool right)
    {
        var cpu = Create(instruction);
        cpu.MemStore(8, Word48.FromDouble(99).Value);
        foreach (double operand in new[] { 0.5, 2.0, 1.0, 4.0, 0.5 })
        {
            cpu.StartAt(1); cpu.MutableState.IsRightHalf = right;
            cpu.SetA(Word48.FromDouble(1).Value);
            var command = cpu.PrepareInstruction();
            var operation = cpu.CaptureTransferredArithmeticOperand(in command, 8, Word48.FromDouble(operand), null)!.Value;
            cpu.SupplyArithmeticResult(in command, operation.Evaluate(cpu.A, cpu.Y), null);
            cpu.CompleteInstruction(in command);
            Assert.AreEqual(Word48.FromDouble(instruction.StartsWith("a+x") ? 1 + operand : operand), cpu.A);
            Assert.AreEqual(Word48.FromDouble(99).Value, cpu.MemLoad(8));
            Assert.IsFalse(cpu.HasPreparedInstruction);
        }
    }

    [TestMethod]
    public void FailedTransferredCaptureReleasesItsViewBeforeTheNextCommand()
    {
        var cpu = Create("a+x 10");
        var cause = new InvalidOperationException("Accepted transfer failure");
        var failed = cpu.PrepareInstruction();
        Assert.IsNull(cpu.CaptureTransferredArithmeticOperand(in failed, 8, default, cause));
        Assert.AreSame(cause, Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in failed)));
        Assert.IsFalse(cpu.HasPreparedInstruction);
        cpu.StartAt(1); cpu.SetA(Word48.FromDouble(1).Value);
        var next = cpu.PrepareInstruction();
        var operation = cpu.CaptureTransferredArithmeticOperand(in next, 8, Word48.FromDouble(2), null)!.Value;
        cpu.SupplyArithmeticResult(in next, operation.Evaluate(cpu.A, cpu.Y), null);
        cpu.CompleteInstruction(in next);
        Assert.AreEqual(Word48.FromDouble(3), cpu.A);
    }

    [TestMethod]
    public void CompletionDoesNotReadMemoryAgainAndCaptureIsNotReentrant()
    {
        var memory = new ObservedMemory(); var cpu = new Processor(memory);
        cpu.MemStore(1, Assembler.Legacy.Assembler.Asm("a+x 10, stop"));
        cpu.MemStore(8, Word48.FromDouble(1).Value); cpu.SetA(Word48.FromDouble(1).Value);
        var command = cpu.PrepareInstruction();
        memory.Reading = () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(cpu.Reset);
            Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CaptureArithmeticOperand(in command));
        };
        var operation = cpu.CaptureArithmeticOperand(in command)!.Value;
        memory.Failure = new Exception("A second read must not happen");
        cpu.SupplyArithmeticResult(in command, operation.Evaluate(cpu.A, cpu.Y), null);
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.SupplyArithmeticResult(in command, new(), null));
        cpu.CompleteInstruction(in command);
        Assert.AreEqual(1, memory.OperandReads);
        Assert.AreEqual(Word48.FromDouble(2), cpu.A);
    }

    [TestMethod]
    public void FailedOperandReadRetainsItsCauseAndDoesNotAcceptAnArithmeticResult()
    {
        var memory = new ObservedMemory(); var cpu = new Processor(memory);
        cpu.MemStore(1, Assembler.Legacy.Assembler.Asm("a+x 10, stop"));
        var cause = new InvalidOperationException("Synthetic memory failure");
        memory.Failure = cause;
        var command = cpu.PrepareInstruction();
        Assert.IsNull(cpu.CaptureArithmeticOperand(in command));
        Assert.ThrowsExactly<InvalidOperationException>(() => cpu.SupplyArithmeticResult(in command, new(), null));
        Assert.AreSame(cause, Assert.ThrowsExactly<InvalidOperationException>(() => cpu.CompleteInstruction(in command)));
        Assert.AreEqual(1, memory.OperandReads);
        Assert.IsFalse(cpu.HasPreparedInstruction);
    }

    [TestMethod]
    public void NonzeroAddressModificationIsConsumedAtTheSameBoundary()
    {
        var serial = Create("a+x 1"); var phased = Create("a+x 1");
        foreach (var cpu in new[] { serial, phased }) { cpu.MutableState.C = 7; cpu.MutableState.ApplyC = true; }
        serial.Step();
        var command = phased.PrepareInstruction();
        Finish(phased, in command);
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(serial.State)),
            JsonSerializer.Serialize(new ProcessorSnapshot(phased.State)));
        Assert.IsFalse(phased.ApplyC);
    }

    [TestMethod]
    public void RoundedOutputMatchesIndependentExpectedBits()
    {
        var cpu = Create("a+x 10");
        var one = new Word48(Convert.ToUInt64("4050000000000000", 8));
        cpu.SetA(one.Value); cpu.MemStore(8, (24UL << 41) | (1UL << 39)); cpu.SetR(0);
        var command = cpu.PrepareInstruction();
        var operation = cpu.CaptureArithmeticOperand(in command)!.Value;
        var result = operation.Evaluate(cpu.A, cpu.Y);
        Assert.AreEqual(one, result.UnroundedA); Assert.IsTrue(result.RoundOnOutput);
        cpu.SupplyArithmeticResult(in command, result, null);
        cpu.CompleteInstruction(in command);
        Assert.AreEqual(one.Value | 1UL, cpu.A.Value);
    }

    [TestMethod]
    public void DeferredMemoryControlFaultRetainsItsOriginalBufferDetail()
    {
        var memory = new MappedMemoryBackend();
        memory.HostMemory.Write(8, new(Assembler.Legacy.Assembler.Asm("a+x 20, stop")));
        var cpu = new Processor(memory, ProcessorProfile.Supervisor);
        cpu.Supervisor!.Status = new(ControlUnitFlags.AssignmentBlocked | ControlUnitFlags.ProtectionBlocked);
        memory.InvertLeftStoreControl = true;
        memory.InvertRightStoreControl = false;
        memory.Write(16, Word48.FromDouble(1)); // Mixed control: error in BRZ slot0.
        cpu.StartAt(8);
        var command = cpu.PrepareInstruction();
        Assert.IsNull(cpu.CaptureArithmeticOperand(in command));
        Assert.AreEqual(MemoryFaultSource.OperandBuffer, memory.LastFault!.Value.Source);
        Assert.AreEqual(0UL, cpu.Supervisor.InternalInterrupts);
        memory.PhysicalMemory.WriteRaw(9, new(0));
        Assert.ThrowsExactly<MemoryControlException>(() => memory.Read(9));
        Assert.AreEqual(MemoryFaultSource.PhysicalMemory, memory.LastFault!.Value.Source);
        cpu.CompleteInstruction(in command);
        Assert.AreEqual(1UL << 20, cpu.Supervisor.InternalInterrupts);
        Assert.AreEqual(8u, cpu.Supervisor.ReadModifier(27));
        Assert.AreEqual(0x10Bu, cpu.Supervisor.SavedFlags); // Right-half flag, M17 controls3, interrupt mode8.
        Assert.IsFalse(cpu.LastStepCompleted);
    }
}
