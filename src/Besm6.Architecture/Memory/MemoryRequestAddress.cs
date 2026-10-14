using Besm6.Architecture.Machine;

namespace Besm6.Architecture.Memory;

/// <summary>
/// Address at the memory-request boundary: 15 address bits and the physical-address
/// marker (bit 16), documented for RAZ, SchAS and VRAM in TO-2, sheets 94–95.
/// This value does not define the hardware comparison keys in BAZ/BAS.
/// </summary>
public readonly record struct MemoryRequestAddress
{
    public uint Address { get; }
    public bool IsPhysical { get; }
    public uint EncodedValue => Address | (IsPhysical ? 0x8000u : 0u);

    public MemoryRequestAddress(uint address, bool isPhysical)
    {
        if (address >= ArchitectureConstants.WordCount)
            throw new ArgumentOutOfRangeException(nameof(address));
        Address = address;
        IsPhysical = isPhysical;
    }
}
