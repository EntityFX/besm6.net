namespace Besm6.Architecture.Machine;

/// <summary>Guest-visible access of a register in the current model.</summary>
public enum MachineRegisterAccess
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
    HostOnly
}
