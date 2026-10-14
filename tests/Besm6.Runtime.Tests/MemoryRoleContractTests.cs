using Besm6.Architecture.Memory;
using Besm6.Core;
using Besm6.Processor.Memory;
using Besm6.Runtime.Configuration;

namespace Besm6.Tests;

[TestClass]
public sealed class MemoryRoleContractTests
{
    [TestMethod]
    [DataRow(MemoryModel.Dubna)]
    [DataRow(MemoryModel.Buffered)]
    [DataRow(MemoryModel.Mapped)]
    public void HostReplacementReconcilesPendingOperandsAndNextInstructionFetch(MemoryModel model)
    {
        var machine = new MachineCore(memoryModel: model);
        var operands = (IOperandMemory)machine.CommandMemory;
        var instructions = operands as IInstructionMemory;
        machine.Memory.Write(16, new(42));
        instructions?.FetchInstruction(16, false);
        operands.Write(16, new(123));
        operands.Write(17, new(234));
        var bufferedBefore = machine.BufferedMemory?.Buffers.GetOperandSnapshot();
        var mappedBefore = machine.MappedMemory?.GetOperandSnapshot();

        Assert.AreEqual(123UL, machine.Memory.Read(16).Value);
        if (bufferedBefore is not null)
            CollectionAssert.AreEqual(bufferedBefore, machine.BufferedMemory!.Buffers.GetOperandSnapshot());
        if (mappedBefore is not null)
            CollectionAssert.AreEqual(mappedBefore, machine.MappedMemory!.GetOperandSnapshot());

        machine.Memory.Write(16, new(456));
        Assert.AreEqual(456UL, machine.Memory.Read(16).Value);
        Assert.AreEqual(234UL, machine.Memory.Read(17).Value);
        if (instructions is not null)
        {
            Assert.AreEqual(456UL, instructions.FetchInstruction(16, false).Value);
            Assert.AreEqual(456UL, instructions.FetchInstruction(16, true).Value);
        }
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void PhysicalHostInspectionBypassesMappingProtectionAndInvalidOperandControl()
    {
        var memory = new MappedMemoryBackend
        {
            Supervisor = false, AssignmentBlocked = false, ProtectionBlocked = false
        };
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.Assignment.OperandProtectionMask = 2;
        memory.PhysicalMemory.WriteRaw(8197, new(123));

        Assert.AreEqual(123UL, memory.HostMemory.Read(8197).Value);
        Assert.ThrowsExactly<MemoryProtectionException>(() => memory.Read(1029));
        memory.ProtectionBlocked = true;
        Assert.ThrowsExactly<MemoryControlException>(() => memory.Read(1029));
        Assert.AreEqual(123UL, memory.HostMemory.Read(8197).Value);
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void CommandRolePublishesMappedAliasesAndInvalidatesBothInstructionTags()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Mapped);
        var memory = machine.MappedMemory!;
        memory.Supervisor = false;
        memory.AssignmentBlocked = false;
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.Assignment.SetPhysicalPage(2, 8);
        machine.Memory.Write(8200, new(42));
        memory.FetchInstruction(1032, false);
        memory.FetchInstruction(2056, true);
        memory.Write(1032, new(123));
        memory.Write(2056, new(234));

        machine.CommandMemory.StoreCommand(1032, new(456));

        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(456UL, memory.PhysicalMemory.ReadRaw(8200).Data.Value);
        Assert.AreEqual(0UL, machine.Memory.Read(1032).Value);
        Assert.AreEqual(456UL, memory.FetchInstruction(1032, false).Value);
        Assert.AreEqual(456UL, memory.FetchInstruction(2056, true).Value);
        Assert.AreEqual(0UL, machine.Clock.Tick);
    }

    [TestMethod]
    [DataRow(MemoryModel.Buffered)]
    [DataRow(MemoryModel.Mapped)]
    public void HostViewsCannotBeUsedAsCpuOrCommandPorts(MemoryModel model)
    {
        var machine = new MachineCore(memoryModel: model);
        IHostMemory host = model == MemoryModel.Buffered
            ? machine.BufferedMemory!.HostMemory : machine.MappedMemory!.HostMemory;
        Assert.IsFalse(host is IOperandMemory);
        Assert.IsFalse(host is ICommandMemory);
        Assert.IsFalse(machine.Memory is IOperandMemory);
        Assert.IsFalse(machine.Memory is ICommandMemory);
        Assert.IsTrue(machine.CommandMemory is IOperandMemory);
    }
}
