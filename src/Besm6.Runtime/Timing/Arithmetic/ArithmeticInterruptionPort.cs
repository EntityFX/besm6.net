namespace Besm6.Runtime.Timing;

/// <summary>
/// АУ/УУ — arithmetic/control units. TO-3 §3.28 delivers the stable IZOP cause
/// to the interrupt register; TO-8 §6.12 assigns bits21–23 and input-source1–4.
/// This port stores causes, never changes K/M23/M27 or starts the CPU in a callback.
/// The shared supervisor register uses its existing OR-latch semantics. Detailed
/// simultaneous-source bus priority belongs to the future overlapped UU model.
/// </summary>
internal sealed class ArithmeticInterruptionPort(SupervisorControl? supervisor)
{
    internal ulong PendingCause { get; private set; }
    internal ulong ReceivedSamples { get; private set; }
    internal ArithmeticInterruptionSample? LastSample { get; private set; }

    internal static ulong Encode(ArithmeticInterruptionSample sample)
    {
        const ArithmeticErrorSignals known = ArithmeticErrorSignals.Interruption |
            ArithmeticErrorSignals.PositiveOverflow | ArithmeticErrorSignals.InvalidDivisor | ArithmeticErrorSignals.InputControl;
        if ((sample.Signals & ~known) != 0 || sample.InputSource > 15)
            throw new ArgumentOutOfRangeException(nameof(sample));
        if ((sample.Signals & ArithmeticErrorSignals.Interruption) == 0) return 0;
        ulong cause = 1UL << 20;
        if ((sample.Signals & ArithmeticErrorSignals.PositiveOverflow) != 0) cause |= 1UL << 21;
        if ((sample.Signals & ArithmeticErrorSignals.InvalidDivisor) != 0) cause |= 1UL << 22;
        if ((sample.Signals & ArithmeticErrorSignals.InputControl) != 0) cause |= sample.InputSource;
        return cause;
    }

    internal void Receive(ArithmeticInterruptionSample sample)
    {
        ulong cause = Encode(sample);
        if (cause == 0) return;
        supervisor?.LatchInternalCause(cause);
        PendingCause |= cause;
        LastSample = sample;
        ReceivedSamples++;
    }

    // Acknowledge the delivery boundary, not the guest-owned register or AU clear.
    internal void Acknowledge() => PendingCause = 0;
}
