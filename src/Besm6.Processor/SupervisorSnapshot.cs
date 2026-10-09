namespace Besm6.Core;

/// <summary>Independent host observation, including state hidden from guest reads.</summary>
public sealed record SupervisorSnapshot(
    ControlUnitStatus Status,
    SupervisorMode Mode,
    ulong InternalInterrupts,
    uint[] SpecialRegisters,
    bool ArithmeticStopBlocked,
    bool PanelStopEnabled,
    bool InstructionWatchActive,
    bool OperandWatchActive,
    int InstructionWatchDelay);
