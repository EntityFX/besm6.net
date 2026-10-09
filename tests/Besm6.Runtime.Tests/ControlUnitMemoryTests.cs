namespace Besm6.Tests;

[TestClass]
public sealed class ControlUnitMemoryTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ApplyingWiredControlsChangesOnlyOperandMode(bool assignment, bool protection)
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Mapped);
        var memory = machine.MappedMemory!;
        memory.Supervisor = false;
        memory.AssignmentBlocked = memory.ProtectionBlocked = false;
        memory.InvertLeftStoreControl = false;
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.HostMemory.Write(8192, new(11));
        memory.FetchInstruction(1024, false);
        memory.Write(1025, new(13));
        var brz = memory.GetOperandSnapshot();
        var brs = memory.GetInstructionSnapshot();
        var flags = ControlUnitFlags.StopOnInternalInterrupt | ControlUnitFlags.StopOnControlInterrupt |
            ControlUnitFlags.MatchWriteAddress | ControlUnitFlags.ExternalInterruptsBlocked | ControlUnitFlags.AutomaticB;
        if (assignment) flags |= ControlUnitFlags.AssignmentBlocked;
        if (protection) flags |= ControlUnitFlags.ProtectionBlocked;
        memory.ApplyControlStatus(new(flags));
        Assert.AreEqual(assignment, memory.AssignmentBlocked);
        Assert.AreEqual(protection, memory.ProtectionBlocked);
        Assert.IsFalse(memory.Supervisor);
        Assert.IsFalse(memory.InvertLeftStoreControl);
        Assert.IsTrue(memory.InvertRightStoreControl);
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(8u, memory.Assignment.GetPhysicalPage(1));
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(8193).Data.Value);
        CollectionAssert.AreEqual(brz, memory.GetOperandSnapshot());
        CollectionAssert.AreEqual(brs, memory.GetInstructionSnapshot());
        machine.ResetCpu();
        Assert.AreEqual(assignment, memory.AssignmentBlocked);
        Assert.AreEqual(protection, memory.ProtectionBlocked);
        CollectionAssert.AreEqual(brz, memory.GetOperandSnapshot());
        CollectionAssert.AreEqual(brs, memory.GetInstructionSnapshot());
    }
}
