namespace Besm6.Runtime.Timing;

internal enum ArithmeticOperandBufferKind : byte
{
    WriteResults = 0, // БРЗ — буфер результатов записи.
    ReadNumbers = 8, // БРЧ — буфер чисел, разряд4.
    SpecialRegisters = 16,
    ConsoleDevices = 32 // Пультовые внешние устройства, разряд6.
}

/// <summary>Explicit communication-buffer slot (БРУС); allocation belongs to UU/memory control.</summary>
internal readonly record struct ArithmeticOperandBuffer
{
    internal ArithmeticOperandBufferKind Kind { get; }
    internal byte Number { get; }
    internal uint Code => (uint)Kind | Number;
    internal ArithmeticOperandBuffer(ArithmeticOperandBufferKind kind, byte number)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (number > 7) throw new ArgumentOutOfRangeException(nameof(number));
        Kind = kind; Number = number;
    }
}

/// <summary>
/// УУ — устройство управления. TO-3 table1.1, sheets4/5, gives program and AU
/// codes; §3.8, sheets38/39, gives the interface fields. Only the sixteen commands
/// supported by the shared staged arithmetic handler are admitted here.
/// This is command formation, not another instruction or arithmetic interpreter.
/// </summary>
internal static class ArithmeticCommandEncoding
{
    internal static byte OperationCode(Opcode opcode) => opcode switch
    {
        Opcode.APlusX => 0x04, // 004 octal.
        Opcode.AMinusX => 0x05,
        Opcode.XMinusA => 0x06,
        Opcode.Amx => 0x07,
        Opcode.Aax => 0x09, // 011 octal, logical multiplication.
        Opcode.Aex => 0x0A, // 012 octal, comparison.
        Opcode.Aox => 0x0D, // 015 octal, logical addition.
        Opcode.Avx => 0x0C, // 014 octal.
        Opcode.ADivX => 0x0E,
        Opcode.AMulX => 0x0F,
        Opcode.Apx => 0x10, // 020 octal, packing.
        Opcode.Aux => 0x11, // 021 octal, unpacking.
        Opcode.EPlusX => 0x14, // 024 octal.
        Opcode.EMinusX => 0x15,
        Opcode.EPlusN => 0x1C, // 034 octal, order correction by address.
        Opcode.EMinusN => 0x1D,
        _ => throw new ArgumentException("This guest opcode has no supported staged AU encoding.", nameof(opcode))
    };

    internal static bool IsImmediate(Opcode opcode) => opcode is Opcode.EPlusN or Opcode.EMinusN;

    internal static ArithmeticCommandWord Encode(Opcode opcode, byte immediateOperand,
        ArithmeticOperandBuffer? buffer)
    {
        uint code = (uint)OperationCode(opcode) << 9;
        if (IsImmediate(opcode))
        {
            if (buffer.HasValue || immediateOperand > 127)
                throw new ArgumentException("Immediate AU formation needs seven operand bits and no buffer.");
            return new(code | 0x100u | immediateOperand);
        }
        if (buffer is not { } slot || immediateOperand != 0)
            throw new ArgumentException("Memory arithmetic needs an assigned BRUS slot and no immediate operand.");
        return new(code | slot.Code);
    }
}
