namespace Besm6.Core
{
    /// <summary>Локальное изменяемое состояние исполнения одного полуслова.</summary>
    internal sealed class ExecutionFrame
    {
        internal required DecodedInstruction Instruction { get; init; }
        internal required uint RawInstruction { get; init; }
        internal required ulong RawWord { get; init; }
        internal required uint Address { get; set; }
        internal required uint EffectiveAddress { get; set; }
        internal required bool WasRightHalf { get; init; }
        internal required uint NextK { get; init; }
        internal required ulong A { get; set; }
        internal required ulong Y { get; set; }
        internal uint NextC { get; set; }
        internal bool UpdateModificationRegister { get; set; } = true;
    }
}
