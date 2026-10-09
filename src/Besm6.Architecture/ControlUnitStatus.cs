namespace Besm6.Architecture;

/// <summary>Seven wired controls of M17 (decimal 17, octal 021): TO-8 §7.7, sheet 135.</summary>
[Flags]
public enum ControlUnitFlags : uint
{
    None = 0,
    AssignmentBlocked = 0x001,
    ProtectionBlocked = 0x002,
    StopOnInternalInterrupt = 0x004,
    StopOnControlInterrupt = 0x008,
    MatchWriteAddress = 0x010,
    ExternalInterruptsBlocked = 0x400,
    AutomaticB = 0x800
}

/// <summary>
/// Semantic host snapshot, not a readable guest register or SIMH's artificial RUU.
/// Unwired storage and hardware reset behavior are deliberately not inferred.
/// </summary>
public readonly record struct ControlUnitStatus
{
    public const uint RegisterNumber = 17; // Decimal; distinct from the ordinary octal 017 stack register.
    public const uint KnownMask = 0xC1F;
    public const uint AddressControlMask = 0x403;
    public ControlUnitFlags Flags { get; }
    public uint EncodedControls => (uint)Flags;
    public bool AssignmentBlocked => (Flags & ControlUnitFlags.AssignmentBlocked) != 0;
    public bool ProtectionBlocked => (Flags & ControlUnitFlags.ProtectionBlocked) != 0;

    public ControlUnitStatus(ControlUnitFlags flags)
    {
        if (((uint)flags & ~KnownMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "Unknown host control flag; not a guest hardware fault.");
        Flags = flags;
    }

    /// <summary>
    /// Pure transformation specified for supervisor PA/SA with register 00 (§7.8).
    /// Guest dispatch/mode checking is not implemented by this host operation.
    /// </summary>
    public ControlUnitStatus WithSupervisorAddressControls(uint address)
    {
        if (address > 0x7FFF) throw new ArgumentOutOfRangeException(nameof(address));
        return new((ControlUnitFlags)((EncodedControls & ~AddressControlMask) | (address & AddressControlMask)));
    }
}
