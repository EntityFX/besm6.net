using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Runtime.Modeling;

internal readonly record struct HardwareRunOutcome(HardwareRunBoundary Boundary, ulong Completed,
    HardwareInstant Time, HardwareInstructionOutcome? Instruction);
