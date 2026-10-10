using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ProcessorExecutionOwnershipTests
{
    [TestMethod]
    public void OwnersAlternateOnTheSameCpuMemoryAndDevicesWithoutCopyingState()
    {
        var machine = new MachineCore();
        machine.LoadProgram([
            new(Besm6.Asm.Assembler.Asm("xta 10, atx 11")),
            new(Besm6.Asm.Assembler.Asm("stop, stop"))], 1);
        machine.Memory.Write(8, new(123));
        var cpu = machine.Cpu;
        var memory = machine.Memory;
        var devices = machine.Devices;
        var model = machine.HardwareModel;

        var load = model.PrepareNextInstruction();
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Step());
        model.ScheduleCompletionAt(load, new(100));
        Assert.AreEqual(HardwareInstructionStatus.Completed, model.AdvanceTo(new(100))!.Value.Status);
        Assert.AreEqual(123UL, cpu.GetA().Value);
        Assert.AreEqual(0UL, machine.Clock.Tick);

        Assert.IsFalse(machine.Step());
        Assert.AreEqual(123UL, memory.Read(9).Value);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreEqual(new HardwareInstant(100), model.Timeline.Now);

        var stop = model.PrepareNextInstruction();
        model.ScheduleCompletionAt(stop, new(200));
        Assert.IsTrue(model.AdvanceTo(new(200))!.Value.Stopped);
        Assert.AreEqual(2UL, model.CompletedInstructions);
        Assert.AreEqual(1UL, machine.Clock.Tick);
        Assert.AreSame(cpu, machine.Cpu);
        Assert.AreSame(cpu, machine.Processor);
        Assert.AreSame(memory, machine.Memory);
        Assert.AreSame(devices, machine.Devices);
    }

    [TestMethod]
    public void OwnershipGuardsBelongToOneMachineAndRecoverAfterItsCallback()
    {
        var first = new MachineCore();
        var second = new MachineCore();
        first.LoadProgram([new(Besm6.Asm.Assembler.Asm("vtm 1(1), stop"))], 1);
        second.LoadProgram([new(Besm6.Asm.Assembler.Asm("vtm 2(1), stop"))], 1);
        first.HardwareTimeline.ScheduleAt(new(100), () =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => first.Step());
            Assert.IsFalse(second.Step());
            second.HardwareTimeline.AdvanceTo(new(50));
        });
        first.HardwareTimeline.AdvanceTo(new(100));
        Assert.AreEqual(0U, first.Cpu.GetM(1));
        Assert.AreEqual(2U, second.Cpu.GetM(1));
        Assert.IsFalse(first.Step());
        Assert.AreEqual(1U, first.Cpu.GetM(1));
        Assert.AreEqual(1UL, first.Clock.Tick);
        Assert.AreEqual(1UL, second.Clock.Tick);
    }
}
