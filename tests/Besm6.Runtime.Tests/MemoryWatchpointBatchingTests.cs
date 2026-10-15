using System.Text.Json;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Machine;

namespace Besm6.Tests;

[TestClass]
public sealed class MemoryWatchpointBatchingTests
{
    private static Word48 Code(string text) => new(Assembler.Legacy.Assembler.Asm(text));

    [TestMethod]
    public void JsonPolicyDefaultsOffAndReachesNewAndSuppliedMachines()
    {
        Assert.IsFalse(new Config().MemoryWatchpointBatching);
        var config = JsonSerializer.Deserialize<Config>("{\"memoryWatchpointBatching\":true}")!;
        var machine = MachineFactory.CreateMachine(config);
        Assert.IsTrue(machine.Cpu.MemoryWatchpointBatchingEnabled);
        machine.Reset();
        Assert.IsTrue(machine.Cpu.MemoryWatchpointBatchingEnabled);
        var supplied = new MachineCore();
        MachineFactory.CreateLoader(config, supplied);
        Assert.IsTrue(supplied.Cpu.MemoryWatchpointBatchingEnabled);
        MachineFactory.CreateLoader(new Config(), supplied);
        Assert.IsFalse(supplied.Cpu.MemoryWatchpointBatchingEnabled);
    }

    [TestMethod]
    public void PolicyAllowsOnlyMemoryWatchesAndKeepsOtherGuards()
    {
        var machine = new MachineCore();
        var cpu = machine.Cpu;
        cpu.ArmDebugWatch(1, false, 1, 16, 8);
        Assert.IsFalse(cpu.CanExecuteUnobservedBlock);
        cpu.MemoryWatchpointBatchingEnabled = true;
        Assert.IsTrue(cpu.CanExecuteUnobservedBlock);
        cpu.TraceInstruction = (_, _, _, _) => { };
        Assert.IsFalse(cpu.CanExecuteUnobservedBlock);
        cpu.TraceInstruction = null;
        cpu.InstructionExecuted = _ => { };
        Assert.IsFalse(cpu.CanExecuteUnobservedBlock);
        cpu.InstructionExecuted = null;
        cpu.ArmDebugWatch(1, false, 0, 1, 8);
        Assert.IsFalse(cpu.CanExecuteUnobservedBlock);
    }

    [TestMethod]
    [DataRow("atx 20", 1u, false)]
    [DataRow("atx 20", 1u, true)]
    [DataRow("xta 20", 2u, false)]
    [DataRow("xta 20", 2u, true)]
    [DataRow("stx 20", 1u, false)]
    [DataRow("stx 20", 1u, true)]
    [DataRow("xts 20", 2u, false)]
    [DataRow("xts 20", 2u, true)]
    public void BatchedWatchesMatchStepsIncludingPartialStackEffects(string operation, uint mode, bool right)
    {
        foreach (bool trigger in new[] { false, true })
        foreach (bool counted in new[] { false, true })
        foreach (bool cached in new[] { false, true })
        {
            var expected = Setup(false);
            var actual = Setup(true);
            using var serialAccounting = counted ? new ExecutionAccountingSession(expected.Cpu) : null;
            using var batchAccounting = counted ? new ExecutionAccountingSession(actual.Cpu) : null;
            long serialCount = 0, batchCount = 0;
            bool stopped = false;
            while (serialCount < 32 && !(stopped = expected.Step(ref serialCount))) { }
            Assert.IsTrue(stopped);
            Assert.IsTrue(actual.Cpu.CanExecuteUnobservedBlock);
            Assert.IsTrue(actual.ExecuteBlock(32, ref batchCount));
            Assert.AreEqual(serialCount, batchCount);
            Assert.AreEqual(expected.Clock.Tick, actual.Clock.Tick);
            Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)),
                JsonSerializer.Serialize(new ProcessorSnapshot(actual.Cpu.State)));
            Assert.AreEqual(expected.Cpu.MutableState.StackCorrection, actual.Cpu.MutableState.StackCorrection);
            Assert.AreEqual(expected.Cpu.MutableState.PreviousDebugAbort, actual.Cpu.MutableState.PreviousDebugAbort);
            Assert.AreEqual(!trigger, actual.Cpu.MutableState.DebugState.DebugMemoryArmed);
            Assert.AreEqual(serialAccounting?.TotalInstructions, batchAccounting?.TotalInstructions);
            Assert.AreEqual(serialAccounting?.TotalCycles, batchAccounting?.TotalCycles);
            for (uint address = 0; address < expected.Memory.Size; address++)
                Assert.AreEqual(expected.Memory.Read(address), actual.Memory.Read(address), $"memory {address}");

            MachineCore Setup(bool batching)
            {
                var machine = new MachineCore();
                machine.Cpu.MemoryWatchpointBatchingEnabled = batching;
                machine.Cpu.InstructionCacheEnabled = cached;
                machine.LoadProgram(new[] { Code("vtm 1(1), utc"),
                    Code(right ? "vtm 3(2), " + operation : operation + ", vtm 3(2)"), Code("stop") }, 1);
                machine.Memory.Write(8, Code("vtm 7(3), stop"));
                machine.Memory.Write(16, new(123));
                machine.Memory.Write(32, new(234));
                machine.Cpu.SetA(456);
                machine.Cpu.SetM(15, 32);
                machine.Cpu.ArmDebugWatch(1, true, mode, trigger ? 16u : 17u, 8);
                return machine;
            }
        }
    }

    [TestMethod]
    public void WatchAbortPreservesTheContinuationAfterAPendingAddressModifier()
    {
        foreach (bool batching in new[] { false, true })
        {
            var machine = new MachineCore();
            machine.Cpu.MemoryWatchpointBatchingEnabled = batching;
            machine.LoadProgram(new[] { Code("utc 1, atx 17") }, 1);
            machine.Memory.Write(8, Code("vtm 7(2), stop"));
            machine.Memory.Write(16, new(123));
            machine.Cpu.SetA(456);
            machine.Cpu.ArmDebugWatch(1, false, 1, 16, 8);
            machine.Scheduler.Schedule(2, () =>
            {
                Assert.AreEqual(8u, machine.Cpu.K);
                Assert.AreEqual(0u, machine.Cpu.C);
                Assert.IsFalse(machine.Cpu.ApplyC);
                Assert.IsFalse(machine.Cpu.RightInstruction);
                Assert.AreEqual(123UL, machine.Memory.Read(16).Value);
            });
            long count = 0;
            Assert.IsTrue(machine.ExecuteBlock(32, ref count));
            Assert.AreEqual(4L, count);
            Assert.AreEqual(7u, machine.Cpu.GetM(2));
        }
    }

    [TestMethod]
    public void WatchAbortReturnsAtTheSameTickAndLeavesAccountingPendingLikeStep()
    {
        var machine = new MachineCore();
        machine.Cpu.MemoryWatchpointBatchingEnabled = true;
        machine.LoadProgram(new[] { Code("atx 20, stop") }, 1);
        machine.Memory.Write(8, Code("vtm 7(2), stop"));
        machine.Cpu.ArmDebugWatch(1, false, 1, 16, 8);
        using var accounting = new ExecutionAccountingSession(machine.Cpu);
        machine.Scheduler.Schedule(1, () =>
        {
            Assert.AreEqual(8u, machine.Cpu.K);
            Assert.AreEqual(0u, machine.Cpu.GetM(2));
            Assert.AreEqual(0UL, machine.Memory.Read(16).Value);
            Assert.IsTrue(machine.Cpu.InstructionAccounting!.HasPending);
        });
        long count = 0;
        Assert.IsFalse(machine.ExecuteBlock(1, ref count));
        Assert.AreEqual(1L, count);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(0L, accounting.TotalInstructions);
        Assert.IsTrue(machine.ExecuteBlock(16, ref count));
        Assert.AreEqual(3L, count);
        Assert.AreEqual(2L, accounting.TotalInstructions);
    }
}
