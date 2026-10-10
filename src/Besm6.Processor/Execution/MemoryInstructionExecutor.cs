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

        // Keep the frequent load/store/mode instructions small enough to inline.
        // Their implementations live here once; other opcodes use the same fallback
        // in both execution modes and with diagnostic subscribers.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal InstructionOutcome Execute(ref ExecutionFrame frame)
        {
            switch (frame.Instruction.Opcode)
            {
                case Opcode.Xta:
                {
                    uint addr = frame.Address;
                    int reg = frame.Instruction.Register;
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + _state.M[reg]));
                    frame.A = _memory.MemLoad(frame.EffectiveAddress);
                    _state.SetLogical();
                    return InstructionOutcome.Continue;
                }
                case Opcode.Atx:
                {
                    uint addr = frame.Address;
                    int reg = frame.Instruction.Register;
                    SetEffectiveAddress(ref frame, Addr(addr + _state.M[reg]));
                    _memory.MemStore(frame.EffectiveAddress, frame.A);
                    if (addr == 0 && reg == 15) _state.M[15] = Addr(_state.M[15] + 1);
                    return InstructionOutcome.Continue;
                }
                case Opcode.Ntr:
                    SetEffectiveAddress(ref frame, Addr(frame.Address + _state.M[frame.Instruction.Register]));
                    _state.R = frame.EffectiveAddress & 0x3Fu;
                    frame.RegistersInState = true;
                    return InstructionOutcome.Continue;
                default:
                    return ExecuteOther(ref frame);
            }
        }

        private InstructionOutcome ExecuteOther(ref ExecutionFrame frame)
        {
            var execution = new ImmediateArithmeticInstructionExecution(_alu);
            return ExecuteOther(ref frame, ref execution);
        }

        internal static bool CanCaptureArithmetic(Opcode opcode) => opcode is
            Opcode.APlusX or Opcode.AMinusX or Opcode.XMinusA or Opcode.Amx or
            Opcode.Avx or Opcode.ADivX or Opcode.AMulX or Opcode.EPlusX or
            Opcode.EMinusX or Opcode.EPlusN or Opcode.EMinusN or Opcode.Aax or Opcode.Aex or Opcode.Aox or Opcode.Apx or Opcode.Aux;

        internal CapturedArithmeticInstructionExecution CaptureArithmetic(ref ExecutionFrame frame)
        {
            var execution = new CapturedArithmeticInstructionExecution(_state.R);
            ExecuteOther(ref frame, ref execution);
            return execution;
        }

        internal uint ResolveMemoryOperandAddress(uint address, byte register) =>
            Addr(address + (address == 0 && register == 15 ? Addr(_state.M[15] - 1) : _state.M[register]));

        internal CapturedArithmeticInstructionExecution CaptureTransferredArithmetic(ref ExecutionFrame frame,
            uint address, Word48 word, Exception? failure) =>
            new MemoryInstructionExecutor(_state, _memory.WithTransferredOperand(address, word, failure), _alu)
                .CaptureArithmetic(ref frame);

        internal void PublishArithmetic(ref ExecutionFrame frame, NormalizedArithmeticResult result, bool additive, bool logical)
        {
            if (logical)
            {
                frame.A = result.A.Value;
                frame.Y = result.Y.Value;
                _state.SetLogical();
                return;
            }
            _alu.PublishPreparedResult(result);
            var execution = new ImmediateArithmeticInstructionExecution(_alu);
            execution.Finish(ref frame, _state, additive);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        private InstructionOutcome ExecuteOther<TExecution>(ref ExecutionFrame frame, ref TExecution arithmetic)
            where TExecution : struct, IArithmeticInstructionExecution
        {
            int reg = frame.Instruction.Register;
            uint addr = frame.Address;
            uint[] m = _state.M;

            switch (frame.Instruction.Opcode)
            {
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
                    arithmetic.Add(LoadWord(ref frame), false, false);
                    arithmetic.Finish(ref frame, _state, true);
                    break;
                case Opcode.AMinusX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Add(LoadWord(ref frame), false, true);
                    arithmetic.Finish(ref frame, _state, true);
                    break;
                case Opcode.XMinusA:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Add(LoadWord(ref frame), true, false);
                    arithmetic.Finish(ref frame, _state, true);
                    break;
                case Opcode.Amx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Add(LoadWord(ref frame), true, true);
                    arithmetic.Finish(ref frame, _state, true);
                    break;
                case Opcode.Aax:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Logical(ref frame, LoadWord(ref frame), LogicalArithmeticOperation.And);
                    arithmetic.FinishLogical(_state);
                    break;
                case Opcode.Aex:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Logical(ref frame, LoadWord(ref frame), LogicalArithmeticOperation.Xor);
                    arithmetic.FinishLogical(_state);
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
                    arithmetic.ChangeSign(((_memory.MemLoad(frame.EffectiveAddress) >> 40) & 1u) != 0);
                    arithmetic.Finish(ref frame, _state, true);
                    break;
                case Opcode.Aox:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Logical(ref frame, LoadWord(ref frame), LogicalArithmeticOperation.Or);
                    arithmetic.FinishLogical(_state);
                    break;
                case Opcode.ADivX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Divide(LoadWord(ref frame));
                    arithmetic.Finish(ref frame, _state, false);
                    break;
                case Opcode.AMulX:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Multiply(LoadWord(ref frame));
                    arithmetic.Finish(ref frame, _state, false);
                    break;
                case Opcode.Apx:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Logical(ref frame, LoadWord(ref frame), LogicalArithmeticOperation.Pack);
                    arithmetic.FinishLogical(_state);
                    break;
                case Opcode.Aux:
                    PrepareStack(addr, reg);
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    arithmetic.Logical(ref frame, LoadWord(ref frame), LogicalArithmeticOperation.Unpack);
                    arithmetic.FinishLogical(_state);
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
                    ExecuteExponentFromMemory(ref frame, 1, ref arithmetic);
                    break;
                case Opcode.EMinusX:
                    ExecuteExponentFromMemory(ref frame, -1, ref arithmetic);
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
                    ExecuteExponentImmediate(ref frame, false, ref arithmetic);
                    break;
                case Opcode.EMinusN:
                    ExecuteExponentImmediate(ref frame, true, ref arithmetic);
                    break;
                case Opcode.Asn:
                    SetEffectiveAddress(ref frame, Addr(addr + m[reg]));
                    _alu.Shift((int)(frame.EffectiveAddress & 0x7Fu) - 64);
                    UseAluResult(ref frame);
                    _state.SetLogical();
                    break;
                default:
                    throw new InvalidOperationException($"Opcode {frame.Instruction.Opcode} is not a memory instruction.");
            }
            return InstructionOutcome.Continue;
        }

        private void ExecuteExponentFromMemory<TExecution>(ref ExecutionFrame frame, int direction, ref TExecution arithmetic)
            where TExecution : struct, IArithmeticInstructionExecution
        {
            uint addr = frame.Address;
            int reg = frame.Instruction.Register;
            PrepareStack(addr, reg);
            SetEffectiveAddress(ref frame, Addr(addr + _state.M[reg]));
            int exponent = (int)(_memory.MemLoad(frame.EffectiveAddress) >> 41);
            arithmetic.AddExponent(direction > 0 ? exponent - 64 : 64 - exponent);
            arithmetic.Finish(ref frame, _state, false);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal uint ResolveImmediateAddress(uint address, byte register) => Addr(address + _state.M[register]);

        private void ExecuteExponentImmediate<TExecution>(ref ExecutionFrame frame, bool subtract, ref TExecution arithmetic)
            where TExecution : struct, IArithmeticInstructionExecution
        {
            SetEffectiveAddress(ref frame, ResolveImmediateAddress(frame.Address, frame.Instruction.Register));
            int exponent = (int)(frame.EffectiveAddress & 0x7F);
            arithmetic.AddExponent(subtract ? 64 - exponent : exponent - 64);
            arithmetic.Finish(ref frame, _state, false);
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
