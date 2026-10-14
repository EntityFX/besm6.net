using Besm6.Architecture.Isa;
using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Machine
{
    /// <summary>
    /// Типизированная запись об одной реально исполненной инструкции.
    /// </summary>
    public readonly record struct InstructionTraceRecord(
        ulong Sequence,
        Word48 RawWord,
        uint RawInstruction,
        DecodedInstruction Instruction,
        ProcessorSnapshot Before,
        ProcessorSnapshot After);
}
