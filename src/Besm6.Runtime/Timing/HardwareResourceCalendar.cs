namespace Besm6.Runtime.Timing;

public enum HardwareResourceKind
{
    ControlUnit,
    ArithmeticUnit, // АУ — арифметическое устройство.
    MemoryBank, // Банк МОЗУ — магнитного оперативного запоминающего устройства.
    CommandBuffer,
    WriteBuffer,
    IoChannel
}
public readonly record struct HardwareResource(HardwareResourceKind Kind, int Index = 0);
public readonly record struct HardwareReservation(HardwareInstant Start, HardwareInstant Finish);

/// <summary>
/// Exclusive-resource reservation primitive. Resources and durations must be
/// supplied explicitly: it defines no BESM-6 arbitration, bank wiring, overlap
/// policy, or data-ready latency. Reservations have no cancellation semantics.
/// </summary>
public sealed class HardwareResourceCalendar
{
    private readonly Dictionary<HardwareResource, HardwareInstant> _available = new();

    public HardwareResourceCalendar(IEnumerable<HardwareResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var resource in resources)
        {
            if (!Enum.IsDefined(resource.Kind) || resource.Index < 0)
                throw new ArgumentOutOfRangeException(nameof(resources));
            if (!_available.TryAdd(resource, default))
                throw new ArgumentException("Duplicate hardware resource.", nameof(resources));
        }
    }

    public HardwareInstant AvailableAt(HardwareResource resource) => _available.TryGetValue(resource, out var time)
        ? time : throw new ArgumentException("Resource is not registered.", nameof(resource));

    public HardwareReservation Reserve(HardwareResource resource, HardwareInstant earliestStart,
        HardwareDuration busyDuration)
    {
        var available = AvailableAt(resource);
        var start = new HardwareInstant(Math.Max(available.Nanoseconds, earliestStart.Nanoseconds));
        var finish = start + busyDuration; // Validate overflow before changing the resource.
        _available[resource] = finish;
        return new(start, finish);
    }
}
