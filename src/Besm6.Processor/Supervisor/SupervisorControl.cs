namespace Besm6.Core;

/// <summary>
/// Untimed supervisor state from TO-8 §§3.55–3.60, 6.16, 6.35 and 7.5–7.21.
/// This is not a model of overlapped RK/RR acceptance or asynchronous arithmetic unit execution.
/// </summary>
public sealed class SupervisorControl
{
    public const uint SavedFlagsMask = 0x73F;
    public const uint SavedRightHalf = 0x100;
    public const uint SavedNextInstruction = 0x200;
    private readonly ProcessorState _state;
    private readonly MappedMemoryBackend _memory;
    private readonly uint[] _special = new uint[32];
    private ControlUnitStatus _status;
    private SupervisorMode _mode;
    private int _instructionWatchDelay;
    private bool _instructionWatchActive;
    private bool _operandWatchActive;

    internal SupervisorControl(ProcessorState state, MappedMemoryBackend memory)
    {
        _state = state;
        _memory = memory;
        memory.InstructionAccessAdmitted = (request, _) => CheckInstructionAccess(request);
        memory.OperandAccessAdmitted = CheckOperandAccess;
        ResetCpu();
    }

    public ISupervisorIo? Io { get; set; }
    public ulong InternalInterrupts { get; private set; }
    public uint SavedFlags => _special[23];
    public bool IsSupervisor => Mode != SupervisorMode.Mathematical;
    public bool ArithmeticStopBlocked { get; private set; } = true;
    /// <summary>Host panel configuration, not a guest M17 flag (TO-8 §3.61).</summary>
    public bool PanelStopEnabled { get; set; } = true;

    /// <summary>Software bootstrap, preserving memory, RP/RZ, buffers and attached IO.</summary>
    public void ResetCpu()
    {
        Array.Clear(_special);
        InternalInterrupts = 0;
        ArithmeticStopBlocked = true;
        _instructionWatchActive = _operandWatchActive = false;
        _instructionWatchDelay = 0;
        Status = new((ControlUnitFlags)0x40F);
        Mode = SupervisorMode.Interrupt;
        _memory.InvertLeftStoreControl = true;
        _memory.InvertRightStoreControl = true;
    }

    public ControlUnitStatus Status
    {
        get => _status;
        set { _status = value; _memory.ApplyControlStatus(value); }
    }

    public SupervisorMode Mode
    {
        get => _mode;
        set
        {
            if ((value & ~(SupervisorMode.Extracode | SupervisorMode.Interrupt)) != 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            _mode = value;
            _memory.Supervisor = value != SupervisorMode.Mathematical;
        }
    }

    public uint ReadModifier(int register)
    {
        ValidateRegister(register);
        if (register < 16) return register == 0 ? 0 : _state.M[register];
        RequireSupervisor();
        if (register == 16) return _state.C;
        if (register is 17 or 28 or 29)
            throw new ProcessorException("Illegal instruction: special modifier is write-only (TO-8 §§7.8, 7.19, 7.21).");
        if (register is 23 or 26 or 27) return _special[register];
        throw Unsupported(register);
    }

    public void WriteModifier(int register, uint value)
    {
        ValidateRegister(register);
        value &= 0x7FFF;
        if (register < 16) { if (register != 0) _state.M[register] = value; return; }
        RequireSupervisor();
        switch (register)
        {
            case 16: _state.C = value; break;
            case 17: Status = new((ControlUnitFlags)(value & ControlUnitStatus.KnownMask)); break;
            case 23: _special[23] = value & SavedFlagsMask; break;
            case 26: case 27: _special[register] = value; break;
            case 28: case 29:
                // The comparison address retains the assignment mode at its write.
                _special[register] = value | (Status.AssignmentBlocked ? 0x8000u : 0);
                if (register == 28)
                {
                    _instructionWatchActive = true;
                    // The write instruction completes too; four subsequent instructions
                    // must then complete before comparison can cause an interrupt (§6.7).
                    _instructionWatchDelay = 5;
                }
                else _operandWatchActive = true;
                break;
            default: throw Unsupported(register);
        }
    }

    public uint GetComparisonAddress(int register)
    {
        if (register is not (28 or 29)) throw new ArgumentOutOfRangeException(nameof(register));
        return _special[register];
    }

    public void CheckInstructionAccess(MemoryRequestAddress request)
    {
        if (_instructionWatchActive && _instructionWatchDelay == 0 &&
            _special[28] == request.EncodedValue)
            throw new HardwareWatchException(1UL << 11);
    }

    public void CheckOperandAccess(MemoryRequestAddress request, bool write)
    {
        if (request.Address == 0) return;
        bool watchWrite = (Status.Flags & ControlUnitFlags.MatchWriteAddress) != 0;
        if (_operandWatchActive && watchWrite == write && _special[29] == request.EncodedValue)
            throw new HardwareWatchException(1UL << (write ? 16 : 15));
    }

    public void CompletedInstruction()
    {
        if (_instructionWatchDelay > 0) _instructionWatchDelay--;
    }

    /// <summary>SpecialRegisters[i] denotes M(16+i); the returned array is a copy.</summary>
    public SupervisorSnapshot Snapshot()
    {
        var registers = _special.AsSpan(16, 16).ToArray();
        registers[0] = _state.C;
        registers[1] = Status.EncodedControls;
        return new(Status, Mode, InternalInterrupts, registers, ArithmeticStopBlocked,
            PanelStopEnabled, _instructionWatchActive, _operandWatchActive, _instructionWatchDelay);
    }

    public void EnterExtracode(uint opcode, uint effectiveAddress, uint returnWord)
    {
        uint vector = opcode switch
        {
            >= 0x28 and <= 0x3F => 0x140 + opcode,
            0x80 => 0x170,
            0x88 => 0x171,
            _ => throw new ArgumentOutOfRangeException(nameof(opcode))
        };
        // The return address denotes the next word, whose left instruction resumes.
        _special[23] = CaptureFlags() & ~(SavedRightHalf | 0x30u);
        _special[26] = returnWord & 0x7FFF;
        _state.M[14] = effectiveAddress & 0x7FFF;
        Enter(vector, SupervisorMode.Extracode);
    }

    public void EnterExternal()
    {
        _special[23] = CaptureFlags();
        _special[27] = _state.K & 0x7FFF;
        Enter(0x141, SupervisorMode.Interrupt);
    }

    // РГПр — главный регистр прерывания. Cause storage is distinct from UU acceptance.
    internal void LatchInternalCause(ulong interruptFlags) =>
        InternalInterrupts |= interruptFlags & Word48.Mask48;

    // УУ — устройство управления. Shared stop policy for serial faults and hardware input.
    internal void AcceptInternalInterrupt(ulong signal, uint returnWord, uint savedFlags)
    {
        bool arithmetic = (signal & (3UL << 21)) != 0;
        bool control = (signal & ((1UL << 20) | (1UL << 14))) != 0;
        ControlUnitFlags stop = arithmetic ? ControlUnitFlags.StopOnInternalInterrupt | ControlUnitFlags.StopOnControlInterrupt :
            control ? ControlUnitFlags.StopOnControlInterrupt : ControlUnitFlags.StopOnInternalInterrupt;
        if ((!arithmetic || !ArithmeticStopBlocked) && (Status.Flags & stop) != 0)
            throw new SupervisorHaltException(signal);
        EnterInternal(signal, returnWord, savedFlags);
    }

    public void EnterInternal(ulong interruptFlags, uint returnWord, uint savedFlags)
    {
        LatchInternalCause(interruptFlags);
        _special[23] = (CaptureFlags() & ~(SavedRightHalf | SavedNextInstruction)) |
            (savedFlags & (SavedRightHalf | SavedNextInstruction | 0x30u));
        _special[27] = returnWord & 0x7FFF;
        Enter(0x140, SupervisorMode.Interrupt);
    }

    public void ReturnFromInterrupt(uint instructionRegister)
    {
        if (!IsSupervisor) throw new ProcessorException("Return from interrupt requires supervisor mode.");
        uint saved = _special[23];
        Status = new((ControlUnitFlags)((Status.EncodedControls & ~ControlUnitStatus.AddressControlMask) |
            (saved & ControlUnitStatus.AddressControlMask)));
        Mode = (SupervisorMode)((saved >> 2) & 3);
        _state.K = (instructionRegister & 7) switch { 2 => _special[26], 3 => _special[27], _ => 0 };
        _state.IsRightHalf = (saved & SavedRightHalf) != 0;
        _state.ApplyC = (saved & 0x10) != 0;
    }

    public Word48 ReadRegister(uint address)
    {
        RequireSupervisor();
        uint register = address & 0x7F;
        if (register < 8)
        {
            MemoryWord50 word = _memory.ReadOperandBufferRegister((int)register);
            if (!word.HasValidOperandControl)
                throw new MemoryControlException(register, MemoryAccessKind.OperandRead);
            return word.Data;
        }
        if (register == 0x1F)
            return new(InternalInterrupts | (Io?.ReadRegister(register).Value ?? 0));
        return RequireIo().ReadRegister(register);
    }

    public void WriteRegister(uint address, Word48 value)
    {
        RequireSupervisor();
        uint register = address & 0x7F;
        if (register < 8)
        {
            _memory.WriteOperandBufferRegister((int)register, value);
            return;
        }
        if (register is >= 0x10 and <= 0x17)
        {
            // Guest software must publish BRZ before changing RP (TO-8 §4.24).
            _memory.Assignment.ValidateImportAssignmentGroup(register & 7, value);
            _memory.GuestAssignmentChanging();
            _memory.Assignment.ImportAssignmentGroup(register & 7, value);
            return;
        }
        if (register is >= 0x18 and <= 0x1B)
        {
            _memory.Assignment.ImportProtectionGroup(register & 3, value);
            return;
        }
        if (register is >= 0x40 and <= 0x5F)
        {
            ArithmeticStopBlocked = (register & 1) != 0;
            _memory.InvertRightStoreControl = (register & 2) != 0;
            _memory.InvertLeftStoreControl = (register & 4) != 0;
            return;
        }
        if (register == 0x1F)
        {
            InternalInterrupts &= value.Value;
            Io?.WriteRegister(register, value);
            return;
        }
        RequireIo().WriteRegister(register, value);
    }

    private uint CaptureFlags() => (Status.EncodedControls & ControlUnitStatus.AddressControlMask) |
        ((uint)Mode << 2) | (_state.ApplyC ? 0x10u : 0) | (_state.IsRightHalf ? SavedRightHalf : 0);

    private void Enter(uint vector, SupervisorMode mode)
    {
        Status = new((ControlUnitFlags)(Status.EncodedControls | ControlUnitStatus.AddressControlMask));
        Mode = mode;
        _state.K = vector;
        _state.IsRightHalf = false;
        _state.ApplyC = false;
    }

    private void RequireSupervisor()
    {
        if (!IsSupervisor) throw new ProcessorException("Illegal instruction: register access requires supervisor mode.");
    }
    private ISupervisorIo RequireIo() => Io ?? throw new NotSupportedException("No supervisor register/device controller is attached.");
    private static NotSupportedException Unsupported(int register) => new($"Special modifier {register} is not implemented.");
    private static void ValidateRegister(int register)
    {
        if (register is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(register));
    }
}
