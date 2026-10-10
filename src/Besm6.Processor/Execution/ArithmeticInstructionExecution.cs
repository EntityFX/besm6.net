namespace Besm6.Core;

// АУ — арифметическое устройство. The opcode/addressing handler is shared;
// its arithmetic receiver either executes immediately or captures a PVR input.
internal interface IArithmeticInstructionExecution
{
    void Add(Word48 value, bool negateA, bool negateValue);
    void Multiply(Word48 value);
    void Divide(Word48 value);
    void ChangeSign(bool negate);
    void AddExponent(int delta);
    void Logical(ref ExecutionFrame frame, Word48 value, LogicalArithmeticOperation operation);
    void FinishLogical(ProcessorState state);
    void Finish(ref ExecutionFrame frame, ProcessorState state, bool additive);
}

internal readonly struct ImmediateArithmeticInstructionExecution(Alu alu) : IArithmeticInstructionExecution
{
    public void Add(Word48 value, bool negateA, bool negateValue) => alu.Add(value, negateA, negateValue);
    public void Multiply(Word48 value) => alu.Multiply(value);
    public void Divide(Word48 value) => alu.Divide(value);
    public void ChangeSign(bool negate) => alu.ChangeSign(negate);
    public void AddExponent(int delta) => alu.AddExponent(delta);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Logical(ref ExecutionFrame frame, Word48 value, LogicalArithmeticOperation operation)
    {
        var result = LogicalArithmeticOperations.Evaluate(new(frame.A), value, operation);
        frame.A = result.A.Value;
        frame.Y = result.Y.Value;
    }
    public void FinishLogical(ProcessorState state) => state.SetLogical();
    public void Finish(ref ExecutionFrame frame, ProcessorState state, bool additive)
    {
        frame.RegistersInState = true;
        if (additive) state.SetAdditive(); else state.SetMultiplicative();
    }
}

internal struct CapturedArithmeticInstructionExecution(uint mode) : IArithmeticInstructionExecution
{
    internal PreparedArithmeticOperation Operation;
    internal bool Additive;
    internal bool IsLogical;
    public void Add(Word48 value, bool negateA, bool negateValue) =>
        Operation = PreparedArithmeticOperation.Add(value, mode, negateA, negateValue);
    public void Multiply(Word48 value) => Operation = PreparedArithmeticOperation.Multiply(value, mode);
    public void Divide(Word48 value) => Operation = PreparedArithmeticOperation.Divide(value, mode);
    public void ChangeSign(bool negate) => Operation = PreparedArithmeticOperation.ChangeSign(negate, mode);
    public void AddExponent(int delta) => Operation = PreparedArithmeticOperation.AddExponent(delta, mode);
    public void Logical(ref ExecutionFrame frame, Word48 value, LogicalArithmeticOperation operation) =>
        Operation = PreparedArithmeticOperation.Logical(value, operation);
    public void FinishLogical(ProcessorState state) => IsLogical = true;
    public void Finish(ref ExecutionFrame frame, ProcessorState state, bool additive) => Additive = additive;
}
