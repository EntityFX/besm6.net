using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Memory;

/// <summary>
/// Optional CPU memory capability: fetch a selected command half independently
/// from operand reads/writes. Plain operand backends may use Read for fetch.
/// </summary>
public interface IInstructionMemory : IOperandMemory
{
    Word48 FetchInstruction(uint address, bool rightHalf);
}
