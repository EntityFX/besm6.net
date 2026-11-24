using System.Text.Json;
using Besm6.Assembler.Assembly;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

namespace Besm6.Runtime.Tests;

[TestClass]
public sealed class MachineInspectionTests
{
    [TestMethod]
    public void BufferedInspectionPreservesRecencyMemoryAndClock()
    {
        var machine = MachineFactory.CreateMachine(new Config { BufferedMemory = true });
        machine.CommandMemory.StoreCommand(512, new(new MadlenAssembler().AssembleWord("xta 1003, atx 1004")));
        machine.Memory.Write(515, new Word48(42)); machine.Cpu.StartAt(512); machine.Step(); machine.Step();
        string before = JsonSerializer.Serialize(machine.BufferedMemory!.Buffers.GetOperandSnapshot());
        ulong ticks = machine.Clock.Tick;
        var snapshot = machine.InspectRegisters();
        Assert.IsTrue(snapshot.Buffers.Any(b => b.Name.StartsWith("БРЗ") && b.Value == 42));
        Assert.IsTrue(snapshot.Buffers.All(b => b.Raw50.HasValue && new MemoryWord50(b.Raw50.Value).Data.Value == b.Value));
        Assert.AreEqual(before, JsonSerializer.Serialize(machine.BufferedMemory.Buffers.GetOperandSnapshot()));
        Assert.AreEqual(ticks, machine.Clock.Tick); Assert.IsNull(snapshot.HardwareNanoseconds);
    }

    [TestMethod]
    public void MappedInspectionExposesPhysicalWordsWithTheirControlBits()
    {
        var machine = new MachineJobConfiguration().CreateMachine(MachineExecutionModel.Functional);
        var memory = machine.MappedMemory!;
        var command = MemoryWord50.Form(new(new MadlenAssembler().AssembleWord("stop, stop")), false, false);
        memory.PhysicalMemory.WriteRaw(8, command);
        memory.FetchInstruction(8, false);
        memory.WriteOperandBufferRegister(7, new Word48(0x800000000001));
        string before = JsonSerializer.Serialize(memory.GetInstructionSnapshot());

        var snapshot = machine.InspectRegisters();
        Assert.AreEqual(8, snapshot.Buffers.Count(b => b.Name == "БРЗ"));
        Assert.AreEqual(4, snapshot.Buffers.Count(b => b.Name == "БРС"));
        Assert.AreEqual(command.RawValue, snapshot.Buffers.Single(b => b.Name == "БРС" && b.Slot == 0).Raw50);
        Assert.AreEqual(memory.ReadOperandBufferRegister(7).RawValue,
            snapshot.Buffers.Single(b => b.Name == "БРЗ" && b.Slot == 7).Raw50);
        Assert.AreEqual(before, JsonSerializer.Serialize(memory.GetInstructionSnapshot()));
        Assert.AreEqual(command, memory.ReadInstructionBufferRegister(0));
    }

    [TestMethod]
    [DataRow(MachineExecutionModel.Functional)]
    [DataRow(MachineExecutionModel.Hardware)]
    public void ExistingExecutionOwnersKeepTheSameResultWithInspections(MachineExecutionModel model)
    {
        var job = new MachineJobConfiguration
        {
            MemoryWords = [new() { Address = 8, Raw50 = MemoryWord50.Form(new(new MadlenAssembler().AssembleWord("vtm 1(1), stop")), false, false).RawValue }],
            Timing = new() { BankCycleNs=2000, ReadLatencyNs=900, WriteVisibilityNs=1500, BufferHitNs=100, ArithmeticCycleNs=100,
                ControlDurationsNs=new(){["Vtm"]=300,["Stop"]=300}, Devices=new(){ExchangeReadyNs=20000,ConsoleReplyNs=700,PaperFeedNs=900,
                    PrinterCharacterNs=100,PrinterZeroNs=50,TimerPeriodNs=500,SerialPeriodNs=30} }
        };
        var baseline = job.CreateMachine(model); var inspected = job.CreateMachine(model);
        var expected = baseline.RunInstructions(10);
        var actual = inspected.RunInstructions(10, executionCheckpoint: _ => inspected.InspectRegisters());
        Assert.AreEqual(JsonSerializer.Serialize(expected.Outcome), JsonSerializer.Serialize(actual.Outcome));
        Assert.AreEqual(baseline.Cpu.GetM(1), inspected.Cpu.GetM(1));
        Assert.AreEqual(model == MachineExecutionModel.Hardware ? 4 : 0, inspected.InspectRegisters().Buffers.Count(b => b.Name == "БРЧ"));
    }
}
