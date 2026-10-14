namespace Besm6.Architecture.Memory;

/// <summary>
/// CPU operand accesses. The CPU supplies normalized fifteen-bit addresses;
/// control, protection, translation and buffer effects belong to the selected
/// backend. Host loading must use the backend's separate host view.
/// </summary>
public interface IOperandMemory : IMemory
{
}
