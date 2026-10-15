using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Supervisor;
using Besm6.Core;
using Besm6.Processor.Diagnostics;
using Besm6.Processor.Execution;
using Besm6.Processor.Memory;
using CpuProcessor = global::Besm6.Processor.Cpu.Processor;

namespace Besm6.Tests;

[TestClass]
public sealed class InstructionAccountingContractTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BlockHonorsCycleBoundaryWithBuiltinAndDecoratedSessions(bool useDecorator)
    {
        var memory = new CoreMemory();
        memory.Write(1, new(Assembler.Legacy.Assembler.Asm("vtm 7(1), stop")));
        var cpu = new CpuProcessor(memory) { K = 1 };
        var costs = Enumerable.Repeat(1, 256).ToArray();
        var original = cpu.BeginInstructionAccounting(costs, 1);
        var decorated = new AccountingDecorator(original) { CycleLimit = 1 };
        var host = new AccountingHost(cpu, useDecorator ? decorated : original);
        var access = new ProcessorMemoryAccess(new ProcessorDebugWatch(cpu, cpu.MutableState), memory);
        var executor = new InstructionExecutor(host, cpu.MutableState, access, cpu);
        long completed = 0;
        ulong tick = 0;
        Assert.IsFalse(executor.ExecuteUnobservedBlock(8, ref completed, ref tick));
        Assert.AreEqual(1L, completed);
        Assert.AreEqual(1UL, tick);
        Assert.AreEqual(7u, cpu.GetM(1));
        Assert.AreEqual(useDecorator ? 1 : 0, decorated.Begins);
        Assert.AreEqual(useDecorator ? 1 : 0, decorated.Completions);
        Assert.AreEqual(1L, original.TotalCycles);
        decorated.CycleLimit = long.MaxValue;
        Assert.IsTrue(executor.ExecuteUnobservedBlock(8, ref completed, ref tick));
        Assert.AreEqual(2L, completed);
        Assert.AreEqual(2UL, tick);
        Assert.AreEqual(useDecorator ? 2 : 0, decorated.Begins);
        Assert.AreEqual(useDecorator ? 2 : 0, decorated.Completions);
        Assert.AreEqual(2L, original.TotalInstructions);
        Assert.IsFalse(original.HasPending);
        cpu.EndInstructionAccounting(original);
    }

    [TestMethod]
    public void ZeroCostCommandRemainsPendingUntilCompletedExactlyOnce()
    {
        var cpu = new CpuProcessor(new CoreMemory());
        var accounting = cpu.BeginInstructionAccounting(new int[256], 7);
        accounting.Begin(Opcode.Xta);
        Assert.IsTrue(accounting.HasPending);
        Assert.AreEqual(0L, accounting.TotalInstructions);
        accounting.Complete();
        Assert.IsFalse(accounting.HasPending);
        Assert.AreEqual(1L, accounting.TotalInstructions);
        Assert.AreEqual(0L, accounting.TotalCycles);
        accounting.Complete();
        Assert.AreEqual(1L, accounting.TotalInstructions);
        cpu.EndInstructionAccounting(accounting);
    }

    [TestMethod]
    public void CancellationAndReplacementDoNotRetireAbandonedCommands()
    {
        var cpu = new CpuProcessor(new CoreMemory());
        var cycles = new int[256];
        cycles[1] = 3;
        cycles[2] = 5;
        var accounting = cpu.BeginInstructionAccounting(cycles, 7);
        accounting.CycleLimit = 5;
        accounting.Begin((Opcode)1);
        accounting.CancelPending();
        accounting.Complete();
        Assert.IsFalse(accounting.HasPending);
        Assert.AreEqual(0L, accounting.TotalInstructions);
        accounting.Begin((Opcode)1);
        accounting.Begin((Opcode)2);
        cycles[2] = 100; // The session owns a copy of the cost table.
        Assert.IsFalse(accounting.AtCycleLimit);
        Assert.AreEqual(0L, accounting.TotalCycles);
        accounting.Complete();
        Assert.AreEqual(1L, accounting.TotalInstructions);
        Assert.AreEqual(5L, accounting.TotalCycles);
        Assert.IsTrue(accounting.AtCycleLimit);
        accounting.Begin((Opcode)1);
        cpu.EndInstructionAccounting(accounting);
        accounting.Complete();
        Assert.IsFalse(accounting.HasPending);
        Assert.AreEqual(1L, accounting.TotalInstructions);
    }

    [TestMethod]
    public void MaximumCostsAndUnknownOpcodesAccumulateWithoutIntOverflow()
    {
        var cpu = new CpuProcessor(new CoreMemory());
        var cycles = new int[256];
        cycles[255] = int.MaxValue;
        var accounting = cpu.BeginInstructionAccounting(cycles, int.MaxValue);
        foreach (var opcode in new[] { (Opcode)255, (Opcode)256, (Opcode)ushort.MaxValue })
        {
            accounting.Begin(opcode);
            Assert.IsTrue(accounting.HasPending);
            accounting.Complete();
        }
        Assert.AreEqual(3L, accounting.TotalInstructions);
        Assert.AreEqual(3L * int.MaxValue, accounting.TotalCycles);
        cpu.EndInstructionAccounting(accounting);
    }

    private sealed class AccountingDecorator(IProcessorInstructionAccounting inner) : IProcessorInstructionAccounting
    {
        internal int Begins { get; private set; }
        internal int Completions { get; private set; }
        public long CycleLimit { get => inner.CycleLimit; set => inner.CycleLimit = value; }
        public bool AtCycleLimit => inner.AtCycleLimit;
        public bool HasPending => inner.HasPending;
        public long TotalInstructions => inner.TotalInstructions;
        public long TotalCycles => inner.TotalCycles;
        public void Begin(Opcode opcode) { Begins++; inner.Begin(opcode); }
        public void Complete() { Completions++; inner.Complete(); }
        public void CancelPending() => inner.CancelPending();
    }

    private sealed class AccountingHost(IInstructionHost inner, IProcessorInstructionAccounting accounting) : IInstructionHost
    {
        public ISupervisorControl? Supervisor => inner.Supervisor;
        public bool ExecutionProhibited => inner.ExecutionProhibited;
        public bool LastStepCompleted { get => inner.LastStepCompleted; set => inner.LastStepCompleted = value; }
        public Action<uint, bool, uint, uint>? TraceInstruction => inner.TraceInstruction;
        public IProcessorInstructionAccounting? InstructionAccounting => accounting;
        public void CanonPre(ulong word, uint rk, DecodedInstruction instruction) => inner.CanonPre(word, rk, instruction);
        public void CanonPost(uint k, bool right) => inner.CanonPost(k, right);
        public void CancelInstructionTrace() => inner.CancelInstructionTrace();
        public bool DebugCheckFetch(uint address, uint opcode) => inner.DebugCheckFetch(address, opcode);
    }
}
