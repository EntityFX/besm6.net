using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Runtime.Timing.Devices;

/// <summary>
/// Caller-selected logical device delays, in nanoseconds. They are independent
/// of instruction ticks and do not assert historical peripheral measurements.
/// A zero DMA issue interval explicitly selects a burst FIFO request policy.
/// </summary>
public sealed class SupervisorIoHardwareTiming
{
    public HardwareDuration ExchangeReady { get; }
    public HardwareDuration ConsoleReply { get; }
    public HardwareDuration PaperFeed { get; }
    public HardwareDuration PrinterCharacter { get; }
    public HardwareDuration PrinterZero { get; }
    public HardwareDuration TimerPeriod { get; }
    public HardwareDuration SerialPeriod { get; }
    public HardwareDuration DmaIssueInterval { get; }

    public SupervisorIoHardwareTiming(HardwareDuration exchangeReady, HardwareDuration consoleReply,
        HardwareDuration paperFeed, HardwareDuration printerCharacter, HardwareDuration printerZero,
        HardwareDuration timerPeriod, HardwareDuration serialPeriod, HardwareDuration dmaIssueInterval)
    {
        foreach (var duration in new[] { exchangeReady, consoleReply, paperFeed, printerCharacter,
            printerZero, timerPeriod, serialPeriod })
            if (duration.Nanoseconds == 0) throw new ArgumentException("Device periods must be positive.");
        ExchangeReady = exchangeReady; ConsoleReply = consoleReply; PaperFeed = paperFeed;
        PrinterCharacter = printerCharacter; PrinterZero = printerZero; TimerPeriod = timerPeriod;
        SerialPeriod = serialPeriod; DmaIssueInterval = dmaIssueInterval;
    }
}
