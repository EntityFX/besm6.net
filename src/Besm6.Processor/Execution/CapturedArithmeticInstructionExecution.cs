using Besm6.Processor.Cpu;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Arithmetic;

namespace Besm6.Processor.Execution;

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
    public void FinishLogical(IMutableProcessorState state) => IsLogical = true;
    public void Finish(ref ExecutionFrame frame, IMutableProcessorState state, bool additive) => Additive = additive;
}
