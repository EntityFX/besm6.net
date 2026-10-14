using Besm6.Architecture.Isa;

namespace Besm6.Core;

internal interface IBlockAccounting
{
    void Begin(Opcode opcode);
    void Complete();
    bool AtCycleLimit { get; }
}