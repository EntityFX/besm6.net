namespace Besm6.Core;

/// <summary>Architectural address-comparison signal, independent of debugger hooks.</summary>
public sealed class HardwareWatchException(ulong signal) : Exception("Hardware address comparison matched.")
{
    public ulong Signal { get; } = signal;
}
