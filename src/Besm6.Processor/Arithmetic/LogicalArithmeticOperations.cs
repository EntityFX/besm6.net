using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Arithmetic;

/// <summary>
/// АУ — арифметическое устройство. Shared logical result, without register publication.
/// </summary>
internal static class LogicalArithmeticOperations
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static NormalizedArithmeticResult Evaluate(Word48 accumulator, Word48 operand,
        LogicalArithmeticOperation operation) => operation switch
    {
        LogicalArithmeticOperation.And => new(new(accumulator.Value & operand.Value), Word48.Zero, false, false),
        LogicalArithmeticOperation.Xor => new(new(accumulator.Value ^ operand.Value), accumulator, false, false),
        LogicalArithmeticOperation.Or => new(new(accumulator.Value | operand.Value), Word48.Zero, false, false),
        LogicalArithmeticOperation.Pack => new(new(ProcessorBitOperations.Pack(accumulator.Value, operand.Value)), Word48.Zero, false, false),
        LogicalArithmeticOperation.Unpack => new(new(ProcessorBitOperations.Unpack(accumulator.Value, operand.Value)), Word48.Zero, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
