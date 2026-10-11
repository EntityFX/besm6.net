namespace Besm6.Core
{
    /// <summary>Исполняет индексные команды 040–047.</summary>
    internal sealed class IndexInstructionExecutor
    {
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;
        private readonly MemoryInstructionExecutor _data;

        internal IndexInstructionExecutor(ProcessorState state, ProcessorMemoryAccess memory, MemoryInstructionExecutor data)
        {
            _state = state;
            _memory = memory; _data = data;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal InstructionOutcome Execute(ref ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint[] m = _state.M;
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Ati:
                    frame.RegistersInState = true;
                    SetEffectiveAddress(ref frame, Addr(frame.Address + m[reg]));
                    m[frame.EffectiveAddress & 0xFu] = Addr((uint)frame.A);
                    m[0] = 0;
                    break;
                case Opcode.Sti:
                    return _data.ExecuteSti(ref frame);
                case Opcode.Ita:
                    SetEffectiveAddress(ref frame, Addr(frame.Address + m[reg]));
                    frame.A = Addr(m[frame.EffectiveAddress & 0xFu]);
                    _state.SetLogical();
                    break;
                case Opcode.Its:
                    return _data.ExecuteIts(ref frame);
                case Opcode.Mtj:
                    frame.RegistersInState = true;
                    SetEffectiveAddress(ref frame, frame.Address);
                    m[frame.EffectiveAddress & 0xFu] = m[reg];
                    m[0] = 0;
                    break;
                case Opcode.JPlusM:
                    frame.RegistersInState = true;
                    SetEffectiveAddress(ref frame, frame.Address);
                    uint index = frame.EffectiveAddress & 0xFu;
                    m[index] = Addr(m[index] + m[reg]);
                    m[0] = 0;
                    break;
                case Opcode.Op46:
                    throw new ProcessorException("Illegal instruction 046 соп");
                case Opcode.Op47:
                    throw new ProcessorException("Illegal instruction 047");
                default:
                    throw new InvalidOperationException($"Opcode {frame.Instruction.Opcode} is not an index instruction.");
            }
            return InstructionOutcome.Continue;
        }

        private void SetEffectiveAddress(ref ExecutionFrame frame, uint value)
        {
            frame.EffectiveAddress = value;
            _state.EffectiveAddress = value;
        }

        private static uint Addr(uint value) => ArchitectureConstants.NormalizeAddress(value);
    }
}
