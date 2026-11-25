namespace Besm6.Runtime;

/// <summary>Independent host observation of one buffer register slot.</summary>
public sealed record BufferRegisterView(
    string Name,
    int Slot,
    ulong Value,
    uint? Address,
    bool? Active,
    bool? Ready,
    bool? PendingWrite = null,
    ulong? Raw50 = null);
