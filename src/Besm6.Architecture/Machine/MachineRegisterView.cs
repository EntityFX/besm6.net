namespace Besm6.Architecture.Machine;

/// <summary>
/// Named host observation of one architectural register. The snapshot is a copy
/// and is created only at an inspection boundary; execution never walks this list.
/// </summary>
public sealed record MachineRegisterView(
    string Name,
    string Group,
    int Width,
    ulong Value,
    MachineRegisterAccess Access,
    string? Address = null,
    string? Alias = null,
    string? Detail = null);
