namespace Besm6.Core
{
    /// <summary>Исполняет индексные команды 040–047.</summary>
    internal sealed class IndexInstructionExecutor
    {
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;

        internal IndexInstructionExecutor(ProcessorState state, ProcessorMemoryAccess memory)
        {
            _state = state;
            _memory = memory;
        }

        internal InstructionOutcome Execute(ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint[] m = _state.M;
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Ati:
                    SetEffectiveAddress(frame, Addr(frame.Address + m[reg]));
                    m[frame.EffectiveAddress & 0xFu] = Addr((uint)frame.A);
                    m[0] = 0;
                    break;
                case Opcode.Sti:
                    SetEffectiveAddress(frame, Addr(frame.Address + m[reg]));
                    uint targetRegister = frame.EffectiveAddress & 0xFu;
                    uint accumulatorAddress = Addr((uint)frame.A);
                    if (targetRegister != 15)
                    {
                        m[15] = Addr(m[15] - 1);
                        _state.StackCorrection = 1;
                    }
                    frame.A = _memory.MemLoad(targetRegister != 15 ? m[15] : accumulatorAddress);
                    m[targetRegister] = accumulatorAddress;
                    m[0] = 0;
                    _state.SetLogical();
                    break;
                case Opcode.Ita:
                    SetEffectiveAddress(frame, Addr(frame.Address + m[reg]));
                    frame.A = Addr(m[frame.EffectiveAddress & 0xFu]);
                    _state.SetLogical();
                    break;
                case Opcode.Its:
                    _memory.MemStore(m[15], frame.A);
                    m[15] = Addr(m[15] + 1);
                    SetEffectiveAddress(frame, Addr(frame.Address + m[reg]));
                    frame.A = Addr(m[frame.EffectiveAddress & 0xFu]);
                    _state.SetLogical();
                    break;
                case Opcode.Mtj:
                    SetEffectiveAddress(frame, frame.Address);
                    m[frame.EffectiveAddress & 0xFu] = m[reg];
                    m[0] = 0;
                    break;
                case Opcode.JPlusM:
                    SetEffectiveAddress(frame, frame.Address);
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

        private void SetEffectiveAddress(ExecutionFrame frame, uint value)
        {
            frame.EffectiveAddress = value;
            _state.EffectiveAddress = value;
        }

        private static uint Addr(uint value) => ArchitectureConstants.NormalizeAddress(value);
    }
}
