using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Cpu;

namespace Besm6.Processor.Execution;

internal readonly struct ImmediateArithmeticInstructionExecution(IAlu alu) : IArithmeticInstructionExecution
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
    public void FinishLogical(IMutableProcessorState state) => state.SetLogical();
    public void Finish(ref ExecutionFrame frame, IMutableProcessorState state, bool additive)
    {
        frame.RegistersInState = true;
        if (additive) state.SetAdditive(); else state.SetMultiplicative();
    }
}
