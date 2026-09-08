namespace Besm6.Core
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
