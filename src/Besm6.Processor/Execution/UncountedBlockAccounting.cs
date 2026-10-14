using Besm6.Architecture.Isa;
using System.Runtime.CompilerServices;

namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private readonly struct UncountedBlockAccounting : IBlockAccounting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin(Opcode opcode) { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() { }
        public bool AtCycleLimit => false;
    }
}
