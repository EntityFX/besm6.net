using Besm6.Architecture.Isa;

namespace Besm6.Processor.Execution;

/// <summary>A single-use command lease bound to its issuing owner and generation.</summary>
internal readonly struct PreparedInstruction
{
    private readonly object _owner;
    private readonly ulong _generation;
    internal uint Address { get; }
    internal bool RightHalf { get; }
    internal DecodedInstruction? Instruction { get; }

    internal PreparedInstruction(object owner, ulong generation, uint address, bool rightHalf,
        DecodedInstruction? instruction)
    {
        _owner = owner;
        _generation = generation;
        Address = address;
        RightHalf = rightHalf;
        Instruction = instruction;
    }

    internal bool BelongsTo(object owner, ulong generation) =>
        ReferenceEquals(_owner, owner) && _generation == generation;
}
