namespace Besm6.Core
{
    /// <summary>
    /// Единственный владелец изменяемого архитектурного состояния процессора.
    /// </summary>
    internal sealed class ProcessorState
    {
        internal uint K;
        internal Word48 A;
        internal Word48 Y;
        internal readonly uint[] M = new uint[ArchitectureConstants.IndexRegCount];
        internal uint C;
        internal uint R;
        internal int InterceptCount;
        internal uint InterceptAddress = 16;
        internal bool IsRightHalf;
        internal bool ApplyC;
        internal int StackCorrection;
        internal uint RawInstruction;
        internal uint EffectiveAddress;
        internal bool DebugFetchArmed;
        internal uint DebugFetchAddress;
        internal uint DebugFetchContinuation;
        internal bool DebugFetchPrintInfo;
        internal bool DebugMemoryArmed;
        internal uint DebugMemoryAddress;
        internal uint DebugMemoryContinuation;
        internal bool DebugMemoryPrintInfo;
        internal uint DebugMemoryMode;
        internal bool DebugWatchSuppressed;
        internal uint PreviousDebugAbort;

        internal void Reset()
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
            DebugFetchArmed = false;
            DebugMemoryArmed = false;
            DebugWatchSuppressed = false;
            PreviousDebugAbort = 0;
        }
    }
}
