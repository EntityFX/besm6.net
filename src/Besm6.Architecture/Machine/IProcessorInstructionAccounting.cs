using Besm6.Architecture.Isa;

namespace Besm6.Architecture.Machine
{
    public interface IProcessorInstructionAccounting
    {
        bool AtCycleLimit { get; }
        long CycleLimit { get; set; }
        bool HasPending { get; }
        long TotalCycles { get; }
        long TotalInstructions { get; }

        void Begin(Opcode opcode);
        void CancelPending();
        void Complete();
    }
}