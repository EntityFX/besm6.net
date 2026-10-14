using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;
using Besm6.Processor.Execution;
using Besm6.Processor.Memory;
using Besm6.Runtime.Execution;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;

[TestClass]
public sealed class FunctionalExecutionPortTests
{
    [TestMethod]
    public void SimulationUsesTheSuppliedExecutionPortAndPreservesItsClockBoundary()
    {
        var memory = new CoreMemory();
        memory.Write(1, new(Assembler.Legacy.Assembler.Asm("xta 3, stop")));
        memory.Write(3, new(123));
        var cpu = new Processor(memory);
        var port = new ObservedExecution(cpu.FunctionalExecution);
        var simulation = new FunctionalProcessorSimulation(port, memory);
        Assert.IsFalse(simulation.Step());
        Assert.IsTrue(simulation.Step());
        Assert.AreEqual(2, port.Steps);
        Assert.AreEqual(2UL, simulation.Clock.Tick);
        Assert.AreEqual(123UL, cpu.A.Value);
    }

    // A substitutable execution decorator, without exposing a concrete CPU to the simulation.
    private sealed class ObservedExecution(IFunctionalProcessorExecution inner) : IFunctionalProcessorExecution
    {
        internal int Steps { get; private set; }
        public IProcessorState State => inner.State;
        public uint K => inner.K;
        public Word48 A => inner.A;
        public Word48 Y => inner.Y;
        public uint R => inner.R;
        public ReadOnlySpan<uint> M => inner.M;
        public uint C => inner.C;
        public bool ApplyC => inner.ApplyC;
        public uint EffectiveAddress => inner.EffectiveAddress;
        public bool RightInstruction => inner.RightInstruction;
        public ProcessorProfile Profile => inner.Profile;
        public ISupervisorControl? Supervisor => inner.Supervisor;
        public bool LastStepCompleted => inner.LastStepCompleted;
        public bool ExecutionProhibited
        { get => inner.ExecutionProhibited; set => inner.ExecutionProhibited = value; }
        public bool PreparedExecutionOnly
        { get => inner.PreparedExecutionOnly; set => inner.PreparedExecutionOnly = value; }
        public bool HasPreparedInstruction => inner.HasPreparedInstruction;
        public bool CanExecuteUnobservedBlock => inner.CanExecuteUnobservedBlock;
        public IProcessorInstructionAccounting? InstructionAccounting => inner.InstructionAccounting;
        public IProcessorInstructionAccounting BeginInstructionAccounting(ReadOnlySpan<int> cycles, int defaultCycles) =>
            inner.BeginInstructionAccounting(cycles, defaultCycles);
        public void EndInstructionAccounting(IProcessorInstructionAccounting session) => inner.EndInstructionAccounting(session);
        public void Reset() => inner.Reset();
        public void StartAt(uint address) => inner.StartAt(address);
        public bool Step() { Steps++; return inner.Step(); }
        public bool ExecuteUnobservedBlock(int count, ref long completed, ref ulong tick) =>
            inner.ExecuteUnobservedBlock(count, ref completed, ref tick);
    }
}
