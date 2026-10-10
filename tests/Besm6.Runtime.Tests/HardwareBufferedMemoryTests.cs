using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class HardwareBufferedMemoryTests
{
    private static MemoryWord50 Number(ulong value) => MemoryWord50.Form(new(value), true, true);
    private static MemoryWord50 Command(ulong value) => MemoryWord50.Form(new(value), false, false);
    private static MramPortConfiguration Configuration => new(new(2000), new(900), new(1500),
        MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);
    private static (MachineCore Machine, MappedMemoryBackend Memory, HardwareBufferedMemory Buffers, HardwareTimeline Time) Setup()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        return (machine, machine.MappedMemory!, machine.HardwareModel.CreateBufferedMemory(Configuration, new(100)), machine.HardwareModel.Timeline);
    }
    private static HardwareBufferedRead Read(HardwareBufferedMemory buffers, HardwareTimeline time, uint address,
        bool instruction = false, bool right = false)
    {
        HardwareBufferedRead? result = null;
        if (instruction) buffers.FetchInstruction(address, right, value => result = value);
        else buffers.ReadOperand(address, value => result = value);
        for (int i = 0; result is null && i < 30; i++)
        {
            Assert.IsNotNull(time.NextEventTime); time.AdvanceTo(time.NextEventTime!.Value);
        }
        Assert.IsNotNull(result); return result.Value;
    }
    private static bool Publish(HardwareBufferedMemory buffers, HardwareTimeline time)
    {
        bool? removed = null;
        Assert.IsTrue(buffers.TryPublishOldest(value => removed = value, out _));
        for (int i = 0; removed is null && i < 30; i++) time.AdvanceTo(time.NextEventTime!.Value);
        Assert.IsNotNull(removed); return removed.Value;
    }

    [TestMethod]
    public void OperandMissReturnsAtMramDeadlineWithoutAllocatingBrz()
    {
        var (_, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, Number(17));
        var result = Read(buffers, time, 8);
        Assert.AreEqual(new HardwareInstant(900), result.CompletedAt); Assert.AreEqual(17UL, result.Word.Value);
        Assert.AreEqual(MemoryFaultSource.PhysicalMemory, result.Source); Assert.IsNull(result.Failure);
        Assert.AreEqual(0, memory.GetOperandSnapshot().Length); Assert.AreEqual(0, buffers.PendingRequests);
    }

    [TestMethod]
    public void CapturedBrzHitKeepsItsAcceptedWordWithoutReplacingANewerStore()
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17)); HardwareBufferedRead? result = null;
        buffers.ReadOperand(8, value => result = value); memory.Write(8, new(18));
        time.AdvanceTo(new(100)); Assert.AreEqual(17UL, result!.Value.Word.Value);
        Assert.AreEqual(MemoryFaultSource.OperandBuffer, result.Value.Source);
        Assert.AreEqual(18UL, memory.Read(8).Value); Assert.AreEqual(default, memory.PhysicalMemory.ReadRaw(8).Data);
    }

    [TestMethod]
    public void BothInstructionHalvesShareExistingBrsAndDoNotReadBrz()
    {
        var (_, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, Command(17)); memory.Write(8, new(18));
        var left = Read(buffers, time, 8, true); var right = Read(buffers, time, 8, true, true);
        Assert.AreEqual(17UL, left.Word.Value); Assert.AreEqual(17UL, right.Word.Value);
        Assert.AreEqual(new HardwareInstant(1000), right.CompletedAt);
        Assert.AreEqual(MemoryFaultSource.InstructionBuffer, right.Source);
        Assert.AreEqual(1, memory.GetInstructionSnapshot().Length);
    }

    [TestMethod]
    public void ControlFailureIsAReceivedSignalAndOtherHalfCanUseCachedWord()
    {
        var (_, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, MemoryWord50.Form(new(17), true, false));
        var left = Read(buffers, time, 8, true); Assert.IsNotNull(left.Failure);
        Assert.AreEqual(MemoryAccessKind.InstructionLeft, left.Failure.AccessKind);
        Assert.AreEqual(MemoryFaultSource.PhysicalMemory, memory.LastFault!.Value.Source);
        var right = Read(buffers, time, 8, true, true); Assert.IsNull(right.Failure);
        Assert.AreEqual(17UL, right.Word.Value); Assert.AreEqual(new HardwareInstant(1000), right.CompletedAt);
    }

    [TestMethod]
    public void OperandControlFailurePreservesRawWordAndDoesNotAllocateBrz()
    {
        var (_, memory, buffers, time) = Setup(); var invalid = new MemoryWord50(0);
        Assert.IsFalse(invalid.HasValidOperandControl); memory.PhysicalMemory.WriteRaw(8, invalid);
        var result = Read(buffers, time, 8); Assert.IsNotNull(result.Failure);
        Assert.AreEqual(invalid, result.RawWord); Assert.AreEqual(MemoryAccessKind.OperandRead, result.Failure.AccessKind);
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void ZeroAndPanelReadUseConfiguredLocalDelayAndSharedPanel()
    {
        var (_, memory, buffers, time) = Setup(); memory.SetPanel(1, Command(31));
        Assert.AreEqual(0UL, Read(buffers, time, 0).Word.Value);
        Assert.AreEqual(31UL, Read(buffers, time, 1, true).Word.Value);
        Assert.AreEqual(new HardwareInstant(200), time.Now);
        Assert.AreEqual(0, buffers.PendingRequests);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void UpdatedBrzValueSurvivesCompletionOfOldWriteEvenWhenBitsAreIdentical(bool identical)
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17));
        bool? removed = null; buffers.TryPublishOldest(value => removed = value, out _);
        time.AdvanceTo(new(100)); memory.Write(8, new(identical ? 17UL : 18UL));
        time.AdvanceTo(new(1500)); Assert.AreEqual(false, removed);
        Assert.AreEqual(Number(17), memory.PhysicalMemory.ReadRaw(8)); Assert.AreEqual(1, memory.PendingWriteCount);
        Assert.IsTrue(Publish(buffers, time)); Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(Number(identical ? 17UL : 18UL), memory.PhysicalMemory.ReadRaw(8));
    }

    [TestMethod]
    public void RecencyChangeDoesNotInvalidateAnUnchangedWriteback()
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17)); memory.Write(9, new(18));
        bool? removed = null; buffers.TryPublishOldest(value => removed = value, out _);
        time.AdvanceTo(new(100)); Assert.AreEqual(17UL, memory.Read(8).Value);
        time.AdvanceTo(new(1500)); Assert.AreEqual(true, removed);
        Assert.AreEqual(1, memory.PendingWriteCount); Assert.AreEqual(9U, memory.GetOperandSnapshot()[0].Request.Address);
    }

    [TestMethod]
    public void SerialWritebackPreservesAliasedPublicationOrder()
    {
        var (_, memory, buffers, time) = Setup(); memory.AssignmentBlocked = false;
        memory.Assignment.SetPhysicalPage(1, 2); memory.Assignment.SetPhysicalPage(3, 2);
        memory.Write(1024, new(17)); memory.Write(3072, new(18));
        bool? first = null; Assert.IsTrue(buffers.TryPublishOldest(value => first = value, out _));
        Assert.IsFalse(buffers.TryPublishOldest(_ => Assert.Fail(), out _));
        time.AdvanceTo(new(1500)); Assert.AreEqual(true, first); Assert.AreEqual(Number(17), memory.PhysicalMemory.ReadRaw(2048));
        Assert.IsTrue(Publish(buffers, time)); Assert.AreEqual(Number(18), memory.PhysicalMemory.ReadRaw(2048));
    }

    [TestMethod]
    public void PublishedOperandDoesNotInvalidateStaleInstructionBuffer()
    {
        var (_, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, Command(17));
        Assert.AreEqual(17UL, Read(buffers, time, 8, true).Word.Value);
        memory.InvertLeftStoreControl = memory.InvertRightStoreControl = false; memory.Write(8, new(18));
        Assert.IsTrue(Publish(buffers, time)); Assert.AreEqual(Command(18), memory.PhysicalMemory.ReadRaw(8));
        Assert.AreEqual(17UL, Read(buffers, time, 8, true).Word.Value);
        memory.ClearInstructionBuffer(); Assert.AreEqual(18UL, Read(buffers, time, 8, true).Word.Value);
    }

    [TestMethod]
    public void HostReplacementCancelsReadFillAndDelayedWriteBeforeChangingSharedStorage()
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17));
        buffers.TryPublishOldest(_ => Assert.Fail("stale write completion"), out _);
        buffers.FetchInstruction(16, false, _ => Assert.Fail("stale read fill"));
        time.AdvanceTo(new(100)); memory.HostMemory.Write(8, new(18));
        time.AdvanceTo(new(3000)); Assert.AreEqual(Command(18), memory.PhysicalMemory.ReadRaw(8));
        Assert.AreEqual(0, memory.GetInstructionSnapshot().Length); Assert.AreEqual(0, buffers.PendingRequests);
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void RemapCancelsOldReadAndPublishesPendingWritesUnderOldAssignment()
    {
        var (_, memory, buffers, time) = Setup(); memory.AssignmentBlocked = false; memory.Assignment.SetPhysicalPage(1, 2);
        memory.Write(1024, new(17)); buffers.TryPublishOldest(_ => Assert.Fail(), out _);
        buffers.ReadOperand(1025, _ => Assert.Fail());
        time.AdvanceTo(new(100)); memory.SetPhysicalPage(1, 3); time.AdvanceTo(new(3000));
        Assert.AreEqual(Number(17), memory.PhysicalMemory.ReadRaw(2048));
        Assert.AreEqual(0, memory.PendingWriteCount); Assert.AreEqual(0, buffers.PendingRequests);
        Assert.AreEqual(3U, memory.Assignment.GetPhysicalPage(1));
    }

    [TestMethod]
    public void SynchronousFlushIsABarrierAgainstDelayedOldWrite()
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17));
        buffers.TryPublishOldest(_ => Assert.Fail(), out _); time.AdvanceTo(new(100));
        memory.Write(8, new(18)); memory.FlushOperands(); time.AdvanceTo(new(3000));
        Assert.AreEqual(Number(18), memory.PhysicalMemory.ReadRaw(8)); Assert.AreEqual(0, buffers.PendingRequests);
    }

    [TestMethod]
    public void ClearBrsCancelsReadsButLeavesWritebackPending()
    {
        var (_, memory, buffers, time) = Setup(); memory.Write(8, new(17)); bool written = false;
        buffers.TryPublishOldest(_ => written = true, out _); buffers.FetchInstruction(16, false, _ => Assert.Fail());
        time.AdvanceTo(new(100)); memory.ClearInstructionBuffer(); time.AdvanceTo(new(1500));
        Assert.IsTrue(written); Assert.AreEqual(Number(17), memory.PhysicalMemory.ReadRaw(8));
        Assert.AreEqual(0, buffers.PendingRequests);
    }

    [TestMethod]
    public void CancelledWriteRetainsBrzAndForeignOrCompletedTokensCannotCancel()
    {
        var (_, memory, buffers, time) = Setup(); var (_, _, foreign, _) = Setup(); memory.Write(8, new(17));
        buffers.TryPublishOldest(_ => Assert.Fail(), out var token); Assert.IsFalse(foreign.Cancel(token));
        time.AdvanceTo(new(100)); Assert.IsTrue(buffers.Cancel(token)); Assert.IsFalse(buffers.Cancel(token));
        time.AdvanceTo(new(2000)); Assert.AreEqual(1, memory.PendingWriteCount);
        Assert.AreEqual(Number(0), memory.PhysicalMemory.ReadRaw(8)); Assert.IsTrue(Publish(buffers, time));
    }

    [TestMethod]
    public void ReadObserverFailureDoesNotReplayTransferOrLoseQueuedEvents()
    {
        var (_, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, Number(17));
        var failure = new ApplicationException("receiver"); var token = buffers.ReadOperand(8, _ => throw failure);
        bool other = false; buffers.ReadOperand(9, _ => other = true);
        var error = Assert.ThrowsExactly<HardwareCallbackException>(() => time.AdvanceTo(new(900)));
        Assert.AreSame(failure, error.InnerException); Assert.IsFalse(buffers.Cancel(token));
        time.AdvanceTo(new(900)); Assert.IsTrue(other); Assert.AreEqual(0, buffers.PendingRequests);
    }

    [TestMethod]
    public void ResetCpuPreservesBufferedTransfersAndConfigurationCannotBeChanged()
    {
        var (machine, memory, buffers, time) = Setup(); memory.PhysicalMemory.WriteRaw(8, Number(17)); bool read = false;
        buffers.ReadOperand(8, _ => read = true); machine.ResetCpu(); time.AdvanceTo(new(900)); Assert.IsTrue(read);
        Assert.AreSame(buffers, machine.HardwareModel.CreateBufferedMemory(Configuration, new(100)));
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.HardwareModel.CreateBufferedMemory(Configuration, new(101)));
        Assert.AreEqual(0UL, machine.Clock.Tick); Assert.AreEqual(0UL, machine.HardwareModel.CompletedInstructions);
    }
}
