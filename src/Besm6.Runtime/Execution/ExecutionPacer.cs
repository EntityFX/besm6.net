using Besm6.Runtime.Profiling;
using System.Diagnostics;

namespace Besm6.Runtime.Execution;

internal interface IExecutionTimeSource
{
    double ElapsedSeconds { get; }
    void Sleep(int milliseconds);
    void Spin();
}

internal sealed class StopwatchExecutionTimeSource(Stopwatch stopwatch) : IExecutionTimeSource
{
    public double ElapsedSeconds => stopwatch.Elapsed.TotalSeconds;
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
    public void Spin() => Thread.SpinWait(32);
}

internal sealed class RelativeExecutionTimeSource(IExecutionTimeSource source, double started) : IExecutionTimeSource
{
    public double ElapsedSeconds => source.ElapsedSeconds - started;
    public void Sleep(int milliseconds) => source.Sleep(milliseconds);
    public void Spin() => source.Spin();
}

/// <summary>Absolute deadlines prevent accumulated sleep error from changing the modeled rate.</summary>
internal sealed class ExecutionPacer(IExecutionTimeSource time)
{
    internal const long CheckpointCycles = 100_000;
    internal static double SecondsOf(long cycles) => cycles * (Besm6Timing.NanosecondsPerCycle / 1_000_000_000.0);

    internal bool Synchronize(long cycles, double? wallLimitSeconds = null)
        => SynchronizeSeconds(SecondsOf(cycles), wallLimitSeconds);

    internal bool SynchronizeSeconds(double target, double? wallLimitSeconds = null)
    {
        if (!double.IsFinite(target) || target < 0) throw new ArgumentOutOfRangeException(nameof(target));
        double deadline = wallLimitSeconds is double limit ? Math.Min(target, limit) : target;
        while (true)
        {
            double remaining = deadline - time.ElapsedSeconds;
            if (remaining <= 0) break;
            if (remaining >= 0.003)
                time.Sleep((int)Math.Min(50, Math.Floor((remaining - 0.002) * 1000)));
            else
                time.Spin();
        }
        return wallLimitSeconds is not double maximum || time.ElapsedSeconds < maximum;
    }
}
