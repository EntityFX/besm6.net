using Besm6.Architecture.Debug;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.State;

namespace Besm6.Processor.Cpu
{
    /// <summary>
    /// Изменяемые архитектурные состояния процессора.
    /// </summary>
    internal sealed class ProcessorState : IProcessorState
    {
        public uint K { get; set; }
        public Word48 A { get; set; }
        public Word48 Y { get; set; }
        public uint[] M { get; set; } = new uint[ArchitectureConstants.IndexRegCount];
        public uint C { get; set; }
        public uint R { get; set; }
        public int InterceptCount { get; set; }
        public uint InterceptAddress { get; set; } = 16;
        public bool IsRightHalf { get; set; }
        public bool ApplyC { get; set; }
        public int StackCorrection { get; set; }
        public uint RawInstruction { get; set; }
        public uint EffectiveAddress { get; set; }
        public uint PreviousDebugAbort
        {
            get; set;
        }
        public DebugState DebugState { get; } = new();

        public bool IsAdditive => (R & (uint)RFlags.Add) != 0;
        public bool IsMultiplicative =>
            (R & ((uint)RFlags.Add | (uint)RFlags.Mult)) == (uint)RFlags.Mult;
        public bool IsLogical => (R & (uint)RFlags.Mode) == (uint)RFlags.Log;

        public void SetAdditive() =>
            R = (R & ~(uint)RFlags.Mode) | (uint)RFlags.Add;

        public void SetMultiplicative() =>
            R = (R & ~(uint)RFlags.Mode) | (uint)RFlags.Mult;

        public void SetLogical() =>
            R = (R & ~(uint)RFlags.Mode) | (uint)RFlags.Log;

        public void Reset()
        {
            K = 1;
            A = Word48.Zero;
            Y = Word48.Zero;
            Array.Clear(M);
            C = 0;
            R = 0;
            InterceptCount = 0;
            IsRightHalf = false;
            ApplyC = false;
            StackCorrection = 0;
            RawInstruction = 0;
            EffectiveAddress = 0;
            DebugState.DebugFetchArmed = false;
            DebugState.DebugMemoryArmed = false;
            DebugState.DebugWatchSuppressed = false;
            PreviousDebugAbort = 0;
        }
    }
}
