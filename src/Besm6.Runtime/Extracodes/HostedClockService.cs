namespace Besm6.Runtime.Extracodes;
/// <summary>Hosted DATE and CTIME policies, independent of numeric parsing and printing.</summary>
internal sealed partial class HostedClockService
{
    internal bool UseWallClock { get; set; }
    internal Func<double>? GuestTimeSeconds { get; set; }
}
