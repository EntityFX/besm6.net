using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Cpu;

/// <summary>Live observation that cannot be cast to a register editor.</summary>
internal sealed class ProcessorStateView(IMutableProcessorState state) : IProcessorState
{
    public uint K => state.K;
    public Word48 A => state.A;
    public Word48 Y => state.Y;
    public uint R => state.R;
    public ReadOnlySpan<uint> M => state.M;
    public uint C => state.C;
    public bool ApplyC => state.ApplyC;
    public uint EffectiveAddress => state.EffectiveAddress;
    public bool IsRightHalf => state.IsRightHalf;
    public int InterceptCount => state.InterceptCount;
    public uint InterceptAddress => state.InterceptAddress;
}
