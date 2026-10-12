namespace Besm6.Runtime.Timing;

/// <summary>
/// Statistical hosted CPU time, independent of functional execution and host time.
/// UU/AU component durations come from BookTimingSpecification. Buffer replacement,
/// overlap and hosted service costs are explicit estimates, not reconstructed microcode.
/// The supervisor hardware execution owner remains separate.
/// </summary>
internal sealed class HostedOriginalTiming : IDisposable
{
    private readonly Processor _cpu;
    private readonly HostedTimingCalendar _calendar;
    private readonly HostedTimingParameters _parameters;
    private DecodedInstruction _instruction;
    private uint _address, _stack, _register, _mode;
    private ulong _accumulator;
    private bool _branchTaken;
    private bool _attached;

    internal HostedOriginalTiming(Processor cpu, HostedTimingParameters? parameters = null)
    {
        _cpu = cpu;
        _parameters = parameters ?? HostedTimingParameters.Default;
        _calendar = new(_parameters);
        cpu.TraceInstruction += Begin;
        cpu.InstructionExecuted += Complete;
        _attached = true;
    }

    internal ulong Nanoseconds => (ulong)Math.Round(_calendar.Nanoseconds * _parameters.ModelTimeFactor);
    internal double Seconds => Nanoseconds / 1_000_000_000.0;

    private void Begin(uint address, bool right, uint raw, uint opcode)
    {
        _instruction = InstructionCodec.DecodeHalf(raw);
        _address = (_instruction.Address + (_cpu.ApplyC ? _cpu.C : 0)) & 0x7fff;
        _register = _cpu.GetM(_instruction.Register);
        _stack = _cpu.GetM(15);
        _accumulator = _cpu.GetA().Value;
        _mode = _cpu.GetR();
        _branchTaken = Taken(_instruction.Opcode);
        _calendar.Fetch(address);
    }

    private bool Taken(Opcode opcode)
    {
        bool zero = (_mode & (uint)RFlags.Add) != 0 ? (_accumulator & (1UL << 40)) == 0 :
            (_mode & (uint)RFlags.Mult) != 0 ? (_accumulator & (1UL << 47)) != 0 :
            (_mode & (uint)RFlags.Mode) == (uint)RFlags.Log && _accumulator == 0;
        return opcode switch
        {
            Opcode.Uza => zero,
            Opcode.U1a => !zero,
            Opcode.Vzm or Opcode.Op36 => _register == 0,
            Opcode.V1m or Opcode.Vlm => _register != 0,
            _ => false,
        };
    }

    private void Complete(Opcode opcode)
    {
        uint effective = (_address + _register) & 0x7fff;
        bool stackRead = _address == 0 && _instruction.Register == 15;
        uint readAddress = stackRead ? (_stack - 1) & 0x7fff : effective;
        ulong operandReady = _calendar.ControlNanoseconds;
        // Arithmetic consumers wait for the previous AU result. Independent UU
        // instructions can run while AU is busy. No architectural state is delayed.
        if (opcode is Opcode.Atx or Opcode.Stx or Opcode.Xts or Opcode.Ati or
            Opcode.Sti or Opcode.Its or Opcode.Uza or Opcode.U1a or Opcode.Stop ||
            Besm6Timing.IsExtracode(opcode))
            _calendar.WaitForArithmetic();

        switch (opcode)
        {
            case Opcode.Atx:
                operandReady = _calendar.Store(effective); break;
            case Opcode.Stx:
                _calendar.Store(effective);
                operandReady = _calendar.Read(stackRead ? _stack : (_stack - 1) & 0x7fff);
                break;
            case Opcode.Xts:
                _calendar.Store(_stack);
                operandReady = _calendar.Read(stackRead ? (_stack + 1) & 0x7fff : effective);
                break;
            case Opcode.Its:
                operandReady = _calendar.Store(_stack); break;
            case Opcode.Sti:
                operandReady = _calendar.Read((effective & 15) == 15 ?
                    (uint)(_accumulator & 0x7fff) : (_stack - 1) & 0x7fff);
                break;
            default:
                if ((uint)opcode is >= 4 and <= 0x17 || opcode == Opcode.Wtc)
                    operandReady = _calendar.Read(readAddress);
                break;
        }

        if (Besm6Timing.IsExtracode(opcode))
        {
            ulong duration = HostedMathTiming.Nanoseconds(opcode, effective);
            _calendar.Service(duration > 5_000 ? (ulong)Math.Round(duration * _parameters.MathFactor) : duration);
            return;
        }
        if (!BookTimingSpecification.TryGet(opcode, out var timing))
        {
            // STOP and unused/privileged forms have no entry in the book table.
            // Hosted fallback is explicitly the legacy estimate, not a book value.
            _calendar.Execute((ulong)(Besm6Timing.CyclesOf(opcode) * 100), 0, operandReady);
            return;
        }
        ulong control = timing.ControlFirst.Nanoseconds;
        if (timing.ControlSecond is { } alternative)
            control = _branchTaken ? Math.Max(control, alternative.Nanoseconds) : Math.Min(control, alternative.Nanoseconds);
        ulong arithmetic = timing.Arithmetic is { } envelope ?
            (envelope.Average ?? envelope.Minimum).Nanoseconds : 0;
        if (_parameters.UseUnnormalizedMinimum && (_mode & (uint)RFlags.NormDisable) != 0 &&
            timing.Arithmetic is { } unnormalized && opcode is
                Opcode.APlusX or Opcode.AMinusX or Opcode.XMinusA or Opcode.Amx or Opcode.AMulX)
            arithmetic = unnormalized.Minimum.Nanoseconds;
        if (opcode is Opcode.Asn or Opcode.Asx)
        {
            // Immediate shifts have a known distance. Memory shifts use the
            // published upper bound; timing never performs extra guest reads.
            arithmetic = opcode == Opcode.Asn ?
                (ulong)(4 + Math.Abs((int)(effective & 127) - 64)) * 110 : 68 * 110;
        }
        _calendar.Execute((ulong)Math.Round(control * _parameters.ControlFactor), arithmetic, operandReady);
        // Modifier writes from AU cannot be consumed early by following UU commands.
        if (opcode is Opcode.Ati or Opcode.Sti or Opcode.Wtc) _calendar.WaitForArithmetic();
    }

    public void Dispose()
    {
        if (!_attached) return;
        _cpu.TraceInstruction -= Begin;
        _cpu.InstructionExecuted -= Complete;
        _attached = false;
    }
}

/// <summary>Separate logical UU/AU clocks and eight-bank memory reservations.
/// Eight instruction and eight operand entries use FIFO replacement; eight stores
/// may be outstanding. These are statistical shadow buffers, never guest storage.</summary>
internal sealed class HostedTimingCalendar
{
    private const ulong HitNanoseconds = 220;
    private readonly HostedTimingParameters _parameters;
    private readonly uint[] _instructions = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
    private readonly uint[] _operands;
    private readonly ulong[] _banks = new ulong[8];
    private readonly uint[] _storeAddresses = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
    private readonly ulong[] _stores = new ulong[8];
    private int _instructionSlot, _operandSlot, _storeSlot;
    private ulong _control, _arithmetic;
    internal ulong ControlNanoseconds => _control;
    internal ulong Nanoseconds => Math.Max(_control, _arithmetic);

    internal HostedTimingCalendar(HostedTimingParameters? parameters = null)
    {
        _parameters = parameters ?? HostedTimingParameters.Default;
        if (!double.IsFinite(_parameters.ControlFactor) || _parameters.ControlFactor <= 0 ||
            !double.IsFinite(_parameters.MathFactor) || _parameters.MathFactor <= 0 ||
            !double.IsFinite(_parameters.ModelTimeFactor) || _parameters.ModelTimeFactor <= 0 ||
            _parameters.InstructionFetchOverlapNanoseconds > 900 ||
            _parameters.OperandBufferCapacity is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(parameters));
        _operands = Enumerable.Repeat(uint.MaxValue, _parameters.OperandBufferCapacity).ToArray();
    }

    internal void Fetch(uint address)
    {
        ulong ready = BufferedRead(address, _instructions, ref _instructionSlot);
        // The selected portion of the buffer fetch overlaps UU processing.
        _control = ready - Math.Min(ready - _control, _parameters.InstructionFetchOverlapNanoseconds);
    }
    internal ulong Read(uint address) => BufferedRead(address, _operands, ref _operandSlot);

    private ulong BufferedRead(uint address, uint[] buffer, ref int slot)
    {
        if (address == 0 || Array.IndexOf(buffer, address) >= 0)
            return _control + HitNanoseconds;
        // Forward a pending store without a second MRAM transaction.
        for (int index = 0; index < 8; index++)
            if (_storeAddresses[index] == address && _stores[index] > _control)
                return _control + HitNanoseconds;
        ulong ready = Reserve(address, false);
        buffer[slot] = address;
        slot = (slot + 1) % buffer.Length;
        return ready;
    }

    internal ulong Store(uint address)
    {
        if (address == 0) return _control;
        _control = Math.Max(_control, _stores[_storeSlot]);
        ulong ready = Reserve(address, true);
        _stores[_storeSlot] = ready;
        _storeAddresses[_storeSlot] = address;
        _storeSlot = (_storeSlot + 1) & 7;
        Invalidate(_instructions, address);
        if (_parameters.RetainStoredOperands)
        {
            if (Array.IndexOf(_operands, address) < 0)
            {
                _operands[_operandSlot] = address;
                _operandSlot = (_operandSlot + 1) % _operands.Length;
            }
        }
        else Invalidate(_operands, address);
        return _control + HitNanoseconds;
    }

    private static void Invalidate(uint[] buffer, uint address)
    {
        int index = Array.IndexOf(buffer, address);
        if (index >= 0) buffer[index] = uint.MaxValue;
    }

    private ulong Reserve(uint address, bool write)
    {
        int bank = (int)(address & 7);
        ulong start = Math.Max(_control, _banks[bank]);
        _banks[bank] = start + 2000;
        return start + (write ? 2000UL : 900UL);
    }

    internal void WaitForArithmetic() => _control = Math.Max(_control, _arithmetic);

    internal void Execute(ulong controlDuration, ulong arithmeticDuration, ulong operandReady)
    {
        ulong issue = _control;
        _control = _parameters.OperandTransfersOverlapControl && arithmeticDuration != 0 ?
            issue + controlDuration : Math.Max(issue + controlDuration, operandReady);
        if (arithmeticDuration != 0)
            _arithmetic = Math.Max(Math.Max(issue, operandReady), _arithmetic) + arithmeticDuration;
    }

    internal void Service(ulong duration)
    {
        WaitForArithmetic();
        _control += duration;
        _arithmetic = _control;
        // Hosted OS services can replace arbitrary guest code/data, outside the
        // CPU store path. Forget shadow buffers at every service boundary.
        Array.Fill(_instructions, uint.MaxValue);
        Array.Fill(_operands, uint.MaxValue);
    }
}

/// <summary>Explicit statistical overlap parameters, calibrated independently of
/// functional execution. A factor applies to UU throughput, not book AU latency.</summary>
internal sealed record HostedTimingParameters(double ControlFactor = 1,
    ulong InstructionFetchOverlapNanoseconds = 0, double MathFactor = 1,
    bool OperandTransfersOverlapControl = false, bool UseUnnormalizedMinimum = false,
    bool RetainStoredOperands = false, int OperandBufferCapacity = 8, double ModelTimeFactor = 1)
{
    internal static double PositiveFinite(double value, string name) =>
        double.IsFinite(value) && value > 0 ? value :
            throw new ArgumentOutOfRangeException(name, "Timing coefficient must be finite and positive.");
    // Empirical fit to the cited 2017 Whetstone total and combined N5/N8 time.
    // This is calibration evidence, not independent historical validation.
    internal static HostedTimingParameters Default { get; } = new(
        InstructionFetchOverlapNanoseconds: 900, MathFactor: 1.769005,
        OperandTransfersOverlapControl: true, UseUnnormalizedMinimum: true,
        RetainStoredOperands: true, ModelTimeFactor: 0.670839);
}

/// <summary>
/// Initial statistical software-service estimates, in nanoseconds. These are
/// assumptions, not measured timings or a reconstruction of Dubna microcode.
/// See docs/original-timing.md for provenance, uncertainty and validation scope.
/// </summary>
internal static class HostedMathTiming
{
    internal static ulong Nanoseconds(Opcode opcode, uint address)
    {
        int function = (uint)opcode switch
        {
            0x28 when address <= 7 => (int)address,
            0x29 when address <= 1 => address == 0 ? 1 : 2,
            0x2a when address == 0 => 2,
            0x2b when address == 0 => 3,
            0x2c when address == 0 => 4,
            0x2d when address == 0 => 5,
            0x2e when address == 0 => 6,
            _ => -1,
        };
        return function switch
        {
            0 => 40_000, // SQRT
            1 or 2 => 50_000, // SIN/COS
            3 => 80_000, // ATAN
            4 => 120_000, // ASIN
            5 => 80_000, // LOG
            6 => 60_000, // EXP
            _ => 5_000, // FLOOR and other hosted OS services: legacy estimate.
        };
    }
}
