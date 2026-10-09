namespace Besm6.Core;

public enum MemoryAccessKind1967
{
    InstructionLeft,
    InstructionRight,
    OperandRead,
    OperandWrite
}

/// <summary>A memory control signal, not yet wired to hardware interrupt handling.</summary>
public sealed class MemoryControlException : Exception
{
    public uint PhysicalAddress { get; }
    public MemoryAccessKind1967 AccessKind { get; }

    public MemoryControlException(uint physicalAddress, MemoryAccessKind1967 accessKind)
        : base($"Memory control failure at physical address {physicalAddress}, access {accessKind}")
    {
        PhysicalAddress = physicalAddress;
        AccessKind = accessKind;
    }
}

/// <summary>A page protection signal, separate from Dubna's guest faults.</summary>
public sealed class MemoryProtection1967Exception : Exception
{
    public uint MathematicalAddress { get; }
    public uint MathematicalPage => MathematicalAddress >> 10;
    public MemoryAccessKind1967 AccessKind { get; }

    public MemoryProtection1967Exception(uint mathematicalAddress, MemoryAccessKind1967 accessKind)
        : base($"Memory protection at mathematical address {mathematicalAddress}, access {accessKind}")
    {
        MathematicalAddress = mathematicalAddress;
        AccessKind = accessKind;
    }
}
