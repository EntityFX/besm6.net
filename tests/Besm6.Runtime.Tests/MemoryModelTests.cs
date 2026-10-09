using Besm6;

namespace Besm6.Tests;

[TestClass]
public sealed class MemoryModelTests
{
    private static Word48 Code(string text) => new(Besm6.Asm.Assembler.Asm(text));

    [TestMethod]
    public void DefaultRemainsDubnaAndSelectionIsPerMachine()
    {
        var old = new MachineCore();
        var buffered = new MachineCore(memoryModel: MemoryModel.Buffered);
        Assert.AreEqual(MemoryModel.Dubna, old.MemoryModel);
        Assert.IsNotNull(typeof(MachineCore).GetConstructor(new[] { typeof(uint), typeof(string) }));
        Assert.IsNull(old.BufferedMemory);
        Assert.AreEqual(MemoryModel.Buffered, buffered.MemoryModel);
        Assert.IsNotNull(buffered.BufferedMemory);
        buffered.Memory.Write(16, new(123));
        Assert.AreEqual(0UL, old.Memory.Read(16).Value);
        Assert.AreEqual(123UL, buffered.Memory.Read(16).Value);
    }

    [TestMethod]
    public void InvalidSelectionAndUnsupportedSizeAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineCore(memoryModel: (MemoryModel)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineCore(memoryModel: MemoryModel.Buffered, memorySize: 65536));
        Assert.AreEqual(16, new MachineCore(16).Memory.Size);
    }

    private static (MachineCore Machine, DubnaLoader Loader) Run(MemoryModel model, ExecutionSpeed speed, bool trace)
    {
        var machine = new MachineCore(memoryModel: model);
        machine.LoadProgram(new[] { Code("xta 20, atx 21"), Code("xta 21, stop") }, 8);
        machine.Memory.Write(16, new(123));
        var loader = new DubnaLoader(machine) { Speed = speed, CollectStatistics = true, InstructionLimit = 100 };
        if (trace) machine.StepTrace = (_, _) => { };
        Assert.IsTrue(loader.RunLoaded().Success);
        Assert.AreEqual(123UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(123UL, machine.Memory.Read(17).Value);
        Assert.AreEqual(4UL, machine.Clock.Tick);
        Assert.AreEqual(4L, loader.Statistics!.CompletedInstructions);
        return (machine, loader);
    }

    [TestMethod]
    [DataRow(MemoryModel.Dubna, false)]
    [DataRow(MemoryModel.Dubna, true)]
    [DataRow(MemoryModel.Buffered, false)]
    [DataRow(MemoryModel.Buffered, true)]
    public void OriginalAndMaxPreserveFullStateWithinEachMemoryModel(MemoryModel model, bool trace)
    {
        var original = Run(model, ExecutionSpeed.Original, trace);
        var max = Run(model, ExecutionSpeed.Max, trace);
        Assert.AreEqual(original.Machine.Cpu.GetA(), max.Machine.Cpu.GetA());
        Assert.AreEqual(original.Machine.Cpu.GetY(), max.Machine.Cpu.GetY());
        Assert.AreEqual(original.Machine.Cpu.GetR(), max.Machine.Cpu.GetR());
        Assert.AreEqual(original.Machine.Cpu.GetK(), max.Machine.Cpu.GetK());
        Assert.AreEqual(original.Machine.Cpu._rightInstrFlag, max.Machine.Cpu._rightInstrFlag);
        Assert.AreEqual(original.Machine.Cpu.C, max.Machine.Cpu.C);
        Assert.AreEqual(original.Machine.Cpu.ApplyC, max.Machine.Cpu.ApplyC);
        for (int i = 0; i < 16; i++) Assert.AreEqual(original.Machine.Cpu.GetM(i), max.Machine.Cpu.GetM(i));
        for (uint address = 0; address < 32768; address++)
            Assert.AreEqual(original.Machine.Memory.Read(address), max.Machine.Memory.Read(address));
        Assert.AreEqual(original.Loader.Statistics!.ModelCycles, max.Loader.Statistics!.ModelCycles);
        if (model == MemoryModel.Buffered)
        {
            CollectionAssert.AreEqual(original.Machine.BufferedMemory!.Buffers.GetOperandSnapshot(),
                max.Machine.BufferedMemory!.Buffers.GetOperandSnapshot());
            CollectionAssert.AreEqual(original.Machine.BufferedMemory.Buffers.GetInstructionSnapshot(),
                max.Machine.BufferedMemory.Buffers.GetInstructionSnapshot());
            for (uint address = 0; address < 32768; address++)
                Assert.AreEqual(original.Machine.BufferedMemory.PhysicalMemory.ReadRaw(address),
                    max.Machine.BufferedMemory.PhysicalMemory.ReadRaw(address));
        }
    }

    [TestMethod]
    [DataRow(MemoryModel.Dubna, 7u)]
    [DataRow(MemoryModel.Buffered, 3u)]
    public void SelfModificationShowsTheSelectedMemoryVisibility(MemoryModel model, uint expected)
    {
        var machine = new MachineCore(memoryModel: model);
        machine.LoadProgram(new[] { Code("atx 10, vtm 3(2)") }, 8);
        machine.Cpu.SetA(Code("atx 10, vtm 7(2)").Value);
        Assert.IsFalse(machine.Step());
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(expected, machine.Cpu.GetM(2));
        Assert.AreEqual(Code("atx 10, vtm 7(2)"), machine.Memory.Read(8));
        if (model == MemoryModel.Buffered)
        {
            Assert.AreEqual(Code("atx 10, vtm 3(2)"), machine.BufferedMemory!.PhysicalMemory.ReadRaw(8).Data);
            Assert.AreEqual(1, machine.BufferedMemory.Buffers.PendingWriteCount);
        }
    }

    [TestMethod]
    public void ReloadAfterRightHalfReplacesWarmRegistersAndPendingWrite()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        machine.LoadProgram(new[] { Code("atx 10, vtm 3(2)") }, 8);
        machine.Cpu.SetA(Code("vtm 7(2), stop").Value);
        machine.Step();
        machine.Step();
        Assert.AreEqual(1, machine.BufferedMemory!.Buffers.PendingWriteCount);
        machine.LoadProgram(new[] { Code("vtm 11(2), stop") }, 8);
        Assert.AreEqual(0, machine.BufferedMemory.Buffers.PendingWriteCount);
        Assert.AreEqual(0, machine.BufferedMemory.Buffers.GetInstructionSnapshot().Length);
        Assert.IsFalse(machine.Cpu._rightInstrFlag);
        machine.Step();
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(9u, machine.Cpu.GetM(2)); // octal 11
        Assert.AreEqual(4UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void ResetCpuPreservesBothBuffersAndModelTime()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        machine.LoadProgram(new[] { Code("atx 20, stop") }, 8);
        machine.Cpu.SetA(123);
        machine.Step();
        var operands = machine.BufferedMemory!.Buffers.GetOperandSnapshot();
        var instructions = machine.BufferedMemory.Buffers.GetInstructionSnapshot();
        machine.Scheduler.Schedule(5, () => { });
        machine.ResetCpu();
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(123UL, machine.Memory.Read(16).Value);
        CollectionAssert.AreEqual(operands, machine.BufferedMemory.Buffers.GetOperandSnapshot());
        CollectionAssert.AreEqual(instructions, machine.BufferedMemory.Buffers.GetInstructionSnapshot());
    }

    [TestMethod]
    [DataRow(1u)]
    [DataRow(2u)]
    public void WatchpointStopsBeforeBufferedOperandEffect(uint mode)
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        machine.LoadProgram(new[] { Code(mode == 1 ? "atx 20" : "xta 20"), Code("stop") }, 8);
        machine.Cpu.SetA(123);
        machine.Cpu.ArmDebugWatch(8, false, mode, 16, 9);
        machine.Step();
        Assert.AreEqual(0, machine.BufferedMemory!.Buffers.GetOperandSnapshot().Length);
        Assert.AreEqual(0UL, machine.Memory.Read(16).Value);
        Assert.AreEqual(123UL, machine.Cpu.GetA().Value);
    }

    [TestMethod]
    public void ControlFaultIsNotGuestInterceptAndDoesNotEarnTickOrNotification()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        machine.LoadProgram(new[] { Code("xta 20, stop") }, 8);
        machine.BufferedMemory!.Buffers.StoreRaw(16, new(0));
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 9;
        int executed = 0;
        machine.Cpu.InstructionExecuted = _ => executed++;
        var loader = new DubnaLoader(machine) { InstructionLimit = 100 };
        Assert.ThrowsExactly<MemoryControlException>(() => loader.RunLoaded());
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0L, loader.InstructionsExecuted);
        Assert.AreEqual(0, executed);
        Assert.AreEqual(1, machine.Cpu.InterceptCount);
    }

    [TestMethod]
    public void CpuChecksSelectedCommandHalfAndCountsOnlyCompletedLeftHalf()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        Word48 code = Code("vtm 1(2), stop");
        machine.LoadProgram(new[] { code }, 8);
        var command = MemoryWord50.Form(code, false, false);
        machine.BufferedMemory!.Buffers.ReplaceFromHost(8, new(command.RawValue ^ (1UL << 48)));
        int executed = 0;
        machine.Cpu.InstructionExecuted = _ => executed++;
        Assert.IsFalse(machine.Step());
        var fault = Assert.ThrowsExactly<MemoryControlException>(() => machine.Step());
        Assert.AreEqual(MemoryAccessKind.InstructionRight, fault.AccessKind);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1, executed);
        Assert.AreEqual(1u, machine.Cpu.GetM(2));
    }

    [TestMethod]
    [DataRow(MemoryModel.Dubna)]
    [DataRow(MemoryModel.Buffered)]
    public void HostedCtxPublishesCommandAndRefreshesNextFetch(MemoryModel model)
    {
        var machine = new MachineCore(memoryModel: model);
        machine.LoadProgram(new[] { Code("atx 12, *75 12"), Code("uj 12"), Code("vtm 3(2), stop") }, 8);
        machine.Cpu.SetA(Code("vtm 7(2), stop").Value);
        var loader = new DubnaLoader(machine) { InstructionLimit = 100 };
        Assert.IsTrue(loader.RunLoaded().Success);
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        if (model == MemoryModel.Buffered)
        {
            var backend = machine.BufferedMemory!;
            Assert.AreEqual(Code("vtm 7(2), stop"), backend.PhysicalMemory.ReadRaw(10).Data);
            Assert.IsTrue(backend.PhysicalMemory.ReadRaw(10).HasValidInstructionControl(false));
            Assert.IsFalse(backend.Buffers.GetOperandSnapshot().Any(e => e.PhysicalAddress == 10));
        }
    }

    [TestMethod]
    public void HostObservationDoesNotChangeRegisterRecencyOrCheckGuestControl()
    {
        var machine = new MachineCore(memoryModel: MemoryModel.Buffered);
        var backend = machine.BufferedMemory!;
        backend.Write(16, new(123));
        backend.Write(17, new(456));
        var before = backend.Buffers.GetOperandSnapshot();
        Assert.AreEqual(123UL, machine.Memory.Read(16).Value);
        CollectionAssert.AreEqual(before, backend.Buffers.GetOperandSnapshot());
        backend.Buffers.StoreRaw(18, new(0));
        Assert.AreEqual(0UL, machine.Memory.Read(18).Value);
        Assert.ThrowsExactly<MemoryControlException>(() => backend.Read(18));
    }
}
