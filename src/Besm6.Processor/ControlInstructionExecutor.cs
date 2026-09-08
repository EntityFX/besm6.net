namespace Besm6.Core
{
    /// <summary>Исполняет длинные команды управления 0220–0370.</summary>
    internal sealed class ControlInstructionExecutor
    {
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;

        internal ControlInstructionExecutor(ProcessorState state, ProcessorMemoryAccess memory)
        {
            _state = state;
            _memory = memory;
        }

        internal InstructionOutcome Execute(ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint addr = frame.Address;
            uint[] m = _state.M;
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Utc:
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    frame.NextC = frame.EffectiveAddress;
                    break;
                case Opcode.Wtc:
                    if (addr == 0 && reg == 15)
                    {
                        m[15] = Addr(m[15] - 1);
                        _state.StackCorrection = 1;
                    }
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    frame.NextC = Addr((uint)_memory.MemLoad(frame.EffectiveAddress));
                    break;
                case Opcode.Vtm:
                    SetEffectiveAddress(frame, addr);
                    m[reg] = addr;
                    m[0] = 0;
                    break;
                case Opcode.Utm:
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    m[reg] = frame.EffectiveAddress;
                    m[0] = 0;
                    break;
                case Opcode.Uza:
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    frame.Y = frame.A;
                    if (_state.IsAdditive)
                    {
                        if ((frame.A & ArchitectureConstants.BIT41) != 0) break;
                    }
                    else if (_state.IsMultiplicative)
                    {
                        if ((frame.A & ArchitectureConstants.BIT48) == 0) break;
                    }
                    else if (_state.IsLogical)
                    {
                        if (frame.A != 0) break;
                    }
                    else break;
                    Branch(frame.EffectiveAddress);
                    break;
                case Opcode.U1a:
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    frame.Y = frame.A;
                    if (_state.IsAdditive)
                    {
                        if ((frame.A & ArchitectureConstants.BIT41) == 0) break;
                    }
                    else if (_state.IsMultiplicative)
                    {
                        if ((frame.A & ArchitectureConstants.BIT48) != 0) break;
                    }
                    else if (_state.IsLogical)
                    {
                        if (frame.A == 0) break;
                    }
                    Branch(frame.EffectiveAddress);
                    break;
                case Opcode.Uj:
                    SetEffectiveAddress(frame, Addr(addr + m[reg]));
                    Branch(frame.EffectiveAddress);
                    break;
                case Opcode.Vjm:
                    SetEffectiveAddress(frame, addr);
                    m[reg] = frame.NextK;
                    m[0] = 0;
                    Branch(addr);
                    break;
                case Opcode.Ij:
                    throw new ProcessorException("Illegal instruction 320 выпр/iret");
                case Opcode.Stop:
                    frame.UpdateModificationRegister = false;
                    return InstructionOutcome.Stop;
                case Opcode.Vzm:
                    SetEffectiveAddress(frame, addr);
                    if (m[reg] == 0) Branch(addr);
                    break;
                case Opcode.V1m:
                    SetEffectiveAddress(frame, addr);
                    if (m[reg] != 0) Branch(addr);
                    break;
                case Opcode.Op36:
                    SetEffectiveAddress(frame, addr);
                    if (m[reg] == 0) Branch(addr);
                    break;
                case Opcode.Vlm:
                    SetEffectiveAddress(frame, addr);
                    if (m[reg] == 0) break;
                    m[reg] = Addr(m[reg] + 1);
                    Branch(addr);
                    break;
                default:
                    throw new InvalidOperationException($"Opcode {frame.Instruction.Opcode} is not a control instruction.");
            }
            return InstructionOutcome.Continue;
        }

        private void Branch(uint address)
        {
            _state.K = address;
            _state.IsRightHalf = false;
        }

        private void SetEffectiveAddress(ExecutionFrame frame, uint value)
        {
            frame.EffectiveAddress = value;
            _state.EffectiveAddress = value;
        }

        private static uint Addr(uint value) => ArchitectureConstants.NormalizeAddress(value);
    }
}
