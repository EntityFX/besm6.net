using Besm6.Architecture.Numerics;

namespace Besm6.Architecture.Memory;

/// <summary>
/// Hosted CTX command publication in the CPU address domain. Resolves the current
/// mapping, publishes pending aliases, sets instruction control and reconciles
/// the next fetch. Distinct from physical host loading and operand stores.
/// </summary>
public interface ICommandMemory
{
    void StoreCommand(uint address, Word48 word);
}
