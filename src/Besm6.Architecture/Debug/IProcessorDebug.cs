using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;

namespace Besm6.Architecture.Debug;

/// <summary>Host diagnostic subscriptions and explicit watchpoint configuration.</summary>
public interface IProcessorDebug
{
    Action<RegisterTraceRecord>? RegisterTrace { get; set; }
    Action<uint, bool, uint, uint>? TraceInstruction { get; set; }
    Action<InstructionTraceRecord>? InstructionTrace { get; set; }
    Action<Opcode>? InstructionExecuted { get; set; }
    void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont);
}
