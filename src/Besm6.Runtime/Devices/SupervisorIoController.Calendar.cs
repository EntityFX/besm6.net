using Besm6.Runtime.Timing;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private enum IoEventKind { Exchange, Console, PaperReader, PrinterCharacter, PrinterZero, Timer, Serial }
    private readonly record struct IoEventToken(EventToken? Functional, HardwareEventToken? Hardware);
    private HardwareTimeline? _hardwareTimeline;
    private SupervisorIoHardwareTiming? _hardwareTiming;
    private TimedSupervisorDma? _hardwareDma;
    private int _calendarCallbacks;
    public bool UsesHardwareTiming => _hardwareTimeline is not null;

    private bool HasScheduledIo => _timer.HasValue || _serialClock.HasValue ||
        _drumPending.Any(token => token.HasValue) || _consolePending.Any(token => token.HasValue) ||
        _channels.Any(channel => channel.Pending.HasValue) || _paperReaders.Any(reader => reader.Pending.HasValue) ||
        _printers.Any(printer => printer.Pending.HasValue);

    private void EnsureCalendarSwitchAllowed()
    {
        if (_calendarCallbacks != 0 || HasScheduledIo || (_hardwareDma?.PendingTransfers ?? 0) != 0 ||
            _hardwareDma?.HasPendingMemoryCycles == true || _hardwareTimeline?.IsAdvancing == true ||
            _hardwareTimeline?.AdvancementProhibited == true ||
            _scheduler is EventScheduler { IsAdvancing: true } or EventScheduler { AdvancementProhibited: true })
            throw new InvalidOperationException("Select a device calendar at an idle device boundary.");
    }

    /// <summary>
    /// Change only device scheduling. Attachments, registers and interrupts survive.
    /// The port must address the same physical storage on the same calendar.
    /// This does not select the machine's CPU execution owner.
    /// </summary>
    public void UseHardwareTiming(HardwareTimeline timeline, MramMemoryPort port, SupervisorIoHardwareTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timeline); ArgumentNullException.ThrowIfNull(port); ArgumentNullException.ThrowIfNull(timing);
        EnsureCalendarSwitchAllowed();
        if (timeline.IsAdvancing || timeline.AdvancementProhibited || !ReferenceEquals(port.Timeline, timeline) || !ReferenceEquals(port.Storage, _memory.PhysicalMemory))
            throw new ArgumentException("Device DMA requires shared physical memory and a shared hardware calendar.");
        _hardwareDma = new(port, timing.DmaIssueInterval);
        _hardwareTimeline = timeline; _hardwareTiming = timing;
    }

    public void UseFunctionalTiming()
    {
        EnsureCalendarSwitchAllowed();
        _hardwareTimeline = null; _hardwareTiming = null; _hardwareDma = null;
    }

    public void StartHardwareTimer(bool includeSlowClock = false)
    {
        RequireHardwareTiming();
        // The value is an enabled marker; the selected nanosecond configuration owns the period.
        StartTimerCore(1, includeSlowClock);
    }

    public void StartHardwareSerialClock()
    {
        RequireHardwareTiming(); StartSerialClockCore(1);
    }

    private void RequireHardwareTiming()
    {
        if (!UsesHardwareTiming) throw new InvalidOperationException("Select hardware device timing first.");
    }
    private void RequireFunctionalTiming()
    {
        if (UsesHardwareTiming) throw new InvalidOperationException("Tick periods require functional device timing; use the configured hardware clock entry point.");
    }

    private IoEventToken ScheduleIo(IoEventKind kind, ulong functionalTicks, Action callback)
    {
        void Invoke()
        {
            _calendarCallbacks++;
            try { callback(); }
            finally { _calendarCallbacks--; }
        }
        if (_hardwareTimeline is not { } timeline) return new(_scheduler.Schedule(functionalTicks, Invoke), null);
        var timing = _hardwareTiming!;
        var delay = kind switch
        {
            IoEventKind.Exchange => timing.ExchangeReady,
            IoEventKind.Console => timing.ConsoleReply,
            IoEventKind.PaperReader => timing.PaperFeed,
            IoEventKind.PrinterCharacter => timing.PrinterCharacter,
            IoEventKind.PrinterZero => timing.PrinterZero,
            IoEventKind.Timer => timing.TimerPeriod,
            IoEventKind.Serial => timing.SerialPeriod,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new(null, timeline.Schedule(delay, Invoke));
    }

    private bool CancelIo(IoEventToken token) => token.Hardware is { } hardware ?
        _hardwareTimeline!.Cancel(hardware) : token.Functional is { } functional && _scheduler.Cancel(functional);
}
