using System.Runtime.CompilerServices;

namespace Besm6.Core;

internal readonly record struct CpuMemoryTransfer(uint Address, bool Write, Word48 Word);

/// <summary>One set of data transitions; UU (УУ) may wait between them.</summary>
internal sealed partial class MemoryInstructionExecutor
{
    internal static bool CanExecuteData(Opcode opcode) => opcode is Opcode.Xta or Opcode.Atx or Opcode.Stx or
        Opcode.Xts or Opcode.Arx or Opcode.Acx or Opcode.Anx or Opcode.Asx or Opcode.Xtr or
        Opcode.Sti or Opcode.Its or Opcode.Wtc;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginLoad(ref ExecutionFrame frame)
    {
        PrepareStack(frame.Address, frame.Instruction.Register);
        uint address = Addr(frame.Address + _state.M[frame.Instruction.Register]);
        SetEffectiveAddress(ref frame, address); return address;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginStore(ref ExecutionFrame frame)
    {
        uint address = Addr(frame.Address + _state.M[frame.Instruction.Register]);
        SetEffectiveAddress(ref frame, address); return address;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishLoad(ref ExecutionFrame frame, ulong word) { frame.A = word; _state.SetLogical(); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishStore(ref ExecutionFrame frame)
    {
        if (frame.Address == 0 && frame.Instruction.Register == 15) _state.M[15] = Addr(_state.M[15] + 1);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteLoad(ref ExecutionFrame frame)
    {
        FinishLoad(ref frame, _memory.MemLoad(BeginLoad(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteStore(ref ExecutionFrame frame)
    {
        _memory.MemStore(BeginStore(ref frame), frame.A); FinishStore(ref frame); return InstructionOutcome.Continue;
    }

    // Direct transitions in functional execution; no opcode redispatch or phase loop.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteStx(ref ExecutionFrame frame)
    {
        _memory.MemStore(BeginStore(ref frame), frame.A); FinishLoad(ref frame, _memory.MemLoad(PopDataStack())); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteXts(ref ExecutionFrame frame)
    {
        _memory.MemStore(_state.M[15], frame.A); FinishLoad(ref frame, _memory.MemLoad(BeginSecondXts(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteArx(ref ExecutionFrame frame)
    {
        FinishCyclic(ref frame, _memory.MemLoad(BeginLoad(ref frame)), false); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteAcx(ref ExecutionFrame frame)
    {
        FinishCyclic(ref frame, _memory.MemLoad(BeginLoad(ref frame)), true); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteAnx(ref ExecutionFrame frame)
    {
        FinishAnx(ref frame, _memory.MemLoad(BeginAnx(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteAsx(ref ExecutionFrame frame)
    {
        FinishShift(ref frame, _memory.MemLoad(BeginLoad(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteXtr(ref ExecutionFrame frame)
    {
        FinishMode(_memory.MemLoad(BeginLoad(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteSti(ref ExecutionFrame frame)
    {
        FinishSti(ref frame, _memory.MemLoad(BeginSti(ref frame))); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteIts(ref ExecutionFrame frame)
    {
        _memory.MemStore(BeginIts(ref frame), frame.A); FinishIts(ref frame); return InstructionOutcome.Continue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal InstructionOutcome ExecuteWtc(ref ExecutionFrame frame)
    {
        FinishWtc(ref frame, _memory.MemLoad(BeginWtc(ref frame))); return InstructionOutcome.Continue;
    }

    internal CpuMemoryTransfer BeginData(ref ExecutionFrame frame, int phase)
    {
        uint address; bool write = false;
        Opcode opcode = frame.Instruction.Opcode;
        if (phase == 1)
        {
            address = opcode switch
            {
                Opcode.Stx => PopDataStack(),
                Opcode.Xts => BeginSecondXts(ref frame),
                _ => throw new InvalidOperationException("Unexpected second data transfer.")
            };
        }
        else if (phase != 0) throw new InvalidOperationException("Unexpected data transfer phase.");
        else switch (opcode)
        {
            case Opcode.Atx: case Opcode.Stx: address = BeginStore(ref frame); write = true; break;
            case Opcode.Xts: address = _state.M[15]; write = true; break;
            case Opcode.Its: address = BeginIts(ref frame); write = true; break;
            case Opcode.Sti: address = BeginSti(ref frame); break;
            case Opcode.Wtc: address = BeginWtc(ref frame); break;
            case Opcode.Anx: address = BeginAnx(ref frame); break;
            default:
                if (!CanExecuteData(opcode)) throw new InvalidOperationException("Not a data transfer instruction.");
                address = BeginLoad(ref frame); break;
        }
        return new(address, write, new(frame.A));
    }

    internal bool AcceptData(ref ExecutionFrame frame, int phase, ulong word)
    {
        switch (frame.Instruction.Opcode)
        {
            case Opcode.Xta: FinishLoad(ref frame, word); break;
            case Opcode.Atx: FinishStore(ref frame); break;
            case Opcode.Stx: case Opcode.Xts:
                if (phase == 0) return false;
                FinishLoad(ref frame, word); break;
            case Opcode.Arx: FinishCyclic(ref frame, word, false); break;
            case Opcode.Acx: FinishCyclic(ref frame, word, true); break;
            case Opcode.Anx: FinishAnx(ref frame, word); break;
            case Opcode.Asx: FinishShift(ref frame, word); break;
            case Opcode.Xtr: FinishMode(word); break;
            case Opcode.Sti: FinishSti(ref frame, word); break;
            case Opcode.Its: FinishIts(ref frame); break;
            case Opcode.Wtc: FinishWtc(ref frame, word); break;
            default: throw new InvalidOperationException("Not a data transfer instruction.");
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint PopDataStack()
    {
        _state.M[15] = Addr(_state.M[15] - 1); _state.StackCorrection = 1; return _state.M[15];
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginSecondXts(ref ExecutionFrame frame)
    {
        _state.M[15] = Addr(_state.M[15] + 1); _state.StackCorrection = -1; return BeginStore(ref frame);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginAnx(ref ExecutionFrame frame)
    {
        uint address = BeginLoad(ref frame);
        if (frame.A != 0)
        {
            int bit = Processor.Besm6HighestBit(frame.A);
            _alu.Shift(48 - bit); frame.Y = _state.Y.Value; frame.A = (ulong)bit;
        }
        else frame.Y = 0;
        return address;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishCyclic(ref ExecutionFrame frame, ulong word, bool count)
    {
        frame.A = CyclicAdd(count ? (ulong)Processor.Besm6CountOnes(frame.A) : frame.A, word); frame.Y = 0;
        if (count) _state.SetLogical(); else _state.SetMultiplicative();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishAnx(ref ExecutionFrame frame, ulong word)
    { frame.A = CyclicAdd(frame.A, word); _state.SetLogical(); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishShift(ref ExecutionFrame frame, ulong word)
    { _alu.Shift((int)(word >> 41) - 64); frame.RegistersInState = true; _state.SetLogical(); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishMode(ulong word) => _state.R = (uint)((word >> 41) & 0x3F);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginSti(ref ExecutionFrame frame)
    {
        uint target = BeginStore(ref frame) & (_supervisor?.IsSupervisor == true ? 31u : 15u);
        if (_supervisor?.IsSupervisor == true) DataEffective(ref frame, target);
        return target != 15 ? PopDataStack() : Addr((uint)frame.A);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishSti(ref ExecutionFrame frame, ulong word)
    {
        uint address = Addr((uint)frame.A); frame.A = word; _state.SetLogical();
        if (_supervisor?.IsSupervisor == true) _supervisor.WriteModifier((int)frame.EffectiveAddress, address);
        else { _state.M[frame.EffectiveAddress & 15] = address; _state.M[0] = 0; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginIts(ref ExecutionFrame frame)
    {
        if (_supervisor?.IsSupervisor == true)
            DataEffective(ref frame, (frame.Address + _state.M[frame.Instruction.Register]) & 31);
        return _state.M[15];
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishIts(ref ExecutionFrame frame)
    {
        _state.M[15] = Addr(_state.M[15] + 1);
        if (_supervisor?.IsSupervisor == true) frame.A = _supervisor.ReadModifier((int)frame.EffectiveAddress);
        else { BeginStore(ref frame); frame.A = Addr(_state.M[frame.EffectiveAddress & 15]); }
        _state.SetLogical();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint BeginWtc(ref ExecutionFrame frame) { frame.RegistersInState = false; return BeginLoad(ref frame); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FinishWtc(ref ExecutionFrame frame, ulong word) => frame.NextC = Addr((uint)word);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint DataEffective(ref ExecutionFrame frame, uint address)
    {
        uint result = Addr(address); SetEffectiveAddress(ref frame, result); return result;
    }
    private static ulong CyclicAdd(ulong a, ulong b)
    {
        ulong result = a + b;
        return (result & (1UL << 48)) == 0 ? result : (result + 1) & ArchitectureConstants.BITS48;
    }
}
