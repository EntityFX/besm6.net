using System.Numerics;

namespace Besm6.Architecture;

/// <summary>
/// The 48 payload bits and two control bits described in TO-8 (1967), §4.5–4.8.
/// BESM bit 50 controls the left half; bit 49 controls the right half.
/// </summary>
public readonly record struct MemoryWord50
{
    public const ulong Mask50 = (1UL << 50) - 1;
    private const ulong HalfMask = (1UL << 24) - 1;

    public ulong RawValue { get; }
    public Word48 Data => new(RawValue);
    public bool LeftControlBit => (RawValue & (1UL << 49)) != 0;
    public bool RightControlBit => (RawValue & (1UL << 48)) != 0;

    /// <summary>Preserves supplied control bits, including intentionally bad control.</summary>
    public MemoryWord50(ulong rawValue)
    {
        if (rawValue > Mask50)
            throw new ArgumentOutOfRangeException(nameof(rawValue));
        RawValue = rawValue;
    }

    /// <summary>
    /// Forms control bits from PKL/PKP. Both false forms a command word;
    /// both true forms a number. Mixed values form bad operand control (§4.8).
    /// </summary>
    public static MemoryWord50 Form(Word48 data, bool invertLeft, bool invertRight)
    {
        ulong left = (ulong)(BitOperations.PopCount(data.Value >> 24) & 1);
        ulong right = (ulong)(1 ^ (BitOperations.PopCount(data.Value & HalfMask) & 1));
        if (invertLeft) left ^= 1;
        if (invertRight) right ^= 1;
        return new MemoryWord50(data.Value | (left << 49) | (right << 48));
    }

    public bool HasValidInstructionControl(bool rightHalf) => rightHalf
        ? ((BitOperations.PopCount(RawValue & HalfMask) + (RightControlBit ? 1 : 0)) & 1) == 1
        : ((BitOperations.PopCount(Data.Value >> 24) + (LeftControlBit ? 1 : 0)) & 1) == 0;

    public bool HasValidOperandControl => (BitOperations.PopCount(RawValue) & 1) == 1;
}
