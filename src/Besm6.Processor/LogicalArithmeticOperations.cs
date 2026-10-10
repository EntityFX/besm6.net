namespace Besm6.Core;

internal enum LogicalArithmeticOperation { And, Xor, Or }

// АУ — арифметическое устройство. Shared logical result, without register publication.
internal static class LogicalArithmeticOperations
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static NormalizedArithmeticResult Evaluate(Word48 accumulator, Word48 operand,
        LogicalArithmeticOperation operation) => operation switch
    {
        LogicalArithmeticOperation.And => new(new(accumulator.Value & operand.Value), Word48.Zero, false, false),
        LogicalArithmeticOperation.Xor => new(new(accumulator.Value ^ operand.Value), accumulator, false, false),
        LogicalArithmeticOperation.Or => new(new(accumulator.Value | operand.Value), Word48.Zero, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
