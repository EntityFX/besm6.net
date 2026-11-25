using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;

namespace Besm6.Runtime.Machine;

/// <summary>Host-independent run settings. Applying them to Config keeps CLI, TUI and Web
/// on the same machine/loader construction path, including the zero-limit rule.</summary>
public sealed record RunSessionSettings(
    ExecutionSpeed Speed,
    long InstructionLimit,
    bool GuestClock = false,
    bool BufferedMemory = false,
    bool SupervisorHostedServices = false,
    long WallClockLimitMs = 0,
    bool CollectStatistics = false,
    bool? UseWallClock = null,
    string? TapesDirectory = null,
    string? DiskPath = null,
    string? DrumPath = null,
    string? HostFileDirectory = null)
{
    public static RunSessionSettings FromConfig(Config config) => new(
        config.Speed, config.DefaultLimit, config.GuestClock, config.BufferedMemory,
        config.SupervisorHostedServices);
}

/// <summary>One machine and its loader, constructed by the runtime rather than by a host.</summary>
public sealed class MachineRunSession
{
    internal MachineRunSession(MachineCore machine, DubnaLoader loader)
    {
        Machine = machine;
        Loader = loader;
    }

    public MachineCore Machine { get; }
    public DubnaLoader Loader { get; }
}
