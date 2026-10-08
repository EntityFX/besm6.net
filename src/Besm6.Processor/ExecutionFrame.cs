namespace Besm6.Core
{
    /// <summary>Локальное изменяемое состояние исполнения одного полуслова.</summary>
    internal struct ExecutionFrame
    {
        public ExecutionFrame() { }

        internal required DecodedInstruction Instruction { get; init; }
        internal required uint Address { get; set; }
        internal required uint EffectiveAddress { get; set; }
        internal required ulong A { get; set; }
        internal required ulong Y { get; set; }
        internal uint NextC { get; set; }
        // ALU results already reside in ProcessorState; several control instructions
        // also leave A/Y untouched. Neither needs a frame-to-state round trip.
        internal bool RegistersInState { get; set; }
        internal bool UpdateModificationRegister { get; set; } = true;
    }
}
