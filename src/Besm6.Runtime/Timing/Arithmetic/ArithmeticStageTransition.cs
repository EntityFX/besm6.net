using Besm6.Architecture.Machine;
using Besm6.Processor.Arithmetic;
using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Runtime.Timing.Arithmetic;

internal readonly record struct ArithmeticStageTransition(ArithmeticCommandHandle Command,
    uint Code, ArithmeticStageTransitionKind Kind, HardwareInstant Time,
    NormalizedArithmeticResult? Output, ArithmeticUnitFault? Fault);
