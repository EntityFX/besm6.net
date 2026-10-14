using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Supervisor;

namespace Besm6.Processor.Execution;

/// <summary>CPU callbacks used by the shared instruction interpreter.</summary>
internal interface IInstructionHost
{
    ISupervisorControl? Supervisor { get; }
    bool ExecutionProhibited { get; }
    bool LastStepCompleted { get; set; }
    Action<uint, bool, uint, uint>? TraceInstruction { get; }
    IProcessorInstructionAccounting? InstructionAccounting { get; }
    void CanonPre(ulong word, uint rk, DecodedInstruction instruction);
    void CanonPost(uint k, bool right);
    void CancelInstructionTrace();
    bool DebugCheckFetch(uint address, uint opcode);
}
