using Besm6.Architecture.Memory;
namespace Besm6.Processor.Memory;

/// <summary>Latched signal for a future hardware interrupt controller; not a Dubna avost.</summary>
public readonly record struct MemoryFault(MemoryRequestAddress Request, MemoryAccessKind Access,
    MemoryFaultSource Source, uint? PhysicalAddress);
