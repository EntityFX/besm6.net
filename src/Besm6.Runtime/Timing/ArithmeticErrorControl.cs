namespace Besm6.Runtime.Timing;

internal enum ArithmeticErrorMode
{
    Stop,
    ContinueAndReport,
    ContinueArithmeticErrors,
    InputControlOnly
}

internal readonly record struct ArithmeticErrorPolicy(
    ArithmeticErrorMode Mode, bool StopOnInputControl = true);

[Flags]
internal enum ArithmeticErrorSignals
{
    None = 0,
    Interruption = 1, // УПр — прерывание АУ.
    PositiveOverflow = 2, // АВП — аварийное переполнение.
    InvalidDivisor = 4, // УДО — деление на ненормализованный делитель.
    InputControl = 8
}

internal readonly record struct ArithmeticInterruptionSample(
    HardwareInstant Time, ArithmeticErrorSignals Signals, byte InputSource);

/// <summary>
/// АУ — арифметическое устройство. TO-3 §3.28, sheets64-68: logical error
/// signals and the four completion policies. The caller supplies actual IZOP
/// and control-clear pulses; neither instruction durations nor the eight-cycle
/// general-clear microsequence are inferred. Input checking samples a supplied
/// stable word, not electrical changes of the input bus during folding.
/// </summary>
internal sealed class ArithmeticErrorControl
{
    private readonly HardwareTimeline _timeline;
    private readonly List<HardwareEventToken> _inputChecks = new();
    private readonly List<HardwareEventToken> _releases = new();
    private readonly ulong[] _signalVersions = new ulong[4];
    private HardwareEventToken _permission;
    private bool _inputMismatch;
    private byte _inputSource;
    private byte _latchedInputSource;

    internal ArithmeticErrorPolicy Policy { get; }
    internal HardwareDuration Cycle { get; }
    internal ArithmeticErrorSignals Signals { get; private set; }
    internal bool OperationActive { get; private set; }
    internal bool InputControlPermission { get; private set; }
    internal bool CompletionHeld { get; private set; }
    internal bool BlocksNextOperation => CompletionHeld ||
        (Signals & ArithmeticErrorSignals.Interruption) != 0;
    internal HardwareInstant? InputCheckedAt { get; private set; }
    internal ArithmeticInterruptionSample? LastInterruptSample { get; private set; }

    internal ArithmeticErrorControl(HardwareTimeline timeline, HardwareDuration cycle,
        ArithmeticErrorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle), "A positive even cycle is required.");
        if (!Enum.IsDefined(policy.Mode)) throw new ArgumentOutOfRangeException(nameof(policy));
        _timeline = timeline;
        Cycle = cycle;
        Policy = policy;
    }

    /// <summary>1.5 cycles from input arrival, independently of the SPOP origin.</summary>
    internal void CheckInput(MemoryWord50 word, byte source, bool enabled = true)
    {
        if (source > 15) throw new ArgumentOutOfRangeException(nameof(source));
        var due = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds + Cycle.Nanoseconds / 2));
        bool mismatch = enabled && !word.HasValidOperandControl;
        HardwareEventToken token = default;
        token = _timeline.ScheduleAt(due, () =>
        {
            _inputChecks.Remove(token);
            _inputMismatch = mismatch;
            _inputSource = source;
            InputCheckedAt = _timeline.Now;
            IndicateInputControlIfPermitted();
        });
        _inputChecks.Add(token);
    }

    internal void BeginOperation()
    {
        if (OperationActive || BlocksNextOperation)
            throw new InvalidOperationException("SPOP is blocked by arithmetic error control.");
        var due = _timeline.Now + new HardwareDuration(checked(Cycle.Nanoseconds * 2));
        var token = _timeline.ScheduleAt(due, () =>
        {
            _permission = default;
            InputControlPermission = true;
            IndicateInputControlIfPermitted();
        });
        _permission = token;
        InputControlPermission = false;
        OperationActive = true;
    }

    private void Raise(ArithmeticErrorSignals signals)
    {
        for (int i = 0; i < _signalVersions.Length; i++)
            if ((signals & (ArithmeticErrorSignals)(1 << i)) != 0)
                _signalVersions[i]++;
        Signals |= signals;
    }

    private void IndicateInputControlIfPermitted()
    {
        if (InputControlPermission && _inputMismatch)
        {
            if ((Signals & ArithmeticErrorSignals.InputControl) == 0)
                _latchedInputSource = _inputSource;
            Raise(ArithmeticErrorSignals.InputControl | ArithmeticErrorSignals.Interruption);
        }
    }

    /// <summary>The caller has reached the UDO indication, not host evaluation.</summary>
    internal void IndicateInvalidDivisor()
    {
        if (!OperationActive) throw new InvalidOperationException("UDO requires an active operation.");
        var signals = ArithmeticErrorSignals.InvalidDivisor | ArithmeticErrorSignals.PositiveOverflow;
        if (Policy.Mode != ArithmeticErrorMode.InputControlOnly)
            signals |= ArithmeticErrorSignals.Interruption;
        Raise(signals);
    }

    internal void CompleteOperation(bool positiveOverflow)
    {
        if (!OperationActive) throw new InvalidOperationException("IZOP requires an active operation.");
        bool inputError = (Signals & ArithmeticErrorSignals.InputControl) != 0;
        bool newOverflow = positiveOverflow && !inputError;
        bool interrupted = BlocksNextOperation || newOverflow && Policy.Mode != ArithmeticErrorMode.InputControlOnly;
        bool overflowSignal = newOverflow || (Signals & ArithmeticErrorSignals.PositiveOverflow) != 0;
        bool hold = interrupted && (Policy.Mode == ArithmeticErrorMode.Stop ||
            Policy.Mode == ArithmeticErrorMode.ContinueArithmeticErrors && !overflowSignal && Policy.StopOnInputControl ||
            Policy.Mode == ArithmeticErrorMode.InputControlOnly && inputError && Policy.StopOnInputControl);
        // Compute/register the release before changing completion state.
        if (!hold && (Signals != ArithmeticErrorSignals.None || newOverflow))
        {
            var due = _timeline.Now + Cycle;
            var versions = (ulong[])_signalVersions.Clone();
            if (newOverflow)
            {
                versions[1]++;
                if (Policy.Mode != ArithmeticErrorMode.InputControlOnly) versions[0]++;
            }
            HardwareEventToken token = default;
            token = _timeline.ScheduleAt(due, () =>
            {
                _releases.Remove(token);
                // A previous completion must not erase a newer error indication.
                for (int i = 0; i < versions.Length; i++)
                    if (_signalVersions[i] == versions[i])
                        Signals &= ~(ArithmeticErrorSignals)(1 << i);
            });
            _releases.Add(token);
        }
        if (newOverflow)
        {
            var signals = ArithmeticErrorSignals.PositiveOverflow;
            if (Policy.Mode != ArithmeticErrorMode.InputControlOnly)
                signals |= ArithmeticErrorSignals.Interruption;
            Raise(signals);
        }
        if ((Signals & ArithmeticErrorSignals.Interruption) != 0)
        {
            var outgoing = Signals;
            if (Policy.Mode == ArithmeticErrorMode.InputControlOnly)
                outgoing &= ~(ArithmeticErrorSignals.PositiveOverflow | ArithmeticErrorSignals.InvalidDivisor);
            LastInterruptSample = new(_timeline.Now, outgoing,
                (Signals & ArithmeticErrorSignals.InputControl) != 0 ? _latchedInputSource : _inputSource);
        }
        _timeline.Cancel(_permission);
        _permission = default;
        InputControlPermission = false;
        OperationActive = false;
        CompletionHeld = hold;
        if (!hold) Signals &= ~ArithmeticErrorSignals.InvalidDivisor; // UDO ends at IZOP.
    }

    /// <summary>PSb/UOZ: release held interruption/overflow after the operation ends.</summary>
    internal void ReleaseSingleOperation()
    {
        if (OperationActive || !CompletionHeld)
            throw new InvalidOperationException("PSb requires a held completed operation.");
        Signals = ArithmeticErrorSignals.None;
        CompletionHeld = false;
        // An unchanged bad input can be detected again after the next SPOP.
    }

    /// <summary>
    /// Receives an actual general-clear control pulse at a caller-selected instant.
    /// This is not ResetCpu, a cold-machine reset, or an eight-cycle timer.
    /// </summary>
    internal void ApplyGeneralClearSignal()
    {
        _timeline.Cancel(_permission);
        foreach (var token in _inputChecks) _timeline.Cancel(token);
        foreach (var token in _releases) _timeline.Cancel(token);
        _permission = default;
        _inputChecks.Clear();
        _releases.Clear();
        Signals = ArithmeticErrorSignals.None;
        OperationActive = InputControlPermission = CompletionHeld = _inputMismatch = false;
        _inputSource = _latchedInputSource = 0;
        InputCheckedAt = null;
        // The register receiving the cause is external; its last sample survives.
    }
}
