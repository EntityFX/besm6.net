namespace Besm6.Tests;

[TestClass]
[TestCategory("Supervisor")]
public sealed class SupervisorProcessorTests
{
    private static uint Half(uint opcode, uint address = 0, byte register = 0) =>
        ((uint)register << 20) | (opcode << 12) | address;

    private static (Processor Cpu, MappedMemoryBackend Memory, SupervisorControl Supervisor) Create(uint left, uint right = 0)
    {
        var memory = new MappedMemoryBackend();
        memory.HostMemory.Write(8, new(((ulong)left << 24) | right));
        var cpu = new Processor(memory, ProcessorProfile.Supervisor);
        cpu.StartAt(8);
        return (cpu, memory, cpu.Supervisor!);
    }

    [TestMethod]
    [DataRow(0x28u, 0x168u, false)] // TO-8 sheet80: Э050 -> 00550 octal.
    [DataRow(0x28u, 0x168u, true)]
    [DataRow(0x3Fu, 0x17Fu, false)] // Э077 -> 00577 octal.
    [DataRow(0x3Fu, 0x17Fu, true)]
    [DataRow(0x80u, 0x170u, false)] // Sheet82: Э20 -> Э060.
    [DataRow(0x88u, 0x171u, true)]  // Э21 -> Э061.
    public void ExtracodeVectorsPreserveAuAndReturnToNextWord(uint opcode, uint vector, bool right)
    {
        uint call = Half(opcode, 123, 3);
        var (cpu, memory, supervisor) = Create(right ? Half((uint)Opcode.Vtm, 0, 1) : call, right ? call : 0);
        cpu.SetM(3, 7);
        if (right) cpu.Step();
        cpu.SetA(0x123456789ABC);
        cpu.SetY(0xABCDEF123456);
        cpu.SetR(0x23);
        supervisor.Status = new((ControlUnitFlags)0x81C);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(vector, cpu.GetK());
        Assert.IsFalse(cpu.RightInstruction);
        Assert.AreEqual(130u, cpu.GetM(14));
        Assert.AreEqual(9u, supervisor.ReadModifier(26));
        Assert.AreEqual(0xC1Fu, supervisor.Status.EncodedControls);
        Assert.AreEqual(0x123456789ABCUL, cpu.GetA().Value);
        Assert.AreEqual(0xABCDEF123456UL, cpu.GetY().Value);
        Assert.AreEqual(0x23u, cpu.GetR());
        Assert.AreEqual(SupervisorMode.Extracode, supervisor.Mode);
        Assert.AreEqual(0u, supervisor.SavedFlags & 0x100);
        memory.HostMemory.Write(vector, new((ulong)Half((uint)Opcode.Ij, 555, 2) << 24));
        cpu.Step();
        Assert.AreEqual(9u, cpu.GetK());
        Assert.IsFalse(cpu.RightInstruction);
        // TO-8 does not restore PoP, PoK, write-match or AutomaticB from M23.
        Assert.AreEqual(0x81Cu, supervisor.Status.EncodedControls);
    }

    [TestMethod]
    [DataRow(2, 101u)]
    [DataRow(10, 101u)]
    [DataRow(3, 202u)]
    [DataRow(11, 202u)]
    [DataRow(0, 0u)]
    [DataRow(1, 0u)]
    [DataRow(4, 0u)]
    [DataRow(5, 0u)]
    [DataRow(6, 0u)]
    [DataRow(7, 0u)]
    [DataRow(14, 0u)]
    [DataRow(15, 0u)]
    public void ReturnSelectorUsesThreeBitsAndInvalidSelectorsReturnToZero(int register, uint expected)
    {
        var (cpu, _, supervisor) = Create(Half((uint)Opcode.Ij, 321, (byte)register));
        supervisor.WriteModifier(26, 101);
        supervisor.WriteModifier(27, 202);
        supervisor.WriteModifier(23, 0); // Mathematical mode, left half, controls clear.
        supervisor.Status = new((ControlUnitFlags)0xC1F);
        cpu.SetA(99);
        cpu.SetY(77);
        cpu.SetR(0x32);
        cpu.Step();
        Assert.AreEqual(expected, cpu.GetK());
        Assert.AreEqual(SupervisorMode.Mathematical, supervisor.Mode);
        Assert.AreEqual(0x81Cu, supervisor.Status.EncodedControls);
        Assert.AreEqual(99UL, cpu.GetA().Value);
        Assert.AreEqual(77UL, cpu.GetY().Value);
        Assert.AreEqual(0x32u, cpu.GetR());
    }

    [TestMethod]
    public void GuestRpWriteDoesNotReplaceTheRequiredPublicationSequenceWithHostSerialization()
    {
        var (cpu, memory, supervisor) = Create(Half((uint)Opcode.Mod, 0x10));
        supervisor.Status = new(ControlUnitFlags.ProtectionBlocked);
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.Write(1029, new(111));
        cpu.SetA(9u << 5); // RP1=9: register group0 contains pages0..3.
        cpu.Step();
        Assert.AreEqual(1, memory.PendingWriteCount);
        Assert.AreEqual(9u, memory.Assignment.GetPhysicalPage(1));
        memory.FlushOperands();
        Assert.AreEqual(111UL, memory.PhysicalMemory.ReadRaw(9221).Data.Value);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(8197).Data.Value);
        memory.Write(1029, new(222));
        memory.ImportAssignmentGroup(0, new(10u << 5)); // Explicit host bridge publishes under old RP.
        Assert.AreEqual(0, memory.PendingWriteCount);
        Assert.AreEqual(222UL, memory.PhysicalMemory.ReadRaw(9221).Data.Value);
        Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(10245).Data.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OperandProtectionUsesDocumentedReturnPositionAndPageSignal(bool right)
    {
        uint load = Half((uint)Opcode.Xta, 2053);
        var (cpu, memory, supervisor) = Create(0);
        memory.Assignment.SetPhysicalPage(1, 8);
        memory.Assignment.SetPhysicalPage(2, 9);
        memory.HostMemory.Write(8192, new(((ulong)(right ? Half((uint)Opcode.Vtm, 0, 1) : load) << 24) | (right ? load : 0)));
        memory.Assignment.OperandProtectionMask = 4;
        supervisor.Status = new(ControlUnitFlags.None);
        supervisor.Mode = SupervisorMode.Mathematical;
        cpu.StartAt(1024);
        if (right) cpu.Step();
        cpu.SetA(33);
        cpu.SetY(44);
        Assert.IsFalse(cpu.Step());
        Assert.IsFalse(cpu.LastStepCompleted);
        Assert.AreEqual(0x140u, cpu.GetK());
        Assert.AreEqual(1025u, supervisor.ReadModifier(27));
        Assert.AreEqual(right ? 0x300u : 0x200u, supervisor.SavedFlags);
        Assert.AreEqual((1UL << 19) | 32UL, supervisor.InternalInterrupts);
        Assert.AreEqual(33UL, cpu.GetA().Value);
        Assert.AreEqual(44UL, cpu.GetY().Value);
    }

    [TestMethod]
    public void RawRegisterAccessRejectsUnsupportedConfigurationInsteadOfFabricatingZero()
    {
        var (_, _, supervisor) = Create(0);
        Assert.ThrowsExactly<NotSupportedException>(() => supervisor.ReadRegister(8));
        Assert.ThrowsExactly<NotSupportedException>(() => supervisor.WriteRegister(8, new(123)));
    }

    [TestMethod]
    public void CommandWatchWaitsForFourInstructionsFollowingItsInstallation()
    {
        uint nop = Half((uint)Opcode.Vtm, 0, 1);
        var (cpu, memory, supervisor) = Create(Half((uint)Opcode.Ati, 28), nop);
        memory.HostMemory.Write(9, new(((ulong)nop << 24) | nop));
        memory.HostMemory.Write(10, new(((ulong)nop << 24) | nop));
        supervisor.Status = new((ControlUnitFlags)0x403);
        cpu.SetA(10);
        for (int i = 0; i < 5; i++)
        {
            cpu.Step();
            Assert.IsTrue(cpu.LastStepCompleted, $"Instruction {i} must finish before watch activation.");
        }
        Assert.AreEqual(10u, cpu.GetK());
        Assert.IsTrue(cpu.RightInstruction);
        cpu.Step();
        Assert.IsFalse(cpu.LastStepCompleted);
        Assert.AreEqual(0x140u, cpu.GetK());
        Assert.AreEqual(1UL << 11, supervisor.InternalInterrupts);
    }

    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    public void OperandWatchSelectsReadOrWriteAndRejectsBeforeSideEffects(bool write, bool watchWrite, bool expectedFault)
    {
        var (cpu, memory, supervisor) = Create(Half((uint)(write ? Opcode.Atx : Opcode.Xta), 12));
        memory.PhysicalMemory.Store(12, new(5), true, true);
        supervisor.Status = new((ControlUnitFlags)(0x403u | (watchWrite ? 0x10u : 0)));
        supervisor.WriteModifier(29, 12);
        cpu.SetA(99);
        cpu.Step();
        Assert.AreEqual(!expectedFault, cpu.LastStepCompleted);
        if (expectedFault)
        {
            Assert.AreEqual(1UL << (write ? 16 : 15), supervisor.InternalInterrupts);
            Assert.AreEqual(99UL, cpu.GetA().Value);
            Assert.AreEqual(0, memory.PendingWriteCount);
            Assert.AreEqual(5UL, memory.PhysicalMemory.ReadRaw(12).Data.Value);
        }
    }

    [TestMethod]
    [DataRow(0x26u, 0xFEDu)]
    [DataRow(0x27u, 0xEEEu)]
    public void UnusedSupervisorIndexOpcodesUseTwelveAddressBitsAndAddressSelectedTarget(uint opcode, uint expected)
    {
        var (cpu, _, _) = Create(Half(opcode, 0xFED, 9));
        cpu.SetM(13, 0x7F01);
        cpu.SetM(9, 0x100);
        cpu.SetA(123);
        cpu.SetY(456);
        cpu.SetR(0x31);
        cpu.Step();
        Assert.AreEqual(expected, cpu.GetM(13));
        Assert.AreEqual(0x100u, cpu.GetM(9));
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(456UL, cpu.GetY().Value);
        Assert.AreEqual(0x31u, cpu.GetR());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnusedIo032Aliases033InSupervisorMode(bool read)
    {
        var (cpu, _, supervisor) = Create(Half(0x1A, read ? 0x800u : 5u));
        var io = new TestIo();
        supervisor.Io = io;
        cpu.SetA(0xABCDEF);
        cpu.SetY(123);
        cpu.Step();
        Assert.AreEqual(read ? 0x800u : 5u, io.LastAddress);
        Assert.AreEqual(read ? 0x123456UL : 0xABCDEFUL, cpu.GetA().Value);
        Assert.AreEqual(123UL, cpu.GetY().Value);
    }

    [TestMethod]
    [DataRow(0x1Au)]
    [DataRow(0x26u)]
    [DataRow(0x27u)]
    public void UnusedOpcodesAreForbiddenInMathematicalMode(uint opcode)
    {
        var (cpu, memory, supervisor) = Create(Half(opcode, 0));
        MakeMathematical(cpu, memory, supervisor);
        cpu.SetA(123);
        cpu.Step();
        Assert.IsFalse(cpu.LastStepCompleted);
        Assert.AreEqual(1UL << 12, supervisor.InternalInterrupts);
        Assert.AreEqual(123UL, cpu.GetA().Value);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void StopWithPanelDisabledUsesPokRegardlessOfSupervisorMode(bool mathematical, bool pok)
    {
        var (cpu, memory, supervisor) = Create(Half((uint)Opcode.Stop, 123));
        if (mathematical) MakeMathematical(cpu, memory, supervisor);
        supervisor.Status = new((ControlUnitFlags)(pok ? 8 : 0));
        supervisor.PanelStopEnabled = false;
        cpu.SetA(456);
        cpu.SetY(789);
        cpu.SetR(0x23);
        Assert.IsFalse(cpu.Step());
        Assert.AreEqual(pok ? 8u : 0x173u, cpu.GetK());
        Assert.AreEqual(pok, cpu.RightInstruction);
        Assert.AreEqual(456UL, cpu.GetA().Value);
        Assert.AreEqual(789UL, cpu.GetY().Value);
        Assert.AreEqual(0x23u, cpu.GetR());
    }

    private static void MakeMathematical(Processor cpu, MappedMemoryBackend memory, SupervisorControl supervisor)
    {
        memory.Assignment.SetPhysicalPage(0, 1);
        memory.HostMemory.Write(1032, memory.HostMemory.Read(8));
        supervisor.Status = new(ControlUnitFlags.None);
        supervisor.Mode = SupervisorMode.Mathematical;
        cpu.StartAt(8);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void InstructionZeroUsesOrdinaryAdmissionAndControlInsteadOfDubnaAbort(bool mathematical, bool closed)
    {
        var (cpu, memory, supervisor) = Create(0);
        memory.HostMemory.Write(mathematical ? 8192u : 0u, new((ulong)Half((uint)Opcode.Vtm, 99, 1) << 24));
        supervisor.Status = new((ControlUnitFlags)0x403);
        if (mathematical)
        {
            memory.Assignment.SetPhysicalPage(0, closed ? 0u : 8u);
            supervisor.Mode = SupervisorMode.Mathematical;
        }
        cpu.StartAt(0);
        cpu.Step();
        if (closed)
        {
            Assert.IsFalse(cpu.LastStepCompleted);
            Assert.AreEqual(1UL << 13, supervisor.InternalInterrupts);
            Assert.AreEqual(0u, supervisor.ReadModifier(27));
            Assert.AreEqual(0x140u, cpu.GetK());
        }
        else
        {
            Assert.IsTrue(cpu.LastStepCompleted);
            Assert.AreEqual(99u, cpu.GetM(1));
            Assert.AreEqual(0u, cpu.GetK());
            Assert.IsTrue(cpu.RightInstruction);
        }
    }

    [TestMethod]
    public void HostSnapshotObservesHiddenStateInMathematicalModeAndOwnsItsArrays()
    {
        var (_, memory, supervisor) = Create(0);
        supervisor.WriteModifier(16, 123);
        supervisor.WriteModifier(23, 0x713);
        supervisor.WriteModifier(28, 987);
        supervisor.WriteModifier(29, 654);
        supervisor.PanelStopEnabled = false;
        supervisor.Mode = SupervisorMode.Mathematical;
        var snapshot = supervisor.Snapshot();
        Assert.AreEqual(123u, snapshot.SpecialRegisters[0]);
        Assert.AreEqual(0x713u, snapshot.SpecialRegisters[7]);
        Assert.AreEqual(0x8000u | 987u, snapshot.SpecialRegisters[12]);
        Assert.AreEqual(0x8000u | 654u, snapshot.SpecialRegisters[13]);
        Assert.AreEqual(SupervisorMode.Mathematical, snapshot.Mode);
        snapshot.SpecialRegisters[13] = 0;
        Assert.AreEqual(0x8000u | 654u, supervisor.Snapshot().SpecialRegisters[13]);
        supervisor.ResetCpu();
        Assert.IsFalse(supervisor.PanelStopEnabled);
        Assert.AreEqual(0x40Fu, supervisor.Status.EncodedControls);
        Assert.IsTrue(memory.Supervisor);
        Assert.AreEqual(0u, supervisor.Snapshot().SpecialRegisters[13]);
    }

    [TestMethod]
    public void ExtracodeConsumesPendingAddressModificationBeforeReturningToNextWord()
    {
        var (cpu, memory, supervisor) = Create(Half((uint)Opcode.Utc, 5), Half(0x28, 123));
        memory.HostMemory.Write(9, new((ulong)Half((uint)Opcode.Vtm, 99, 1) << 24));
        memory.HostMemory.Write(0x168, new((ulong)Half((uint)Opcode.Ij, 0, 2) << 24));
        cpu.Step();
        cpu.Step();
        Assert.AreEqual(128u, cpu.GetM(14));
        Assert.AreEqual(0u, supervisor.SavedFlags & 0x30);
        cpu.Step();
        cpu.Step();
        Assert.AreEqual(99u, cpu.GetM(1));
    }

    [TestMethod]
    public void OperandFaultPreservesPendingModifierForGuestRetry()
    {
        var (cpu, memory, supervisor) = Create(Half((uint)Opcode.Utc, 3), Half((uint)Opcode.Xta, 2050));
        MakeMathematical(cpu, memory, supervisor);
        memory.Assignment.SetPhysicalPage(2, 9);
        memory.Assignment.OperandProtectionMask = 4;
        cpu.Step();
        cpu.Step();
        Assert.IsFalse(cpu.LastStepCompleted);
        Assert.AreEqual(0x10u, supervisor.SavedFlags & 0x10);
        Assert.AreEqual(3u, supervisor.Snapshot().SpecialRegisters[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZeroAddressModificationSurvivesOperandFaultAndInterruptReturn(bool indirect)
    {
        var (cpu, memory, supervisor) = Create(
            Half((uint)(indirect ? Opcode.Wtc : Opcode.Utc), indirect ? 100u : 0u),
            Half((uint)Opcode.Xta, 2050));
        MakeMathematical(cpu, memory, supervisor);
        memory.HostMemory.Write(1124, new(0)); // Mathematical page0 maps to physical page1.
        memory.HostMemory.Write(1033, new(Half((uint)Opcode.Xta, 2050)));
        memory.Assignment.SetPhysicalPage(2, 9);
        memory.Assignment.OperandProtectionMask = 4;
        memory.HostMemory.Write(0x140, new((ulong)Half((uint)Opcode.Ij, 0, 3) << 24));
        memory.HostMemory.Write(9 * 1024 + 2, new(0x1234));

        cpu.Step();
        Assert.IsTrue(cpu.ApplyC);
        Assert.AreEqual(0u, cpu.C);
        cpu.Step();
        Assert.IsFalse(cpu.LastStepCompleted);
        Assert.AreEqual(0x10u, supervisor.SavedFlags & 0x10);
        Assert.AreEqual(0u, supervisor.Snapshot().SpecialRegisters[0]);

        memory.Assignment.OperandProtectionMask = 0;
        cpu.Step(); // Interrupt return restores even a zero pending modifier.
        Assert.IsTrue(cpu.ApplyC);
        Assert.AreEqual(0u, cpu.C);
        Assert.AreEqual(9u, cpu.K); // The existing serial fault boundary saves the next word.
        Assert.IsTrue(cpu.RightInstruction);
        cpu.Step();
        Assert.AreEqual(0x1234UL, cpu.GetA().Value);
        Assert.IsFalse(cpu.ApplyC);
    }

    [TestMethod]
    public void SupervisorSettledWriteBoundaryPublishesOldestAndEightPanelStoresDrainRemainingSeven()
    {
        var (cpu, memory, _) = Create(0);
        for (uint i = 0; i < 4; i++)
            memory.HostMemory.Write(8 + i, new(((ulong)Half((uint)Opcode.Atx, 100 + i * 2) << 24) |
                Half((uint)Opcode.Atx, 101 + i * 2)));
        for (uint i = 0; i < 4; i++)
            memory.HostMemory.Write(12 + i, new(((ulong)Half((uint)Opcode.Atx, 1) << 24) | Half((uint)Opcode.Atx, 1)));
        cpu.SetA(0x1234);
        for (int i = 0; i < 8; i++) cpu.Step();
        Assert.AreEqual(7, memory.PendingWriteCount);
        Assert.AreEqual(0x1234UL, memory.PhysicalMemory.ReadRaw(100).Data.Value);
        for (uint address = 101; address <= 107; address++)
        {
            Assert.AreEqual(0UL, memory.PhysicalMemory.ReadRaw(address).Data.Value);
            Assert.AreEqual(0x1234UL, memory.Read(address).Value);
        }
        for (int i = 0; i < 8; i++) cpu.Step();
        Assert.AreEqual(0, memory.PendingWriteCount);
        for (uint address = 100; address <= 107; address++)
            Assert.AreEqual(0x1234UL, memory.PhysicalMemory.ReadRaw(address).Data.Value);
    }

    [TestMethod]
    public void HostedMappedPolicyRetainsEightPendingAndNeedsNinePanelStores()
    {
        var memory = new MappedMemoryBackend();
        for (uint address = 100; address <= 107; address++) memory.Write(address, new(0x1234));
        Assert.AreEqual(8, memory.PendingWriteCount);
        for (int i = 0; i < 8; i++) memory.Write(1, new(0x1234));
        Assert.AreEqual(1, memory.PendingWriteCount);
        memory.Write(1, new(0x1234));
        Assert.AreEqual(0, memory.PendingWriteCount);
    }

    [TestMethod]
    public void GuestRawBrzPortKeepsItsPhysicalRegisterIdentityAndPendingAddress()
    {
        var (cpu, memory, _) = Create(Half((uint)Opcode.Atx, 100), Half((uint)Opcode.Atx, 101));
        memory.HostMemory.Write(9, new(((ulong)Half((uint)Opcode.Mod, 0) << 24) | Half((uint)Opcode.Mod, 0x80)));
        memory.HostMemory.Write(10, new((ulong)Half((uint)Opcode.Xta, 100) << 24));
        cpu.SetA(11);
        cpu.Step();
        cpu.SetA(22);
        cpu.Step();
        cpu.SetA(99);
        cpu.Step(); // REG0 write changes the data, retaining its old physical slot and address.
        cpu.SetA(0);
        cpu.Step();
        Assert.AreEqual(99UL, cpu.GetA().Value);
        cpu.Step();
        Assert.AreEqual(99UL, cpu.GetA().Value);
        memory.FlushOperands();
        Assert.AreEqual(99UL, memory.PhysicalMemory.ReadRaw(100).Data.Value);
        Assert.AreEqual(22UL, memory.PhysicalMemory.ReadRaw(101).Data.Value);
    }

    private sealed class TestIo : ISupervisorIo
    {
        public uint LastAddress { get; private set; }
        public ulong PendingExternalInterrupts => 0;
        public Word48 ReadRegister(uint register) => throw new NotSupportedException();
        public void WriteRegister(uint register, Word48 value) => throw new NotSupportedException();
        public Word48 ReadDevice(uint address) { LastAddress = address; return new(0x123456); }
        public void WriteDevice(uint address, Word48 value) { LastAddress = address; Assert.AreEqual(0xABCDEFUL, value.Value); }
    }
}
