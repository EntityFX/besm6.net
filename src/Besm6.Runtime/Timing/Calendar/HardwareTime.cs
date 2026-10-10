namespace Besm6.Runtime.Timing;

/// <summary>A duration in integer nanoseconds, independent of legacy instruction ticks.</summary>
public readonly record struct HardwareDuration(ulong Nanoseconds)
{
    public static HardwareDuration operator +(HardwareDuration left, HardwareDuration right) =>
        new(checked(left.Nanoseconds + right.Nanoseconds));
}

/// <summary>An absolute instant in the separate hardware timeline.</summary>
public readonly record struct HardwareInstant(ulong Nanoseconds)
{
    public static HardwareInstant operator +(HardwareInstant instant, HardwareDuration duration) =>
        new(checked(instant.Nanoseconds + duration.Nanoseconds));

    public HardwareDuration ElapsedSince(HardwareInstant earlier) =>
        earlier.Nanoseconds <= Nanoseconds ? new(Nanoseconds - earlier.Nanoseconds) :
            throw new ArgumentOutOfRangeException(nameof(earlier), "An elapsed duration cannot be negative.");
}
