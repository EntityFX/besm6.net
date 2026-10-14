using Besm6.Architecture.Debug;
using Besm6.Architecture.State;

namespace Besm6.Architecture.Machine
{
    public interface IProcessorState : IRegisters
    {
        int InterceptCount { get; set; }
        uint InterceptAddress { get; set; }

        bool IsRightHalf { get; set; }

        int StackCorrection { get; set; }
        uint RawInstruction { get; set; }

        bool IsAdditive { get; }
        bool IsMultiplicative { get; }
        bool IsLogical { get; }

        DebugState DebugState { get; }

        uint PreviousDebugAbort
        {
            get; set;
        }

        void SetAdditive();

        void SetMultiplicative();

        void SetLogical();

        void Reset();
    }
}
