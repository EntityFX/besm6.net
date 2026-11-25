using Besm6.Architecture.Machine;

namespace Besm6.Runtime;

/// <summary>Independent diagnostic snapshot of the machine register model.</summary>
public sealed record DetailedMachineState(
    string MemoryModel,
    string ExecutionModel,
    BufferRegisterView[] Buffers,
    uint[] SpecialRegisters,
    ulong? ControlFlags,
    ulong? Interrupts,
    ulong? HardwareNanoseconds,
    uint[] ArithmeticCommands,
    uint[] PageAssignment,
    uint? OperandProtection,
    string ProcessorProfile,
    string ServiceRuntime,
    MachineRegisterView[] Registers);
