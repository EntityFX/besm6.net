using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Configuration;
using Besm6.Processor.Supervisor;

namespace Besm6.Tests;

[TestClass]
public sealed class HostedSupervisorTests
{
    private static MachineCore Program(params string[] words)
    {
        var machine = MachineCore.CreateHostedSupervisor();
        machine.LoadProgram(words.Select(word => new Word48(Assembler.Legacy.Assembler.Asm(word))).ToArray(), 512);
        return machine;
    }

    [TestMethod]
    public void HostedAddressSpaceDoesNotReplacePanelOrCpuProfile()
    {
        var machine = MachineCore.CreateHostedSupervisor();
        machine.Memory.Write(3, Word48.FromDouble(3));
        Assert.AreEqual(ProcessorProfile.Supervisor, machine.Cpu.Profile);
        Assert.AreEqual(MemoryModel.Mapped, machine.MemoryModel);
        Assert.AreEqual(32768, machine.Memory.Size);
        Assert.AreEqual(Word48.FromDouble(3), machine.Memory.Read(3));
        Assert.AreEqual(0UL, machine.MappedMemory!.GetPanel(3).Data.Value);
        Assert.AreEqual(Word48.FromDouble(3), machine.MappedMemory.PhysicalMemory.ReadRaw(1027).Data);
        Assert.AreEqual(1U, machine.MappedMemory.Assignment.GetPhysicalPage(0));
        Assert.AreEqual(32U, machine.MappedMemory.Assignment.GetPhysicalPage(31));
    }

    [TestMethod]
    public void OperandWritesAndInstructionFetchesUseRealBuffersWithoutInventingReadRegisters()
    {
        var machine = Program("ntr 6, xta 1010", "atx 1011, xta 1011", "a+x 1010, atx 1011", "stop");
        machine.Memory.Write(520, Word48.FromDouble(3));
        bool stored = false, fetched = false;
        var loader = new DubnaLoader(machine) { InstructionLimit = 100, CollectStatistics = true };
        loader.ExecutionCheckpoint = _ =>
        {
            stored |= machine.MappedMemory!.GetOperandSnapshot().Any(entry => entry.Request.Address == 521);
            fetched |= machine.MappedMemory!.GetInstructionSnapshot().Any(entry => entry.Request.Address == 513);
        };
        var result = loader.RunLoaded();
        Assert.IsTrue(result.Stopped);
        Assert.IsTrue(stored, "The result store must pass through BRZ.");
        Assert.IsTrue(fetched, "Instructions must pass through BRS.");
        Assert.AreEqual(Word48.FromDouble(6), machine.Memory.Read(521));
        var snapshot = machine.InspectRegisters();
        Assert.AreEqual("Supervisor", snapshot.ProcessorProfile);
        Assert.AreEqual("HostedDubna", snapshot.ServiceRuntime);
        Assert.AreEqual(8, snapshot.Buffers.Count(buffer => buffer.Name == "БРЗ"));
        Assert.IsFalse(snapshot.Buffers.Any(buffer => buffer.Name == "БРЧ"));
        Assert.IsNull(snapshot.HardwareNanoseconds);
        Assert.AreEqual(loader.InstructionsExecuted, loader.Statistics!.CompletedInstructions);
    }

    [TestMethod]
    public void CpuEntersNativeExtracodeBeforeHostServiceAndReturnsToMathematicalMode()
    {
        var machine = Program("xta 1010, *50", "atx 1011, stop");
        machine.Memory.Write(520, Word48.FromDouble(16));
        bool nativeEntry = false, returned = false;
        machine.Cpu.InstructionExecuted += opcode =>
        {
            if ((uint)opcode == 0x28)
                nativeEntry = machine.Cpu.Supervisor!.Mode == SupervisorMode.Extracode
                    && machine.Cpu.Supervisor.ReadModifier(26) == 513;
        };
        var loader = new DubnaLoader(machine) { InstructionLimit = 100 };
        loader.InstructionExecuted = opcode =>
        {
            if ((uint)opcode == 0x28)
                returned = machine.Cpu.Supervisor!.Mode == SupervisorMode.Mathematical && machine.Cpu.K == 513;
        };
        Assert.IsTrue(loader.RunLoaded().Stopped);
        Assert.IsTrue(nativeEntry);
        Assert.IsTrue(returned);
        Assert.AreEqual(Word48.FromDouble(4), machine.Memory.Read(521));
    }

    [TestMethod]
    public void GeneratedCodeIsPublishedByRuntimeAndExecutedBySupervisor()
    {
        var machine = Program("xta 1010, atx 1002", "uj 1002, stop", "stop");
        var generated = new Word48(Assembler.Legacy.Assembler.Asm("vtm 7(1), stop"));
        machine.Memory.Write(520, generated);
        var loader = new DubnaLoader(machine) { InstructionLimit = 100 };
        Assert.IsTrue(loader.RunLoaded().Stopped);
        Assert.AreEqual(7U, machine.Cpu.GetM(1));
        uint physical = machine.MappedMemory!.Assignment.TranslateRequest(new(514, false));
        Assert.IsTrue(machine.MappedMemory.PhysicalMemory.ReadRaw(physical).HasValidInstructionControl(false));
    }

    [TestMethod]
    public void NativeSupervisorDoesNotAcquireHostServices()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((Architecture.State.ControlUnitFlags)0x403);
        machine.MappedMemory!.PhysicalMemory.WriteRaw(8,
            MemoryWord50.Form(new(Assembler.Legacy.Assembler.Asm("*50, stop")), false, false));
        machine.Cpu.StartAt(8);
        bool hostCalled = false;
        machine.Cpu.ExtracodeDispatch = _ => { hostCalled = true; return true; };
        var result = machine.RunInstructions(1);
        Assert.IsTrue(result.Outcome.LimitExceeded);
        Assert.IsFalse(hostCalled);
        Assert.AreEqual(SupervisorMode.Extracode, machine.Cpu.Supervisor.Mode);
        Assert.IsFalse(machine.UsesHostedSupervisorServices);
    }

    [TestMethod]
    public void CorruptControlBitsAreNotSilentlyRepaired()
    {
        var machine = Program("stop, stop");
        uint physical = machine.MappedMemory!.Assignment.TranslateRequest(new(512, false));
        var word = machine.MappedMemory.PhysicalMemory.ReadRaw(physical);
        machine.MappedMemory.PhysicalMemory.WriteRaw(physical, new(word.RawValue ^ (1UL << 49)));
        var loader = new DubnaLoader(machine) { InstructionLimit = 10 };
        Assert.ThrowsExactly<SupervisorHaltException>(() => loader.RunLoaded());
    }

    [TestMethod]
    public void MathematicalJobCannotExecutePrivilegedInstructions()
    {
        var machine = Program("mod 1, stop");
        var loader = new DubnaLoader(machine) { InstructionLimit = 10 };
        Assert.ThrowsExactly<SupervisorHaltException>(() => loader.RunLoaded());
    }

    [TestMethod]
    public void HostedArithmeticInterceptIsAdaptedWithoutChangingNativeCpuRules()
    {
        var machine = Program("a/x 1010, stop", "stop, stop");
        machine.Memory.Write(520, Word48.Zero);
        machine.Cpu.InterceptCount = 1;
        machine.Cpu.InterceptAddr = 513;
        var loader = new DubnaLoader(machine) { InstructionLimit = 10 };
        Assert.IsTrue(loader.RunLoaded().Stopped);
        Assert.AreEqual(0, machine.Cpu.InterceptCount);
    }

    [TestMethod]
    public void ZeroLimitRemainsCancellable()
    {
        var machine = Program("uj 1000, uj 1000");
        var loader = new DubnaLoader(machine) { InstructionLimit = 0 };
        loader.ExecutionCheckpoint = count => { if (count >= 10) throw new OperationCanceledException(); };
        Assert.ThrowsExactly<OperationCanceledException>(() => loader.RunLoaded());
        Assert.AreEqual(10L, loader.InstructionsExecuted);
    }

    [TestMethod]
    public void HostedExitCountsItsNativeEntryOnlyOnce()
    {
        var machine = Program("*74, stop");
        var loader = new DubnaLoader(machine) { InstructionLimit = 10, CollectStatistics = true };
        Assert.IsTrue(loader.RunLoaded().Stopped);
        Assert.AreEqual(1L, loader.InstructionsExecuted);
        Assert.AreEqual(1L, loader.Statistics!.CompletedInstructions);
        Assert.AreEqual(SupervisorMode.Mathematical, machine.Cpu.Supervisor!.Mode);
    }
}
