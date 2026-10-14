using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Cpu;

namespace Besm6.Processor.Execution;

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
    void FinishLogical(IMutableProcessorState state);
    void Finish(ref ExecutionFrame frame, IMutableProcessorState state, bool additive);
}
