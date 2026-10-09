namespace Besm6.Tests;

[TestClass]
public sealed class MappedMachineTests
{
    private static Word48 Code(string text) => new(Besm6.Asm.Assembler.Asm(text));
    private static MachineCore Mathematical()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Mapped);
        var memory = machine.MappedMemory!;
        memory.Supervisor = memory.AssignmentBlocked = memory.ProtectionBlocked = false;
        memory.Assignment.SetPhysicalPage(0, 8);
        memory.Assignment.SetPhysicalPage(1, 9);
        // Physical loader view; CPU operates on mathematical addresses.
        machine.Memory.Write(8200, Code("xta 2000, atx 2001"));
        machine.Memory.Write(8201, Code("xta 2001, stop"));
        machine.Memory.Write(9216, new(123));
        machine.Cpu.StartAt(8);
        return machine;
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Original)]
    [DataRow(ExecutionSpeed.Max)]
    public void StepAndRunLoadedAgreeWithRemappingEventsAndStop(ExecutionSpeed speed)
    {
        var stepped = Mathematical();
        var loaded = Mathematical();
        foreach (var machine in new[] { stepped, loaded })
            machine.Scheduler.Schedule(2, () =>
            {
                machine.MappedMemory!.SetPhysicalPage(1, 10);
                machine.Memory.Write(10241, new(456));
                machine.StepTrace = (_, _) => { };
            });
        while (!stepped.Step()) { }
        var loader = new DubnaLoader(loaded) { Speed = speed, CollectStatistics = true, InstructionLimit = 10 };
        Assert.IsTrue(loader.RunLoaded().Success);
        Assert.AreEqual(4L, loader.InstructionsExecuted);
        Assert.AreEqual(456UL, loaded.Cpu.A.Value);
        Assert.AreEqual(4UL, loaded.Clock.Tick);
        Assert.AreEqual(stepped.Cpu.A, loaded.Cpu.A);
        Assert.AreEqual(stepped.Cpu.Y, loaded.Cpu.Y);
        Assert.AreEqual(stepped.Cpu.R, loaded.Cpu.R);
        Assert.AreEqual(stepped.Cpu.K, loaded.Cpu.K);
        Assert.AreEqual(stepped.Cpu.RightInstruction, loaded.Cpu.RightInstruction);
        for (int i = 0; i < 16; i++) Assert.AreEqual(stepped.Cpu.GetM(i), loaded.Cpu.GetM(i));
        for (uint i = 0; i < 32768; i++) Assert.AreEqual(stepped.Memory.Read(i), loaded.Memory.Read(i));
        CollectionAssert.AreEqual(stepped.MappedMemory!.GetOperandSnapshot(), loaded.MappedMemory!.GetOperandSnapshot());
        CollectionAssert.AreEqual(stepped.MappedMemory.GetInstructionSnapshot(), loaded.MappedMemory.GetInstructionSnapshot());
        Assert.AreEqual(123UL, loaded.MappedMemory.PhysicalMemory.ReadRaw(9217).Data.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MemoryFaultPreservesGuestAvostAndCountsOnlyCompletedCommands(bool protection)
    {
        var machine = Mathematical();
        var memory = machine.MappedMemory!;
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 9;
        if (protection) memory.Assignment.OperandProtectionMask = 2;
        else memory.PhysicalMemory.WriteRaw(9216, new(0));
        int notifications = 0;
        machine.Cpu.InstructionExecuted = _ => notifications++;
        var loader = new DubnaLoader(machine) { InstructionLimit = 10 };
        if (protection) Assert.ThrowsExactly<MemoryProtectionException>(() => loader.RunLoaded());
        else Assert.ThrowsExactly<MemoryControlException>(() => loader.RunLoaded());
        Assert.AreEqual(0L, loader.InstructionsExecuted);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0, notifications);
        Assert.AreEqual(1, machine.Cpu.InterceptCount);
        Assert.AreEqual(8u, machine.Cpu.K);
        Assert.IsTrue(machine.Cpu.RightInstruction); // Existing CPU advance-before-dispatch contract.
        Assert.AreEqual(new MemoryRequestAddress(1024, false), memory.LastFault!.Value.Request);
    }

    [TestMethod]
    [DataRow(1u)]
    [DataRow(2u)]
    public void WatchpointPrecedesGuestMemoryEffects(uint mode)
    {
        var machine = Mathematical();
        var memory = machine.MappedMemory!;
        machine.Memory.Write(8200, Code(mode == 1 ? "atx 2000" : "xta 2000"));
        machine.Cpu.SetA(999);
        machine.Cpu.ArmDebugWatch(8, false, mode, 1024, 9);
        machine.Step();
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(123UL, machine.Memory.Read(9216).Value);
        Assert.AreEqual(999UL, machine.Cpu.A.Value);
        Assert.IsNull(memory.LastFault);
    }

    [TestMethod]
    public void ResetAndReloadPreserveMemoryModeButRestartOnLeftHalf()
    {
        var machine = Mathematical();
        var memory = machine.MappedMemory!;
        machine.Step();
        machine.Step();
        var operands = memory.GetOperandSnapshot();
        var instructions = memory.GetInstructionSnapshot();
        machine.ResetCpu();
        Assert.IsFalse(memory.Supervisor);
        Assert.IsFalse(memory.AssignmentBlocked);
        Assert.IsFalse(memory.ProtectionBlocked);
        Assert.AreEqual(2UL, machine.Clock.Tick);
        CollectionAssert.AreEqual(operands, memory.GetOperandSnapshot());
        CollectionAssert.AreEqual(instructions, memory.GetInstructionSnapshot());
        // Loading is physical and coherent, so explicitly choose the mathematical entry.
        machine.LoadProgram(new[] { Code("vtm 7(2), stop") }, 8200);
        machine.Cpu.StartAt(8);
        Assert.IsFalse(machine.Cpu.RightInstruction);
        machine.Step();
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.AreEqual(4UL, machine.Clock.Tick);
        machine.Cpu.StartAt(8);
        machine.Step();
        Assert.IsTrue(machine.Step());
    }
}
