namespace Besm6.Core;

public sealed partial class InstructionExecutor
{
    // Functional serial boundaries. Prefetch/overlap fault positions belong to stage 5.
    private bool ExecuteSupervisor()
    {
        var supervisor = _processor.Supervisor!;
        if (supervisor.Io is { PendingExternalInterrupts: not 0 } &&
            (supervisor.Status.Flags & ControlUnitFlags.ExternalInterruptsBlocked) == 0)
            supervisor.EnterExternal();
        uint start = _state.K;
        bool right = _state.IsRightHalf;
        try
        {
            bool stopped = ExecuteCore();
            supervisor.CompletedInstruction();
            return stopped;
        }
        catch (HardwareWatchException failure)
        {
            bool command = failure.Signal == (1UL << 11);
            return InterruptFailedStep(supervisor, failure.Signal,
                command && !right ? start : ArchitectureConstants.NormalizeAddress(start + 1),
                command ? (right ? 0x200u : 0x300u) : (right ? 0x300u : 0x200u));
        }
        catch (MemoryProtectionException failure)
        {
            bool command = failure.AccessKind is MemoryAccessKind.InstructionLeft or MemoryAccessKind.InstructionRight;
            ulong signal = command ? 1UL << 13 : (1UL << 19) | ((ulong)failure.MathematicalPage << 4);
            uint saved = command ? (right ? 0x200u : 0x300u) : (right ? 0x300u : 0x200u);
            return InterruptFailedStep(supervisor, signal,
                command && !right ? start : ArchitectureConstants.NormalizeAddress(start + 1), saved);
        }
        catch (MemoryControlException failure)
        {
            bool command = failure.AccessKind is MemoryAccessKind.InstructionLeft or MemoryAccessKind.InstructionRight;
            bool registerRead = ((Opcode)((_state.RawInstruction >> 12) & 0x3F)) == Opcode.Mod &&
                (_state.EffectiveAddress & 0x7F) < 8;
            var backend = _memory.MappedBackend!;
            var fault = backend.LastFault;
            bool buffered = !registerRead && fault is { Source: MemoryFaultSource.OperandBuffer };
            ulong detail = registerRead ? failure.PhysicalAddress & 7 : buffered ?
                (ulong)backend.FindOperandBufferRegister(fault!.Value.Request) : 8UL | (failure.PhysicalAddress & 7);
            ulong signal = command ? 1UL << 14 : (1UL << 20) |
                detail;
            return InterruptFailedStep(supervisor, signal, right ? ArchitectureConstants.NormalizeAddress(start + 1) : start,
                right ? 0u : 0x100u);
        }
        catch (ProcessorException failure) when (failure.Message.StartsWith("Illegal instruction", StringComparison.Ordinal) ||
            failure.Message.StartsWith("Unknown instruction", StringComparison.Ordinal) ||
            failure.Message == "Division by zero" || failure.Message == "Arithmetic overflow")
        {
            ulong signal = failure.Message == "Division by zero" ? 7UL << 20 :
                failure.Message == "Arithmetic overflow" ? 3UL << 20 : 1UL << 12;
            return InterruptFailedStep(supervisor, signal, right ? ArchitectureConstants.NormalizeAddress(start + 1) : start,
                right ? 0u : 0x100u);
        }
    }

    private bool InterruptFailedStep(SupervisorControl supervisor, ulong signal, uint returnWord, uint flags)
    {
        _processor.CancelInstructionTrace();
        _processor.LastStepCompleted = false;
        bool arithmetic = (signal & (3UL << 21)) != 0;
        bool control = (signal & ((1UL << 20) | (1UL << 14))) != 0;
        ControlUnitFlags stop = arithmetic ? ControlUnitFlags.StopOnInternalInterrupt | ControlUnitFlags.StopOnControlInterrupt :
            control ? ControlUnitFlags.StopOnControlInterrupt : ControlUnitFlags.StopOnInternalInterrupt;
        if ((!arithmetic || !supervisor.ArithmeticStopBlocked) && (supervisor.Status.Flags & stop) != 0)
            throw new SupervisorHaltException(signal);
        supervisor.EnterInternal(signal, returnWord, flags);
        return false;
    }

    private InstructionOutcome DispatchSupervisor(SupervisorControl supervisor, ref ExecutionFrame frame)
    {
        Opcode opcode = frame.Instruction.Opcode;
        int reg = frame.Instruction.Register;
        uint ea = ArchitectureConstants.NormalizeAddress(frame.Address + _state.M[reg]);
        if (ExtracodeInstructionExecutor.CanExecute(opcode))
        {
            supervisor.EnterExtracode((uint)opcode, ea,
                ArchitectureConstants.NormalizeAddress(_state.K + (_state.IsRightHalf ? 1u : 0u)));
            frame.RegistersInState = true;
            frame.UpdateModificationRegister = false;
            return InstructionOutcome.Continue;
        }
        bool privileged = supervisor.Mode != SupervisorMode.Mathematical;
        if (opcode == Opcode.Stop && !supervisor.PanelStopEnabled)
        {
            if ((supervisor.Status.Flags & ControlUnitFlags.StopOnControlInterrupt) == 0)
            {
                supervisor.EnterExtracode(0x33, ea,
                    ArchitectureConstants.NormalizeAddress(_state.K + (_state.IsRightHalf ? 1u : 0u)));
                frame.UpdateModificationRegister = false;
            }
            frame.RegistersInState = true;
            return InstructionOutcome.Continue;
        }
        if (opcode is Opcode.Mod or Opcode.Ext or Opcode.Op33 or Opcode.Ij)
        {
            if (!privileged) throw new ProcessorException("Illegal instruction: supervisor command in mathematical mode");
            _state.EffectiveAddress = ea;
            if (opcode == Opcode.Mod)
            {
                if ((ea & 0x80) != 0) { frame.A = supervisor.ReadRegister(ea).Value; _state.SetLogical(); }
                else supervisor.WriteRegister(ea, new(frame.A));
            }
            else if (opcode is Opcode.Ext or Opcode.Op33)
            {
                var io = supervisor.Io ?? throw new NotSupportedException("No supervisor peripheral controller.");
                if ((ea & 0x800) != 0) { frame.A = io.ReadDevice(ea).Value; _state.SetLogical(); }
                else io.WriteDevice(ea, new(frame.A));
            }
            else
            {
                supervisor.ReturnFromInterrupt((uint)reg);
                frame.RegistersInState = true;
                frame.UpdateModificationRegister = false;
            }
            return InstructionOutcome.Continue;
        }
        if (opcode is Opcode.Op46 or Opcode.Op47)
        {
            if (!privileged) throw new ProcessorException("Illegal instruction: unused command in mathematical mode");
            uint target = frame.Address & 15;
            uint value = frame.Address & 0xFFF;
            if (opcode == Opcode.Op47) value += _state.M[target];
            _state.M[target] = ArchitectureConstants.NormalizeAddress(value);
            _state.M[0] = 0;
            _state.EffectiveAddress = frame.Address;
            frame.RegistersInState = true;
            return InstructionOutcome.Continue;
        }
        if (privileged && reg == 0 && opcode is Opcode.Vtm or Opcode.Utm)
        {
            supervisor.Status = supervisor.Status.WithSupervisorAddressControls(frame.Address);
            frame.RegistersInState = true;
            return InstructionOutcome.Continue;
        }
        if (privileged && opcode is >= Opcode.Ati and <= Opcode.JPlusM)
        {
            uint target = (opcode is Opcode.Mtj or Opcode.JPlusM ? frame.Address : ea) & 31;
            _state.EffectiveAddress = target;
            if (opcode is Opcode.Ati or Opcode.Sti)
            {
                uint value = ArchitectureConstants.NormalizeAddress((uint)frame.A);
                if (opcode == Opcode.Sti)
                {
                    if (target != 15) { _state.M[15] = ArchitectureConstants.NormalizeAddress(_state.M[15] - 1); _state.StackCorrection = 1; }
                    frame.A = _memory.MemLoad(target != 15 ? _state.M[15] : value);
                    _state.SetLogical();
                }
                supervisor.WriteModifier((int)target, value);
            }
            else if (opcode is Opcode.Ita or Opcode.Its)
            {
                if (opcode == Opcode.Its)
                {
                    _memory.MemStore(_state.M[15], frame.A);
                    _state.M[15] = ArchitectureConstants.NormalizeAddress(_state.M[15] + 1);
                }
                frame.A = supervisor.ReadModifier((int)target);
                _state.SetLogical();
            }
            else
            {
                uint value = _state.M[reg];
                if (opcode == Opcode.JPlusM && target < 16) value += supervisor.ReadModifier((int)target);
                supervisor.WriteModifier((int)target, ArchitectureConstants.NormalizeAddress(value));
            }
            return InstructionOutcome.Continue;
        }
        return Dispatch(ref frame);
    }
}
