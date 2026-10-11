namespace Besm6.Core
{
    /// <summary>Исполняет длинные команды управления 0220–0370.</summary>
    internal sealed class ControlInstructionExecutor
    {
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;
        private readonly MemoryInstructionExecutor _data;

        internal ControlInstructionExecutor(ProcessorState state, ProcessorMemoryAccess memory, MemoryInstructionExecutor data)
        {
            _state = state;
            _memory = memory; _data = data;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal InstructionOutcome Execute(ref ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint addr = frame.Address;
            uint[] m = _state.M;
            frame.RegistersInState = true;
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Utc:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.NextC = frame.EffectiveAddress;
                    return InstructionOutcome.Continue;
                case Opcode.Vtm:
                    SetEffectiveAddress(ref frame, addr);
                    m[reg] = addr;
                    m[0] = 0;
                    return InstructionOutcome.Continue;
                case Opcode.Utm:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    m[reg] = frame.EffectiveAddress;
                    m[0] = 0;
                    return InstructionOutcome.Continue;
                case Opcode.Uj:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    Branch(frame.EffectiveAddress);
                    return InstructionOutcome.Continue;
                case Opcode.Vlm:
                    SetEffectiveAddress(ref frame, addr);
                    if (m[reg] != 0)
                    {
                        m[reg] = Addr(m[reg] + 1);
                        Branch(addr);
                    }
                    return InstructionOutcome.Continue;
                case Opcode.Stop:
                    frame.UpdateModificationRegister = false;
                    return InstructionOutcome.Stop;
                default:
                    return ExecuteOther(ref frame);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        private InstructionOutcome ExecuteOther(ref ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint addr = frame.Address;
            uint[] m = _state.M;
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Wtc:
                    return _data.ExecuteWtc(ref frame);
                case Opcode.Uza:
                    frame.RegistersInState = false;
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
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
                    frame.RegistersInState = false;
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
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
                case Opcode.Vjm:
                    SetEffectiveAddress(ref frame, addr);
                    m[reg] = Addr(_state.K + (_state.IsRightHalf ? 1u : 0u));
                    m[0] = 0;
                    Branch(addr);
                    break;
                case Opcode.Ij:
                    throw new ProcessorException("Illegal instruction 320 выпр/iret");
                case Opcode.Vzm:
                    SetEffectiveAddress(ref frame, addr);
                    if (m[reg] == 0) Branch(addr);
                    break;
                case Opcode.V1m:
                    SetEffectiveAddress(ref frame, addr);
                    if (m[reg] != 0) Branch(addr);
                    break;
                case Opcode.Op36:
                    SetEffectiveAddress(ref frame, addr);
                    if (m[reg] == 0) Branch(addr);
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

        private void SetEffectiveAddress(ref ExecutionFrame frame, uint value)
        {
            frame.EffectiveAddress = value;
            _state.EffectiveAddress = value;
        }

        private static uint Addr(uint value) => ArchitectureConstants.NormalizeAddress(value);
    }
}
