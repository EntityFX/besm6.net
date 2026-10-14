using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Memory;

/// <summary>
/// Optional CPU memory capability: fetch a selected command half independently
/// from IMemory operand reads/writes. Ordinary IMemory implementations remain valid.
/// </summary>
public interface IInstructionMemory : IMemory
{
    Word48 FetchInstruction(uint address, bool rightHalf);
}
