using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.State;
using Besm6.Architecture.Supervisor;
using Besm6.Processor.Supervisor;

namespace Besm6.Runtime.Execution;

/// <summary>Explicit runtime bridge, not a processor instruction implementation.
/// The unchanged CPU performs its real extracode entry; the host serves it and
/// restores the saved context using the existing supervisor return operation.</summary>
internal sealed class HostedSupervisorBridge(MachineCore machine, ExtracodeHandler services)
{
    public Opcode LastOpcode { get; private set; }

    public void Prepare()
    {
        if (!machine.UsesHostedSupervisorServices || machine.ExecutionModel != Machine.MachineExecutionModel.Functional)
            throw new InvalidOperationException("Hosted supervisor services require the explicit functional hosted machine.");
        var supervisor = machine.Cpu.Supervisor!;
        // The loader is an external operator: create an unprivileged job context,
        // stop on unexpected faults and leave external hardware interrupts blocked.
        supervisor.Mode = SupervisorMode.Interrupt;
        supervisor.WriteRegister(0x40, Word48.Zero);
        supervisor.Status = new(ControlUnitFlags.ProtectionBlocked | ControlUnitFlags.ExternalInterruptsBlocked |
            ControlUnitFlags.StopOnInternalInterrupt | ControlUnitFlags.StopOnControlInterrupt);
        supervisor.Mode = SupervisorMode.Mathematical;
    }

    public bool Step(ref long completed)
    {
        var cpu = machine.Cpu;
        var memory = machine.MappedMemory!;
        uint address = cpu.K;
        bool right = cpu.RightInstruction;
        var request = new MemoryRequestAddress(address, false);
        int pending = memory.FindOperandBufferRegister(request);
        if (pending >= 0)
        {
            // The historical hosted toolchain generates commands through ordinary
            // stores. Publish that command using the existing CTX/host-loader API.
            memory.StoreCommand(address, memory.ReadOperandBufferRegister(pending).Data);
        }
        else
        {
            uint physical = memory.Assignment.TranslateRequest(request);
            var word = memory.PhysicalMemory.ReadRaw(physical);
            if (!word.HasValidInstructionControl(right) && word.HasValidOperandControl)
                memory.StoreCommand(address, word.Data);
        }
        ulong payload = machine.Memory.Read(address).Value;
        var instruction = InstructionCodec.DecodeHalf((uint)(right ? payload : payload >> 24) & 0xFFFFFF);
        LastOpcode = instruction.Opcode;
        bool stopped;
        try { stopped = machine.Step(ref completed); }
        catch (SupervisorHaltException error) when (cpu.InterceptCount > 0 && error.Signal is (7UL << 20) or (3UL << 20))
        {
            // Preserve the hosted runtime's explicit arithmetic-intercept contract;
            // memory/protection/illegal-instruction stops are never converted.
            throw new Processor.Cpu.ProcessorException(error.Signal == (7UL << 20) ? "Division by zero" : "Arithmetic overflow");
        }
        uint opcode = (uint)LastOpcode;
        if (cpu.LastStepCompleted && (opcode is >= 0x28 and <= 0x3F or 0x80 or 0x88))
        {
            var supervisor = cpu.Supervisor!;
            if (supervisor.Mode != SupervisorMode.Extracode)
                throw new InvalidOperationException("The supervisor CPU did not enter its extracode context.");
            var call = new ExtracodeCall((Extracode)opcode, cpu.GetM(14), instruction.Register, instruction.Address, right);
            supervisor.ReturnFromInterrupt(2);
            if (!services.Handle(call)) throw new Processor.Cpu.ProcessorException($"Extracode {opcode} not implemented");
            cpu.R = (cpu.R & ~(uint)RFlags.Mode) | (uint)RFlags.Log;
        }
        return stopped;
    }
}
