using Besm6.Architecture.Machine;
using Besm6.Architecture.Isa;
using Besm6.Processor.Execution;
using System.Runtime.CompilerServices;

namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private readonly struct CountedBlockAccounting(IProcessorInstructionAccounting session) : IBlockAccounting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin(Opcode opcode) => session.Begin(opcode);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() => session.Complete();
        public bool AtCycleLimit => session.AtCycleLimit;
    }
}
