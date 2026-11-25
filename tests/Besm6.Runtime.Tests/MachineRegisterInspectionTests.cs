using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Memory;

namespace Besm6.Tests;

[TestClass]
public sealed class MachineRegisterInspectionTests
{
    [TestMethod]
    public void InspectionNamesConfirmedRegistersAndOmitsUnconfirmedNumbers()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var state = machine.InspectRegisters();
        var byName = state.Registers.ToDictionary(register => register.Name);

        foreach (string name in new[]
                 {
                     "K", "H", "A", "Y", "R", "C", "M0", "M15",
                     "M16", "M17", "M23", "M26", "M27", "M28", "M29",
                     "RP0", "RP31", "RZ0", "RZ3",
                     "РГПр", "РСП", "РИ", "МСП"
                 })
            Assert.IsTrue(byName.ContainsKey(name), $"Missing register {name}.");

        foreach (string name in new[] { "M24", "M25", "M30", "M31" })
            Assert.IsFalse(byName.ContainsKey(name), $"Unconfirmed register {name} must stay absent.");

        Assert.AreEqual(MachineRegisterAccess.ReadWrite, byName["M16"].Access);
        Assert.AreEqual(MachineRegisterAccess.WriteOnly, byName["M17"].Access);
        Assert.AreEqual(MachineRegisterAccess.WriteOnly, byName["M28"].Access);
        Assert.AreEqual(MachineRegisterAccess.WriteOnly, byName["M29"].Access);
        Assert.AreEqual("M16", byName["C"].Alias);
        Assert.AreEqual("20B", byName["M16"].Alias);
        Assert.AreEqual(5, byName["RP0"].Width);
        Assert.AreEqual(8, byName["RZ0"].Width);
        Assert.AreEqual("020", byName["RP0"].Address);
        Assert.AreEqual("RPG0", byName["RP0"].Alias);
        Assert.AreEqual("030", byName["RZ0"].Address);
        Assert.AreEqual("0237", byName["РГПр"].Address);
    }

    [TestMethod]
    public void AssignmentAndProtectionViewsFollowGuestWrites()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var supervisor = machine.Cpu.Supervisor!;
        supervisor.WriteRegister(0x10, new(0x20C41UL));
        supervisor.WriteRegister(0x18, new(0xA5UL << 20));

        var byName = machine.InspectRegisters().Registers.ToDictionary(register => register.Name);

        Assert.AreEqual(1UL, byName["RP0"].Value);
        Assert.AreEqual(2UL, byName["RP1"].Value);
        Assert.AreEqual(3UL, byName["RP2"].Value);
        Assert.AreEqual(4UL, byName["RP3"].Value);
        Assert.AreEqual("020", byName["RP0"].Address);
        Assert.AreEqual("020", byName["RP3"].Address);
        Assert.AreEqual("021", byName["RP4"].Address);
        Assert.AreEqual(0xA5UL, byName["RZ0"].Value);
        Assert.AreEqual("030", byName["RZ0"].Address);
    }

    [TestMethod]
    public void InterruptViewsReflectLatchesAndMasks()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var io = machine.SupervisorIo!;
        io.ConfigureConsole(0);
        io.WriteDevice(28, new(0x200));
        io.WriteRegister(30, new(0x800));
        machine.Cpu.Supervisor!.LatchInternalCause(1UL << 20);

        var byName = machine.InspectRegisters().Registers.ToDictionary(register => register.Name);

        Assert.AreEqual((1UL << 20) | io.ExternalInterrupts, byName["РГПр"].Value);
        Assert.AreEqual(0x200UL, byName["РСП"].Value);
        Assert.AreEqual(0x800UL, byName["РИ"].Value);
        Assert.AreEqual(0x200UL, byName["МСП"].Value);
        Assert.AreEqual(MachineRegisterAccess.ReadOnly, byName["РГПр"].Access);
        Assert.AreEqual(MachineRegisterAccess.WriteOnly, byName["РИ"].Access);
    }

    [TestMethod]
    public void RegisterViewsAreIndependentCopies()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var first = machine.InspectRegisters();
        int index = Array.FindIndex(first.Registers, register => register.Name == "RP0");
        first.Registers[index] = first.Registers[index] with { Value = 99 };

        var second = machine.InspectRegisters();
        Assert.AreEqual(0UL, Array.Find(second.Registers, register => register.Name == "RP0")!.Value);
    }

    [TestMethod]
    public void DubnaProfileExposesCpuRegistersWithoutSupervisorGroups()
    {
        var state = new MachineCore().InspectRegisters();
        Assert.IsTrue(state.Registers.Any(register => register.Name == "A"));
        Assert.IsFalse(state.Registers.Any(register => register.Group == "Supervisor"));
        Assert.IsFalse(state.Registers.Any(register => register.Group == "PageAssignment"));
    }

    [TestMethod]
    public void PageAssignmentGroupsValidateTheirRange()
    {
        var assignment = new PageAssignment();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.GetAssignmentGroup(8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => assignment.GetProtectionGroup(4));
        CollectionAssert.AreEqual(new uint[] { 0, 0, 0, 0 }, assignment.GetAssignmentGroup(0));
        Assert.AreEqual(0u, assignment.GetProtectionGroup(0));
    }
}
