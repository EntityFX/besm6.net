using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;
using Besm6.Core;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Cpu;

namespace Besm6.Processor.Execution;

/// <summary>Explicit issue, operand transfer, publication and retirement of a command lease.</summary>
internal interface IPreparedProcessorExecution : IReadOnlyRegisters, IExecutionGuard
{
    ISupervisorControl? Supervisor { get; }
    bool LastStepCompleted { get; }
    bool HasPreparedInstruction { get; }
    PreparedInstruction PrepareInstruction();
    PreparedInstruction BeginInstructionFetch();
    PreparedInstruction AcceptInstructionFetch(in PreparedInstruction lease, Word48 word, Exception? failure);
    uint GetPreparedMemoryOperandAddress(in PreparedInstruction instruction);
    PreparedArithmeticOperation? CaptureTransferredArithmeticOperand(in PreparedInstruction instruction, uint address, Word48 word, Exception? failure);
    bool IsPreparedInstructionSuppressed(in PreparedInstruction instruction);
    bool CanBeginMemoryInstruction(in PreparedInstruction instruction);
    bool CanCaptureArithmeticOperand(in PreparedInstruction instruction);
    CpuMemoryTransfer? BeginMemoryInstruction(in PreparedInstruction instruction);
    CpuMemoryTransfer? AcceptMemoryTransfer(in PreparedInstruction instruction, Word48 word, Exception? failure);
    bool RequiresMemoryTransfer(in PreparedInstruction instruction);
    PreparedArithmeticOperation? CaptureArithmeticOperand(in PreparedInstruction instruction);
    bool RequiresArithmeticResult(in PreparedInstruction instruction);
    byte GetPreparedImmediateOperand(in PreparedInstruction instruction);
    void SupplyArithmeticResult(in PreparedInstruction instruction, NormalizedArithmeticResult? result, ProcessorException? failure);
    bool IsPreparedInstructionActive(in PreparedInstruction instruction);
    bool CompleteInstruction(in PreparedInstruction instruction);
    void CancelInstruction(in PreparedInstruction instruction);
}
