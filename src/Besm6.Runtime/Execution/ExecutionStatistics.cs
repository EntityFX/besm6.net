namespace Besm6.Runtime;

/// <summary>Execution-loop measurements, excluding CLI startup, asset checks and report formatting.</summary>
public sealed record ExecutionStatistics(
    ExecutionSpeed Speed,
    long CompletedInstructions,
    long? ModelCycles,
    double ElapsedSeconds,
    long AllocatedBytes,
    int Gen0Collections,
    double? HardwareModelSeconds = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    double? StatisticalModelSeconds = null)
{
    public double? ModelSeconds => StatisticalModelSeconds ?? HardwareModelSeconds ?? (ModelCycles is long cycles ? ExecutionPacer.SecondsOf(cycles) : null);
    public double InstructionsPerSecond => ElapsedSeconds > 0 ? CompletedInstructions / ElapsedSeconds : 0;
    public double? ModelToWallRatio => ElapsedSeconds > 0 && ModelSeconds is double seconds ? seconds / ElapsedSeconds : null;
    public bool OriginalTempoMet => Speed != ExecutionSpeed.Original ||
        ModelSeconds is double seconds && ElapsedSeconds - seconds <= Math.Max(0.020, seconds * 0.02);
}
