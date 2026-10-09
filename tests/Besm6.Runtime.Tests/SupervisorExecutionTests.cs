namespace Besm6.Tests;

[TestClass]
public sealed class SupervisorExecutionTests
{
    private static Word48 Code(string commands) => new(Besm6.Asm.Assembler.Asm(commands));
    private static MachineCore Create(string commands = "сч 1400, сч 1400")
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Memory.Write(1024, Code(commands));
        machine.Memory.Write(1025, Code("стоп, стоп"));
        machine.Memory.Write(768, new(123));
        machine.Cpu.StartAt(1024);
        return machine;
    }

    private static void ScheduleDma(MachineCore machine, ulong delay = 1)
    {
        var image = new MemoryWord50[SupervisorIoController.ZoneWords];
        Array.Fill(image, MemoryWord50.Form(new(987), true, true));
        var io = machine.SupervisorIo!;
        io.CompletionDelayTicks = delay;
        io.AttachDisk(0, image);
        io.WriteDevice(3, new(0x60000)); // Page read to physical 00000..01777.
        io.WriteDevice(19, new(0x800)); // Zone zero address strobe.
    }

    [TestMethod]
    public void DmaBecomesVisibleAfterCompletedInstructionDiagnosticsBeforeNextInstruction()
    {
        var machine = Create();
        ScheduleDma(machine);
        int callbacks = 0;
        machine.Cpu.InstructionExecuted = _ =>
        {
            if (callbacks++ == 0)
            {
                Assert.AreEqual(0UL, machine.Clock.Tick);
                Assert.AreEqual(123UL, machine.MappedMemory!.PhysicalMemory.ReadRaw(768).Data.Value);
            }
        };
        machine.StepTrace = (_, _) =>
        {
            if (machine.Clock.Tick == 1)
                Assert.AreEqual(123UL, machine.MappedMemory!.PhysicalMemory.ReadRaw(768).Data.Value);
        };
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(123UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(987UL, machine.MappedMemory!.PhysicalMemory.ReadRaw(768).Data.Value);
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(987UL, machine.Cpu.GetA().Value);
        Assert.AreEqual(2, callbacks);
        Assert.AreEqual(1, machine.SupervisorIo!.CompletedTransfers);
    }

    [TestMethod]
    public void StepAndBlocksProduceSameDmaStateCountersAndStop()
    {
        var step = Create();
        var block = Create();
        ScheduleDma(step); ScheduleDma(block);
        int stepCount = 0, blockCount = 0;
        step.Cpu.InstructionExecuted = _ => stepCount++;
        block.Cpu.InstructionExecuted = _ => blockCount++;
        while (!step.Step()) { }
        long completed = 0;
        Assert.IsTrue(block.ExecuteBlock(10, ref completed));
        Assert.AreEqual(3L, completed);
        Assert.AreEqual(stepCount, blockCount);
        Assert.AreEqual(step.Clock.Tick, block.Clock.Tick);
        Assert.AreEqual(step.Cpu.GetK(), block.Cpu.GetK());
        Assert.AreEqual(step.Cpu.GetA(), block.Cpu.GetA());
        Assert.AreEqual(step.Cpu.GetY(), block.Cpu.GetY());
        Assert.AreEqual(step.SupervisorIo!.ExternalInterrupts, block.SupervisorIo!.ExternalInterrupts);
        for (uint address = 0; address < PhysicalMemory.WordCount; address++)
            Assert.AreEqual(step.MappedMemory!.PhysicalMemory.ReadRaw(address), block.MappedMemory!.PhysicalMemory.ReadRaw(address));
    }

    [TestMethod]
    public void CancelledInterruptEventDoesNotAffectCpuOrGuestState()
    {
        var machine = Create("уиа 7(1), стоп");
        var token = machine.Scheduler.Schedule(1, () => machine.SupervisorIo!.RaiseInterrupts(SupervisorIoController.TimerInterrupt));
        Assert.IsTrue(machine.Scheduler.Cancel(token));
        Assert.IsFalse(machine.Scheduler.Cancel(token));
        Assert.IsFalse(machine.Step());
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(7U, machine.Cpu.GetM(1));
        Assert.AreEqual(2UL, machine.Clock.Tick);
        Assert.AreEqual(0UL, machine.SupervisorIo!.ExternalInterrupts);
    }

    [TestMethod]
    public void CpuFaultDoesNotAdvanceTicksOrDeliverFutureDma()
    {
        var machine = Create();
        machine.MappedMemory!.PhysicalMemory.WriteRaw(768, MemoryWord50.Form(new(123), false, true));
        ScheduleDma(machine);
        int completed = 0;
        machine.Cpu.InstructionExecuted = _ => completed++;
        Assert.ThrowsExactly<SupervisorHaltException>(() => machine.Step());
        Assert.AreEqual(0UL, machine.Clock.Tick);
        Assert.AreEqual(0, completed);
        Assert.AreEqual(0, machine.SupervisorIo!.CompletedTransfers);
        machine.ResetCpu();
        machine.Cpu.StartAt(1025);
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1, completed);
        Assert.AreEqual(1, machine.SupervisorIo.CompletedTransfers);
    }

    [TestMethod]
    public void InterruptRaisedAfterInstructionEntersVectorBeforeNextGuestHalf()
    {
        var machine = Create("уиа 7(1), уиа 3(1)");
        machine.Memory.Write(0x141, Code("уиа 11(2), стоп"));
        machine.Cpu.Supervisor!.Status = new(ControlUnitFlags.AssignmentBlocked | ControlUnitFlags.ProtectionBlocked);
        machine.SupervisorIo!.WriteRegister(30, new(SupervisorIoController.TimerInterrupt));
        machine.Scheduler.Schedule(1, () => machine.SupervisorIo.RaiseInterrupts(SupervisorIoController.TimerInterrupt));
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(7U, machine.Cpu.GetM(1));
        Assert.AreEqual(1024U, machine.Cpu.GetK());
        Assert.IsFalse(machine.Step());
        Assert.AreEqual(7U, machine.Cpu.GetM(1));
        Assert.AreEqual(9U, machine.Cpu.GetM(2));
        Assert.AreEqual(0x141U, machine.Cpu.GetK());
        Assert.AreEqual(1024U, machine.Cpu.Supervisor.ReadModifier(27));
        Assert.AreNotEqual(0U, machine.Cpu.Supervisor.SavedFlags & SupervisorControl.SavedRightHalf);
        Assert.AreEqual(2UL, machine.Clock.Tick);
    }

    [TestMethod]
    public void RepeatedCpuResetPreservesIoClockMemoryAndPendingDeviceEvent()
    {
        var machine = Create("уиа 7(1), стоп");
        ScheduleDma(machine, delay: 3);
        machine.SupervisorIo!.WriteRegister(30, new(SupervisorIoController.Channel3Free));
        machine.SupervisorIo.ConfigureConsole(0);
        machine.MappedMemory!.Write(2000, new(456));
        machine.MappedMemory.SetPhysicalPage(8, 9);
        Assert.IsFalse(machine.Step());
        var io = machine.SupervisorIo;
        machine.ResetCpu(); machine.ResetCpu();
        Assert.AreSame(io, machine.SupervisorIo);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(0U, machine.Cpu.GetM(1));
        Assert.AreEqual(456UL, machine.Memory.Read(2000).Value);
        Assert.AreEqual(9U, machine.MappedMemory.Assignment.GetPhysicalPage(8));
        Assert.AreEqual(SupervisorIoController.Channel3Free, io.ExternalInterruptMask);
        Assert.AreNotEqual(0U, io.PeripheralInterrupts & 0x200);
        Assert.AreEqual(0, io.CompletedTransfers);
        machine.Cpu.StartAt(1025);
        Assert.IsTrue(machine.Step());
        machine.Cpu.StartAt(1025);
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(3UL, machine.Clock.Tick);
        Assert.AreEqual(1, io.CompletedTransfers);
        Assert.AreEqual(987UL, machine.MappedMemory.PhysicalMemory.ReadRaw(768).Data.Value);
    }

    [TestMethod]
    public void SchedulerFailureKeepsSuccessfulInstructionAndNeverBecomesCpuInterrupt()
    {
        var machine = Create("уиа 7(1), стоп");
        var cause = new InvalidOperationException("Device callback failed.");
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 2000;
        int completed = 0;
        machine.Cpu.InstructionExecuted = _ => completed++;
        machine.Scheduler.Schedule(1, () => throw cause);
        var failure = Assert.ThrowsExactly<SchedulerCallbackException>(() => machine.Step());
        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(1UL, failure.Tick);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(1, completed);
        Assert.AreEqual(7U, machine.Cpu.GetM(1));
        Assert.AreEqual(1, machine.Cpu.InterceptCount);
        Assert.AreEqual(0UL, machine.Cpu.Supervisor!.InternalInterrupts);
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(2UL, machine.Clock.Tick);
    }
}
