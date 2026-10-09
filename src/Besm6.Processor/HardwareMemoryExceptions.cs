namespace Besm6.Core;

public enum MemoryAccessKind
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
    public MemoryAccessKind AccessKind { get; }

    public MemoryControlException(uint physicalAddress, MemoryAccessKind accessKind)
        : base($"Memory control failure at physical address {physicalAddress}, access {accessKind}")
    {
        PhysicalAddress = physicalAddress;
        AccessKind = accessKind;
    }
}

/// <summary>A page protection signal, separate from Dubna's guest faults.</summary>
public sealed class MemoryProtectionException : Exception
{
    public uint MathematicalAddress { get; }
    public uint MathematicalPage => MathematicalAddress >> 10;
    public MemoryAccessKind AccessKind { get; }

    public MemoryProtectionException(uint mathematicalAddress, MemoryAccessKind accessKind)
        : base($"Memory protection at mathematical address {mathematicalAddress}, access {accessKind}")
    {
        MathematicalAddress = mathematicalAddress;
        AccessKind = accessKind;
    }
}
