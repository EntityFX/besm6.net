namespace Besm6.Core;

/// <summary>
/// Optional CPU memory capability: fetch a selected command half independently
/// from IMemory operand reads/writes. Ordinary IMemory implementations remain valid.
/// </summary>
public interface IInstructionMemory : IMemory
{
    Word48 FetchInstruction(uint address, bool rightHalf);
}
