using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.State;

namespace Besm6.Architecture.Supervisor
{
    public interface ISupervisorControl
    {
        bool ArithmeticStopBlocked { get; }
        ulong InternalInterrupts { get; }
        ISupervisorIo? Io { get; set; }
        bool IsSupervisor { get; }
        SupervisorMode Mode { get; set; }
        bool PanelStopEnabled { get; set; }
        uint SavedFlags { get; }
        ControlUnitStatus Status { get; set; }

        void CheckInstructionAccess(MemoryRequestAddress request);
        void CheckOperandAccess(MemoryRequestAddress request, bool write);
        void CompletedInstruction();
        void EnterExternal();
        void EnterExtracode(uint opcode, uint effectiveAddress, uint returnWord);
        void EnterInternal(ulong interruptFlags, uint returnWord, uint savedFlags);
        uint GetComparisonAddress(int register);
        uint ReadModifier(int register);
        Word48 ReadRegister(uint address);
        void ResetCpu();
        void ReturnFromInterrupt(uint instructionRegister);
        SupervisorSnapshot Snapshot();
        void WriteModifier(int register, uint value);
        void WriteRegister(uint address, Word48 value);

        void AcceptInternalInterrupt(ulong signal, uint returnWord, uint savedFlags);
        void LatchInternalCause(ulong interruptFlags);
    }
}
