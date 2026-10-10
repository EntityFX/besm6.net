namespace Besm6.Core;

/// <summary>
/// Immutable input accepted by the arithmetic unit (АУ — арифметическое устройство).
/// Operation selection is supplied by the command decoder; this is not a mapping
/// of the physical 17-bit arithmetic command register. Evaluation uses the same
/// algorithms as Alu and does not publish registers or assign hardware durations.
/// </summary>
internal readonly struct PreparedArithmeticOperation
{
    private enum Operation { Add, Multiply, Divide, AddExponent, ChangeSign }
    private readonly Operation _operation;
    private readonly Word48 _operand;
    private readonly uint _mode;
    private readonly bool _negateAccumulator, _negateOperand;
    private readonly int _exponentDelta;

    private PreparedArithmeticOperation(Operation operation, Word48 operand, uint mode,
        bool negateAccumulator = false, bool negateOperand = false, int exponentDelta = 0)
    {
        _operation = operation;
        _operand = operand;
        _mode = mode;
        _negateAccumulator = negateAccumulator;
        _negateOperand = negateOperand;
        _exponentDelta = exponentDelta;
    }

    internal bool IsDivision => _operation == Operation.Divide;

    internal static PreparedArithmeticOperation Add(Word48 operand, uint mode,
        bool negateAccumulator = false, bool negateOperand = false) =>
        new(Operation.Add, operand, mode, negateAccumulator, negateOperand);

    internal static PreparedArithmeticOperation Multiply(Word48 operand, uint mode) =>
        new(Operation.Multiply, operand, mode);

    internal static PreparedArithmeticOperation Divide(Word48 operand, uint mode) =>
        new(Operation.Divide, operand, mode);

    internal static PreparedArithmeticOperation AddExponent(int delta, uint mode) =>
        new(Operation.AddExponent, Word48.Zero, mode, exponentDelta: delta);

    internal static PreparedArithmeticOperation ChangeSign(bool negateAccumulator, uint mode) =>
        new(Operation.ChangeSign, Word48.Zero, mode, negateAccumulator);

    internal NormalizedArithmeticResult Evaluate(Word48 accumulator, Word48 lowRegister) =>
        _operation switch
        {
            Operation.Add => AdditiveOperations.EvaluateAdd(accumulator, lowRegister, _mode,
                _operand, _negateAccumulator, _negateOperand),
            Operation.Multiply => MultiplicativeOperations.EvaluateMultiply(accumulator, lowRegister,
                _mode, _operand),
            Operation.Divide => MultiplicativeOperations.EvaluateDivide(accumulator, lowRegister,
                _mode, _operand),
            Operation.AddExponent => AdditiveOperations.EvaluateAddExponent(accumulator, _mode, _exponentDelta),
            Operation.ChangeSign => AdditiveOperations.EvaluateChangeSign(accumulator, _mode, _negateAccumulator),
            _ => throw new InvalidOperationException("Unknown prepared arithmetic operation.")
        };
}
