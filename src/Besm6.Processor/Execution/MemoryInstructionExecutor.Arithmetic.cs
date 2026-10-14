using Besm6.Processor.Execution;
using System.Runtime.CompilerServices;

namespace Besm6.Core;

internal sealed partial class MemoryInstructionExecutor
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private InstructionOutcome ExecuteAdd(ref ExecutionFrame frame)
    {
        var arithmetic = new ImmediateArithmeticInstructionExecution(_alu);
        return ExecuteAdd(ref frame, false, false, ref arithmetic);
    }

    // Both immediate execution and AU (АУ) operand capture use these transitions.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private InstructionOutcome ExecuteAdd<TExecution>(ref ExecutionFrame frame,
        bool negateAccumulator, bool negateOperand, ref TExecution arithmetic)
        where TExecution : struct, IArithmeticInstructionExecution
    {
        PrepareStack(frame.Address, frame.Instruction.Register);
        SetEffectiveAddress(ref frame, Addr(frame.Address + _state.M[frame.Instruction.Register]));
        arithmetic.Add(LoadWord(ref frame), negateAccumulator, negateOperand);
        arithmetic.Finish(ref frame, _state, true);
        return InstructionOutcome.Continue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private InstructionOutcome ExecuteMultiply(ref ExecutionFrame frame)
    {
        var arithmetic = new ImmediateArithmeticInstructionExecution(_alu);
        return ExecuteMultiply(ref frame, ref arithmetic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private InstructionOutcome ExecuteMultiply<TExecution>(ref ExecutionFrame frame, ref TExecution arithmetic)
        where TExecution : struct, IArithmeticInstructionExecution
    {
        PrepareStack(frame.Address, frame.Instruction.Register);
        SetEffectiveAddress(ref frame, Addr(frame.Address + _state.M[frame.Instruction.Register]));
        arithmetic.Multiply(LoadWord(ref frame));
        arithmetic.Finish(ref frame, _state, false);
        return InstructionOutcome.Continue;
    }
}
