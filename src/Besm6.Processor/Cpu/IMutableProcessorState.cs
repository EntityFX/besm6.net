using Besm6.Processor.Diagnostics;
using Besm6.Architecture.Machine;

namespace Besm6.Processor.Cpu;

/// <summary>State transitions available only to the processor implementation.</summary>
internal interface IMutableProcessorState : IProcessorState, IRegisters
{
    new uint[] M { get; }
    new int InterceptCount { get; set; }
    new uint InterceptAddress { get; set; }
    new bool IsRightHalf { get; set; }
    int StackCorrection { get; set; }
    uint RawInstruction { get; set; }
    bool IsAdditive { get; }
    bool IsMultiplicative { get; }
    bool IsLogical { get; }
    DebugState DebugState { get; }
    uint PreviousDebugAbort { get; set; }
    void SetAdditive();
    void SetMultiplicative();
    void SetLogical();
    void Reset();
}
