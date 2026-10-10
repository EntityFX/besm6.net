namespace Besm6.Runtime.Timing;

internal enum ArithmeticCommandSource { BufferRead, Immediate, BufferWrite, IndexRegister, ExternalDevice }
internal enum ArithmeticOperandRoute { Buffered, Direct }

/// <summary>
/// БАК — буфер арифметических команд. TO-3 §3.8, sheets38–40:
/// decoding of the 17-bit interface word, not of a guest instruction.
/// Buffer-class flags remain separate: no priority is invented for mixed flags.
/// OperationCode is the physical six-bit field; its mapping from guest opcodes
/// is not inferred here. Bit numbers in the source are one-based.
/// </summary>
internal readonly record struct ArithmeticCommandWord
{
    internal uint Raw { get; }
    internal ArithmeticCommandSource Source { get; }
    internal byte OperationCode => (byte)((Raw >> 9) & 0x3F);
    internal ushort IndexOperand => (ushort)(Raw & 0x7FFF);
    internal byte ImmediateOperand => (byte)(Raw & 0x7F);
    internal byte BufferNumber => (byte)(Raw & 7);
    internal byte BufferClassFlags => (byte)((Raw >> 3) & 7);
    internal bool WritesControlRegisters => Source == ArithmeticCommandSource.BufferWrite && (Raw & 0x40) != 0;
    internal byte ControlFlags => (byte)(Raw & 7);
    internal bool ExternalRead => Source == ArithmeticCommandSource.ExternalDevice && (Raw & 0x800) != 0;
    internal bool SuppressesExternalControl => Source == ArithmeticCommandSource.ExternalDevice && (Raw & 0x8000) != 0;
    internal ushort ExternalAddress => (ushort)(Raw & 0x7FF);

    internal ArithmeticCommandWord(uint raw)
    {
        if ((raw & ~ArithmeticCommandBuffer.CommandMask) != 0) throw new ArgumentOutOfRangeException(nameof(raw));
        Raw = raw;
        Source = (raw & 0x10000) != 0 ? ArithmeticCommandSource.ExternalDevice :
            (raw & 0x8000) != 0 ? ArithmeticCommandSource.IndexRegister :
            (raw & 0x100) != 0 ? ArithmeticCommandSource.Immediate :
            (raw & 0x80) != 0 ? ArithmeticCommandSource.BufferWrite : ArithmeticCommandSource.BufferRead;
    }
}
