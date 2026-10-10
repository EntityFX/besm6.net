namespace Besm6.Core;

/// <summary>A hardware stop control prevented entry to the internal interrupt vector.</summary>
public sealed class SupervisorHaltException : Exception
{
    public ulong Signal { get; }
    public SupervisorHaltException(ulong signal) : base("Supervisor CPU stopped on an internal hardware signal.") => Signal = signal;
}
