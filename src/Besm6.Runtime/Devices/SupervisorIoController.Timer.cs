using System.Buffers.Binary;
using System.Numerics;

namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private IoEventToken? _timer;
    private ulong _timerPeriod;
    private bool _includeSlowClock;
    private uint _timerPulses;

    public void StartTimer(ulong periodTicks) => StartTimer(periodTicks, includeSlowClock: false);

    /// <summary>Optional selected-SIMH adapter: fast_clk at pinned revision
    /// 9e318d16a203a3efae3420ea3f5700a8d7e3bec3 raises GRP bit 10 every fourth pulse.
    /// This later undocumented addition is separate from the book configuration;
    /// periodTicks remains a functional scheduling delay, not hardware timing.</summary>
    public void StartTimer(ulong periodTicks, bool includeSlowClock)
    {
        RequireFunctionalTiming();
        StartTimerCore(periodTicks, includeSlowClock);
    }

    private void StartTimerCore(ulong periodTicks, bool includeSlowClock)
    {
        if (periodTicks == 0) throw new ArgumentOutOfRangeException(nameof(periodTicks));
        StopTimer();
        _timerPeriod = periodTicks;
        _includeSlowClock = includeSlowClock;
        _timer = ScheduleIo(IoEventKind.Timer, periodTicks, TimerElapsed);
    }

    public void StopTimer()
    {
        if (_timer is IoEventToken token) CancelIo(token);
        _timer = null;
        _timerPeriod = 0;
        _includeSlowClock = false;
        _timerPulses = 0;
    }

    private void TimerElapsed()
    {
        _timer = null;
        ulong signals = TimerInterrupt;
        if (_includeSlowClock && (++_timerPulses & 3) == 0) signals |= SlowClockInterrupt;
        if (_timerPeriod != 0) _timer = ScheduleIo(IoEventKind.Timer, _timerPeriod, TimerElapsed);
        RaiseInterrupts(signals);
    }

}
