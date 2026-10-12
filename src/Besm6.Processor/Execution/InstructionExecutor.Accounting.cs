using System.Runtime.CompilerServices;

namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    private interface IBlockAccounting
    {
        void Begin(Opcode opcode);
        void Complete();
        bool AtCycleLimit { get; }
    }

    private readonly struct UncountedBlockAccounting : IBlockAccounting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin(Opcode opcode) { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() { }
        public bool AtCycleLimit => false;
    }

    private readonly struct CountedBlockAccounting(ProcessorInstructionAccounting session) : IBlockAccounting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin(Opcode opcode) => session.Begin(opcode);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() => session.Complete();
        public bool AtCycleLimit => session.AtCycleLimit;
    }
}
