namespace Besm6.Runtime.Timing;

/// <summary>
/// TO-3 edition1-65, §3.7, sheet37: an already-ready BRUS operand takes
/// three cycles to request/accept; after the waiting stage is released by
/// GBRCh, acceptance follows after one and a half cycles. These are distinct
/// phase origins, not a whole-instruction duration or a memory-bank latency.
/// The caller selects the documented phase and supplies its clock quantum.
/// </summary>
public sealed class ArithmeticBufferTransferTiming
{
    public HardwareDuration Cycle { get; }
    public ArithmeticBufferTransferTiming(HardwareDuration cycle)
    {
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle),
                "A positive even nanosecond quantum is required to represent half cycles exactly.");
        Cycle = cycle;
    }

    public HardwareInstant AcceptFromReadyBuffer(HardwareInstant requestIssued) =>
        requestIssued + new HardwareDuration(checked(Cycle.Nanoseconds * 3));

    public HardwareInstant AcceptAfterBufferWait(HardwareInstant readySignal) =>
        readySignal + new HardwareDuration(checked(Cycle.Nanoseconds + Cycle.Nanoseconds / 2));
}
