using System.Text.Json;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareDataMemoryTests
{
    private static MramPortConfiguration Configuration => new(new(2000), new(900), new(1500),
        MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);
    private static MachineCore Machine(string code, bool right = false)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8,
            MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm(right ? "stop, " + code : code + ", stop")), false, false));
        for (uint i = 16; i < 40; i++) machine.MappedMemory.PhysicalMemory.WriteRaw(i, MemoryWord50.Form(new(i + 1), true, true));
        machine.Cpu.StartAt(8); machine.Cpu.State.IsRightHalf = right;
        machine.Cpu.SetM(15, 24); machine.Cpu.SetA(0x12345); machine.Cpu.SetY(0x56789);
        return machine;
    }

    private static HardwareInstructionHandle Begin(MachineCore machine)
    {
        var model = machine.HardwareModel;
        model.StartTimedInstructionFetch(Configuration, new(100));
        for (int i = 0; model.FetchWaiting && i < 10; i++) model.AdvanceNextEvent();
        Assert.IsFalse(model.FetchWaiting);
        var instruction = model.CurrentInstruction!.Value;
        model.StartDataMemoryInstruction(instruction); return instruction;
    }

    private static HardwareInstructionOutcome Finish(MachineCore machine)
    {
        HardwareInstructionOutcome? outcome = null;
        for (int i = 0; outcome is null && i < 50; i++) outcome = machine.HardwareModel.AdvanceNextEvent();
        Assert.IsNotNull(outcome); return outcome.Value;
    }

    private static void Equal(MachineCore expected, MachineCore actual)
    {
        Assert.AreEqual(JsonSerializer.Serialize(new ProcessorSnapshot(expected.Cpu.State)), JsonSerializer.Serialize(new ProcessorSnapshot(actual.Cpu.State)));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Cpu.Supervisor!.Snapshot()), JsonSerializer.Serialize(actual.Cpu.Supervisor!.Snapshot()));
        Assert.AreEqual(JsonSerializer.Serialize(expected.MappedMemory!.GetOperandSnapshot()), JsonSerializer.Serialize(actual.MappedMemory!.GetOperandSnapshot()));
        for (uint i = 0; i < 48; i++) Assert.AreEqual(expected.MappedMemory.PhysicalMemory.ReadRaw(i), actual.MappedMemory.PhysicalMemory.ReadRaw(i), i.ToString());
    }

    [TestMethod]
    [DataRow("xta 20")] [DataRow("atx 20")] [DataRow("stx 20")] [DataRow("xts 20")]
    [DataRow("arx 20")] [DataRow("acx 20")] [DataRow("anx 20")] [DataRow("asx 20")]
    [DataRow("xtr 20")] [DataRow("sti 3")] [DataRow("sti 17")] [DataRow("its 3")]
    [DataRow("wtc 20")] [DataRow("xta (17)")] [DataRow("atx (17)")]
    [DataRow("stx (17)")] [DataRow("xts (17)")] [DataRow("its 7")] [DataRow("wtc (17)")]
    public void EveryDataInstructionMatchesStepInBothHalves(string code)
    {
        foreach (bool right in new[] { false, true })
        {
            var expected = Machine(code, right); var actual = Machine(code, right);
            expected.Cpu.Step(); Begin(actual); Assert.AreEqual(HardwareInstructionStatus.Completed, Finish(actual).Status);
            Equal(expected, actual); Assert.AreEqual(1UL, actual.HardwareModel.CompletedInstructions);
            Assert.AreEqual(0UL, actual.Clock.Tick);
        }
    }

    [TestMethod]
    public void ReadConsumesTheReturnedWordAndRejectsEarlyCompletion()
    {
        var machine = Machine("xta 20"); var handle = Begin(machine); var model = machine.HardwareModel;
        Assert.IsFalse(model.TryIndicateCompletionReady(handle));
        model.Timeline.ScheduleAt(new(2500), () => machine.MappedMemory!.PhysicalMemory.WriteRaw(16, MemoryWord50.Form(new(999), true, true)));
        Assert.AreEqual(HardwareInstructionStatus.Completed, Finish(machine).Status); Assert.AreEqual(17UL, machine.Cpu.GetA().Value);
    }

    [TestMethod]
    public void StackStorePrecedesReadIncludingForwardingFromItsNewBrzValue()
    {
        var machine = Machine("xts 30"); Begin(machine); var model = machine.HardwareModel;
        Assert.AreEqual(24U, machine.Cpu.GetM(15));
        Assert.IsNull(model.AdvanceTo(new(1000))); Assert.AreEqual(25U, machine.Cpu.GetM(15));
        Assert.AreEqual(1, machine.MappedMemory!.PendingWriteCount);
        Assert.AreEqual(HardwareInstructionStatus.Completed, Finish(machine).Status);
        Assert.AreEqual(0x12345UL, machine.Cpu.GetA().Value); Assert.AreEqual(1100UL, model.Timeline.Now.Nanoseconds);
    }

    [TestMethod]
    public void FillingLastBrzWaitsForTimedPublicationWithoutSynchronousMramWrite()
    {
        var expected = Machine("atx 50"); var machine = Machine("atx 50");
        foreach (var item in new[] { expected, machine })
            for (uint i = 24; i < 31; i++) item.MappedMemory!.Write(i, new(100 + i));
        expected.Cpu.Step(); Begin(machine); var model = machine.HardwareModel;
        Assert.IsNull(model.AdvanceTo(new(1000))); Assert.AreEqual(8, machine.MappedMemory!.PendingWriteCount);
        Assert.AreEqual(25UL, machine.MappedMemory.PhysicalMemory.ReadRaw(24).Data.Value);
        Assert.IsNull(model.AdvanceTo(new(1999)));
        Assert.AreEqual(HardwareInstructionStatus.Completed, Finish(machine).Status);
        Assert.AreEqual(3500UL, model.Timeline.Now.Nanoseconds); Equal(expected, machine);
    }

    [TestMethod]
    [DataRow("xta 20", false)] [DataRow("atx 20", true)]
    [DataRow("stx 20", true)] [DataRow("xts 20", true)] [DataRow("wtc (17)", false)]
    public void HardwareWatchErrorsKeepTheSerialStackAndInterruptState(string code, bool write)
    {
        var expected = Machine(code); var actual = Machine(code);
        foreach (var item in new[] { expected, actual })
        {
            uint address = code.StartsWith("xts") ? 24u : code.StartsWith("wtc") ? 23u : 16u;
            item.Cpu.Supervisor!.Status = new((ControlUnitFlags)(write ? 0x413 : 0x403));
            item.Cpu.Supervisor.WriteModifier(29, address);
        }
        expected.Cpu.Step(); Begin(actual); Assert.AreEqual(HardwareInstructionStatus.GuestFault, Finish(actual).Status);
        Equal(expected, actual); Assert.AreEqual(0UL, actual.HardwareModel.CompletedInstructions);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ResetOrCancellationCannotPublishAWaitingRead(bool reset)
    {
        var machine = Machine("xta 20"); var instruction = Begin(machine);
        if (reset) machine.ResetCpu(); else Assert.IsTrue(machine.HardwareModel.TryRequestCancellation(instruction));
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, Finish(machine).Status);
        Assert.IsFalse(machine.Cpu.HasPreparedInstruction); Assert.AreEqual(0UL, machine.HardwareModel.CompletedInstructions);
        machine.Cpu.StartAt(8); Begin(machine); Assert.AreEqual(HardwareInstructionStatus.Completed, Finish(machine).Status);
    }

    [TestMethod]
    [DataRow("xta (17)")] [DataRow("stx 20")] [DataRow("xts 20")] [DataRow("anx 20")]
    public void ControlFailureMatchesStepAfterAcceptedAddressAndStoreEffects(string code)
    {
        var expected = Machine(code); var actual = Machine(code);
        uint address = code.StartsWith("xta") || code.StartsWith("stx") ? 23u : 16u;
        foreach (var item in new[] { expected, actual })
            item.MappedMemory!.PhysicalMemory.WriteRaw(address, MemoryWord50.Form(new(42), false, true));
        Assert.IsFalse(actual.MappedMemory!.PhysicalMemory.ReadRaw(address).HasValidOperandControl);
        expected.Cpu.Step(); Assert.IsFalse(expected.Cpu.LastStepCompleted);
        Begin(actual); Assert.AreEqual(HardwareInstructionStatus.GuestFault, Finish(actual).Status);
        Equal(expected, actual);
    }

    [TestMethod]
    [DataRow("xta (17)")] [DataRow("atx (17)")] [DataRow("xts (17)")] [DataRow("wtc 0")]
    public void AddressModificationAndZeroUseSharedHandlers(string code)
    {
        var expected = Machine(code); var actual = Machine(code);
        foreach (var item in new[] { expected, actual }) { item.Cpu.State.C = 16; item.Cpu.State.ApplyC = true; }
        expected.Cpu.Step(); Begin(actual); Finish(actual); Equal(expected, actual);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void DiagnosticMemoryWatchMatchesStepWithoutIssuingPhysicalTransfer(bool write)
    {
        var expected = Machine(write ? "atx 20" : "xta 20"); var actual = Machine(write ? "atx 20" : "xta 20");
        foreach (var item in new[] { expected, actual }) item.Cpu.ArmDebugWatch(8, false, write ? 1u : 2u, 16, 12);
        expected.Cpu.Step(); Begin(actual); Finish(actual); Equal(expected, actual);
        Assert.AreEqual(expected.Cpu.LastStepCompleted, actual.Cpu.LastStepCompleted);
        Assert.AreEqual(0, actual.HardwareModel.CreateBufferedMemory(Configuration, new(100)).PendingRequests);
    }

    [TestMethod]
    public void PanelStorePublicationWaitsOnTheSamePortAndRetainsPanelWord()
    {
        var expected = Machine("atx 1"); var actual = Machine("atx 1");
        foreach (var item in new[] { expected, actual })
        { item.MappedMemory!.Write(16, new(123)); item.MappedMemory.Write(1, new(1)); }
        expected.Cpu.Step(); Begin(actual);
        Assert.IsNull(actual.HardwareModel.AdvanceTo(new(1000)));
        Assert.AreEqual(17UL, actual.MappedMemory!.PhysicalMemory.ReadRaw(16).Data.Value);
        Finish(actual); Equal(expected, actual);
    }

    [TestMethod]
    public void PrivilegedSpecialModifierStoreUsesItsFrozenTarget()
    {
        var expected = Machine("sti 20"); var actual = Machine("sti 20");
        expected.Cpu.Step(); Begin(actual); Finish(actual); Equal(expected, actual);
        Assert.AreEqual(0x2345U, actual.Cpu.State.C);
    }

    [TestMethod]
    public void AcceptedBrzAndItsPublicationSurviveCpuResetWithoutRetiringTheCommand()
    {
        var machine = Machine("atx 50");
        for (uint i = 24; i < 31; i++) machine.MappedMemory!.Write(i, new(100 + i));
        Begin(machine); machine.HardwareModel.AdvanceTo(new(1000)); machine.ResetCpu();
        Assert.AreEqual(HardwareInstructionStatus.Cancelled, Finish(machine).Status);
        machine.HardwareModel.AdvanceTo(new(3500));
        Assert.AreEqual(124UL, machine.MappedMemory!.PhysicalMemory.ReadRaw(24).Data.Value);
        Assert.AreEqual(7, machine.MappedMemory.PendingWriteCount); Assert.AreEqual(0UL, machine.HardwareModel.CompletedInstructions);
    }

    [TestMethod]
    public void FullEightSlotBackendWaitsForForeignPublicationThenAcceptsItsStore()
    {
        var machine = new MachineCore(MemoryModel.Mapped); var memory = machine.MappedMemory!;
        var model = machine.HardwareModel; var buffers = model.CreateBufferedMemory(Configuration, new(100));
        for (uint i = 16; i < 24; i++) memory.Write(i, new(i));
        bool accepted = false; buffers.TryPublishOldest(_ => { }, out _);
        buffers.StoreOperand(32, new(777), () => accepted = true);
        model.Timeline.AdvanceTo(new(100)); Assert.IsFalse(accepted); Assert.AreEqual(8, memory.PendingWriteCount);
        model.Timeline.AdvanceTo(new(1500)); Assert.IsTrue(accepted); Assert.AreEqual(8, memory.PendingWriteCount);
        Assert.AreEqual(777UL, memory.Read(32).Value); Assert.AreEqual(16UL, memory.PhysicalMemory.ReadRaw(16).Data.Value);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void InstructionBufferClearPreservesStoreButAssignmentChangeCancelsIt(bool assignment)
    {
        var machine = Machine("atx 20"); Begin(machine);
        if (assignment) machine.Cpu.Supervisor!.WriteRegister(0x10, Word48.Zero);
        else machine.MappedMemory!.ClearInstructionBuffer();
        Assert.AreEqual(assignment ? HardwareInstructionStatus.Cancelled : HardwareInstructionStatus.Completed, Finish(machine).Status);
        Assert.AreEqual(assignment ? 0 : 1, machine.MappedMemory!.PendingWriteCount);
        if (!assignment) Assert.AreEqual(0x12345UL, machine.MappedMemory.Read(16).Value);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void DiagnosticHooksCannotAdvanceTimeInsideFetchOrDataTransition(bool memoryWatch)
    {
        var machine = Machine("xta 20"); var model = machine.HardwareModel; bool checkedGuard = false;
        if (memoryWatch) machine.Cpu.ArmDebugWatch(8, true, 2, 16, 12);
        machine.Cpu.TraceInstruction = (_, _, _, opcode) =>
        {
            if (memoryWatch && opcode != 0) return;
            checkedGuard = true;
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Timeline.AdvanceTo(model.Timeline.Now + new HardwareDuration(1)));
        };
        Begin(machine); Finish(machine); Assert.IsTrue(checkedGuard);
        Assert.IsFalse(model.Timeline.AdvancementProhibited);
        Assert.AreEqual(memoryWatch ? 900UL : 2900UL, model.Timeline.Now.Nanoseconds);
    }
}
