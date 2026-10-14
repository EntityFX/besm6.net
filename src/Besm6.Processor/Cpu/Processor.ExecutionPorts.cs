using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Core;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Execution;

namespace Besm6.Processor.Cpu
{
    public partial class Processor : IFunctionalProcessorExecution, IPreparedProcessorExecution, IInstructionHost
    {
        internal IFunctionalProcessorExecution FunctionalExecution => this;
        internal IPreparedProcessorExecution PreparedExecution => this;

        bool IExecutionGuard.ExecutionProhibited
        { get => ExecutionProhibited; set => ExecutionProhibited = value; }
        bool IExecutionGuard.PreparedExecutionOnly
        { get => PreparedExecutionOnly; set => PreparedExecutionOnly = value; }
        bool IInstructionHost.ExecutionProhibited => ExecutionProhibited;
        bool IInstructionHost.LastStepCompleted
        { get => LastStepCompleted; set => LastStepCompleted = value; }
        void IInstructionHost.CanonPre(ulong word, uint rk, DecodedInstruction instruction) => CanonPre(word, rk, instruction);
        void IInstructionHost.CanonPost(uint k, bool right) => CanonPost(k, right);
        void IInstructionHost.CancelInstructionTrace() => CancelInstructionTrace();
        bool IInstructionHost.DebugCheckFetch(uint address, uint opcode) => DebugCheckFetch(address, opcode);

        bool IFunctionalProcessorExecution.HasPreparedInstruction => HasPreparedInstruction;
        bool IFunctionalProcessorExecution.CanExecuteUnobservedBlock => CanExecuteUnobservedBlock;
        bool IFunctionalProcessorExecution.ExecuteUnobservedBlock(int count, ref long completed, ref ulong tick) =>
            ExecuteUnobservedBlock(count, ref completed, ref tick);
        bool IPreparedProcessorExecution.HasPreparedInstruction => HasPreparedInstruction;
        PreparedInstruction IPreparedProcessorExecution.PrepareInstruction() =>
            PrepareInstruction();
        PreparedInstruction IPreparedProcessorExecution.BeginInstructionFetch() =>
            BeginInstructionFetch();
        PreparedInstruction IPreparedProcessorExecution.AcceptInstructionFetch(in PreparedInstruction lease, Word48 word, Exception? failure) =>
            AcceptInstructionFetch(in lease, word, failure);
        uint IPreparedProcessorExecution.GetPreparedMemoryOperandAddress(in PreparedInstruction instruction) =>
            GetPreparedMemoryOperandAddress(in instruction);
        PreparedArithmeticOperation? IPreparedProcessorExecution.CaptureTransferredArithmeticOperand(in PreparedInstruction instruction, uint address, Word48 word, Exception? failure) =>
            CaptureTransferredArithmeticOperand(in instruction, address, word, failure);
        bool IPreparedProcessorExecution.IsPreparedInstructionSuppressed(in PreparedInstruction instruction) =>
            IsPreparedInstructionSuppressed(in instruction);
        bool IPreparedProcessorExecution.CanBeginMemoryInstruction(in PreparedInstruction instruction) =>
            CanBeginMemoryInstruction(in instruction);
        bool IPreparedProcessorExecution.CanCaptureArithmeticOperand(in PreparedInstruction instruction) =>
            CanCaptureArithmeticOperand(in instruction);
        CpuMemoryTransfer? IPreparedProcessorExecution.BeginMemoryInstruction(in PreparedInstruction instruction) =>
            BeginMemoryInstruction(in instruction);
        CpuMemoryTransfer? IPreparedProcessorExecution.AcceptMemoryTransfer(in PreparedInstruction instruction, Word48 word, Exception? failure) =>
            AcceptMemoryTransfer(in instruction, word, failure);
        bool IPreparedProcessorExecution.RequiresMemoryTransfer(in PreparedInstruction instruction) =>
            RequiresMemoryTransfer(in instruction);
        PreparedArithmeticOperation? IPreparedProcessorExecution.CaptureArithmeticOperand(in PreparedInstruction instruction) =>
            CaptureArithmeticOperand(in instruction);
        bool IPreparedProcessorExecution.RequiresArithmeticResult(in PreparedInstruction instruction) =>
            RequiresArithmeticResult(in instruction);
        byte IPreparedProcessorExecution.GetPreparedImmediateOperand(in PreparedInstruction instruction) =>
            GetPreparedImmediateOperand(in instruction);
        void IPreparedProcessorExecution.SupplyArithmeticResult(in PreparedInstruction instruction, NormalizedArithmeticResult? result, ProcessorException? failure) =>
            SupplyArithmeticResult(in instruction, result, failure);
        bool IPreparedProcessorExecution.IsPreparedInstructionActive(in PreparedInstruction instruction) =>
            IsPreparedInstructionActive(in instruction);
        bool IPreparedProcessorExecution.CompleteInstruction(in PreparedInstruction instruction) =>
            CompleteInstruction(in instruction);
        void IPreparedProcessorExecution.CancelInstruction(in PreparedInstruction instruction) =>
            CancelInstruction(in instruction);
    }
}
