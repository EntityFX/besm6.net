namespace Besm6.Core
{
    /// <summary>Исполняет короткие команды памяти и АЛУ 000–037.</summary>
    internal sealed class MemoryInstructionExecutor
    {
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;
        private readonly Alu _alu;

        internal MemoryInstructionExecutor(ProcessorState state, ProcessorMemoryAccess memory, Alu alu)
        {
            _state = state;
            _memory = memory;
            _alu = alu;
        }

        internal InstructionOutcome Execute(ref ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint addr = frame.Address;
            uint[] m = _state.M;

            switch (frame.Instruction.Opcode)
            {
                case Opcode.Atx:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _memory.MemStore(frame.EffectiveAddress, frame.A);
                    if (addr == 0 && reg == 15) m[15] = Addr(m[15] + 1);
                    break;
                case Opcode.Stx:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _memory.MemStore(frame.EffectiveAddress, frame.A);
                    m[15] = Addr(m[15] - 1);
                    _state.StackCorrection = 1;
                    frame.A = _memory.MemLoad(m[15]);
                    _state.SetLogical();
                    break;
                case Opcode.Mod:
                    throw new ProcessorException("Illegal instruction 002 рег/mod");
                case Opcode.Xts:
                    _memory.MemStore(m[15], frame.A);
                    m[15] = Addr(m[15] + 1);
                    _state.StackCorrection = -1;
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = _memory.MemLoad(frame.EffectiveAddress);
                    _state.SetLogical();
                    break;
                case Opcode.APlusX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Add(LoadWord(ref frame), false, false);
                    UseAluResult(ref frame);
                    _state.SetAdditive();
                    break;
                case Opcode.AMinusX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Add(LoadWord(ref frame), false, true);
                    UseAluResult(ref frame);
                    _state.SetAdditive();
                    break;
                case Opcode.XMinusA:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Add(LoadWord(ref frame), true, false);
                    UseAluResult(ref frame);
                    _state.SetAdditive();
                    break;
                case Opcode.Amx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Add(LoadWord(ref frame), true, true);
                    UseAluResult(ref frame);
                    _state.SetAdditive();
                    break;
                case Opcode.Xta:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = _memory.MemLoad(frame.EffectiveAddress);
                    _state.SetLogical();
                    break;
                case Opcode.Aax:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A &= _memory.MemLoad(frame.EffectiveAddress);
                    frame.Y = 0;
                    _state.SetLogical();
                    break;
                case Opcode.Aex:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.Y = frame.A;
                    frame.A ^= _memory.MemLoad(frame.EffectiveAddress);
                    _state.SetLogical();
                    break;
                case Opcode.Arx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A += _memory.MemLoad(frame.EffectiveAddress);
                    if ((frame.A & Bit49) != 0) frame.A = (frame.A + 1) & ArchitectureConstants.BITS48;
                    frame.Y = 0;
                    _state.SetMultiplicative();
                    break;
                case Opcode.Avx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.ChangeSign(((_memory.MemLoad(frame.EffectiveAddress) >> 40) & 1u) != 0);
                    UseAluResult(ref frame);
                    _state.SetAdditive();
                    break;
                case Opcode.Aox:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A |= _memory.MemLoad(frame.EffectiveAddress);
                    frame.Y = 0;
                    _state.SetLogical();
                    break;
                case Opcode.ADivX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Divide(LoadWord(ref frame));
                    UseAluResult(ref frame);
                    _state.SetMultiplicative();
                    break;
                case Opcode.AMulX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Multiply(LoadWord(ref frame));
                    UseAluResult(ref frame);
                    _state.SetMultiplicative();
                    break;
                case Opcode.Apx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = Processor.Besm6Pack(frame.A, _memory.MemLoad(frame.EffectiveAddress));
                    frame.Y = 0;
                    _state.SetLogical();
                    break;
                case Opcode.Aux:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = Processor.Besm6Unpack(frame.A, _memory.MemLoad(frame.EffectiveAddress));
                    frame.Y = 0;
                    _state.SetLogical();
                    break;
                case Opcode.Acx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = (ulong)Processor.Besm6CountOnes(frame.A) + _memory.MemLoad(frame.EffectiveAddress);
                    if ((frame.A & Bit49) != 0) frame.A = (frame.A + 1) & ArchitectureConstants.BITS48;
                    frame.Y = 0;
                    _state.SetLogical();
                    break;
                case Opcode.Anx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    if (frame.A != 0)
                    {
                        int highestBit = Processor.Besm6HighestBit(frame.A);
                        _alu.Shift(48 - highestBit);
                        frame.Y = _state.Y.Value;
                        frame.A = (ulong)highestBit + _memory.MemLoad(frame.EffectiveAddress);
                        if ((frame.A & Bit49) != 0) frame.A = (frame.A + 1) & ArchitectureConstants.BITS48;
                    }
                    else
                    {
                        frame.Y = 0;
                        frame.A = _memory.MemLoad(frame.EffectiveAddress);
                    }
                    _state.SetLogical();
                    break;
                case Opcode.EPlusX:
                    ExecuteExponentFromMemory(ref frame, 1);
                    break;
                case Opcode.EMinusX:
                    ExecuteExponentFromMemory(ref frame, -1);
                    break;
                case Opcode.Asx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Shift((int)(_memory.MemLoad(frame.EffectiveAddress) >> 41) - 64);
                    UseAluResult(ref frame);
                    _state.SetLogical();
                    break;
                case Opcode.Xtr:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _state.R = (uint)((_memory.MemLoad(frame.EffectiveAddress) >> 41) & 0x3Fu);
                    break;
                case Opcode.Rte:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    frame.A = ((ulong)(_state.R & frame.EffectiveAddress & 0x7Fu)) << 41;
                    _state.SetLogical();
                    break;
                case Opcode.Yta:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    if (_state.IsLogical) frame.A = frame.Y;
                    else
                    {
                        ulong savedY = frame.Y;
                        frame.A = (frame.A & ~(ulong)ArchitectureConstants.BITS41) |
                            (frame.Y & ArchitectureConstants.BITS40);
                        _state.A = Word48.FromInt48(frame.A);
                        _alu.AddExponent((int)(frame.EffectiveAddress & 0x7Fu) - 64);
                        frame.A = _state.A.Value;
                        frame.Y = savedY;
                    }
                    break;
                case Opcode.Ext:
                    throw new ProcessorException("Illegal instruction 032 зпп");
                case Opcode.Op33:
                    throw new ProcessorException("Illegal instruction 033 счп");
                case Opcode.EPlusN:
                    ExecuteExponentImmediate(ref frame, (int)(Addr(addr + m[reg]) & 0x7Fu) - 64);
                    break;
                case Opcode.EMinusN:
                    ExecuteExponentImmediate(ref frame, 64 - (int)(Addr(addr + m[reg]) & 0x7Fu));
                    break;
                case Opcode.Asn:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Shift((int)(frame.EffectiveAddress & 0x7Fu) - 64);
                    UseAluResult(ref frame);
                    _state.SetLogical();
                    break;
                case Opcode.Ntr:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _state.R = frame.EffectiveAddress & 0x3Fu;
                    frame.RegistersInState = true;
                    break;
                default:
                    throw new InvalidOperationException($"Opcode {frame.Instruction.Opcode} is not a memory instruction.");
            }
            return InstructionOutcome.Continue;
        }

        private void ExecuteExponentFromMemory(ref ExecutionFrame frame, int direction)
        {
            uint addr = frame.Address;
            int reg = frame.Instruction.Register;
            PrepareStack(addr, reg);
            SetEffectiveAddress(ref frame, Addr(addr + _state.M[reg]));
            int exponent = (int)(_memory.MemLoad(frame.EffectiveAddress) >> 41);
            _alu.AddExponent(direction > 0 ? exponent - 64 : 64 - exponent);
            UseAluResult(ref frame);
            _state.SetMultiplicative();
        }

        private void ExecuteExponentImmediate(ref ExecutionFrame frame, int delta)
        {
            SetEffectiveAddress(ref frame, Addr(frame.Address + _state.M[frame.Instruction.Register]));
            _alu.AddExponent(delta);
            UseAluResult(ref frame);
            _state.SetMultiplicative();
        }

        private Word48 LoadWord(ref ExecutionFrame frame) =>
            Word48.FromInt48(_memory.MemLoad(frame.EffectiveAddress));

        private void UseAluResult(ref ExecutionFrame frame)
        {
            frame.RegistersInState = true;
        }

        private void PrepareStack(uint addr, int reg)
        {
            if (addr == 0 && reg == 15)
            {
                _state.M[15] = Addr(_state.M[15] - 1);
                _state.StackCorrection = 1;
            }
        }

        private void SetEffectiveAddress(ref ExecutionFrame frame, uint value)
        {
            frame.EffectiveAddress = value;
            _state.EffectiveAddress = value;
        }

        private static uint Addr(uint value) => ArchitectureConstants.NormalizeAddress(value);
        private const ulong Bit49 = 1UL << 48;
    }
}
