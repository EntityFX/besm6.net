namespace Besm6.Runtime.Timing;

/// <summary>
/// АУ — арифметическое устройство. Logical SPOP→IZOP interval: TO-3 §3.2
/// defines the start/end signals; table1.1 gives four cycles for AND/OR and
/// 53 cycles for packing/unpacking,
/// with equal minimum/maximum. No average or early RPK instant is inferred.
/// </summary>
internal static class ArithmeticFixedCompletionTiming
{
    internal static HardwareDuration Duration(Opcode opcode, HardwareDuration cycle)
    {
        if (cycle.Nanoseconds == 0 || (cycle.Nanoseconds & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(cycle));
        ulong cycles = opcode switch
        {
            Opcode.Aax or Opcode.Aox => 4,
            Opcode.Apx or Opcode.Aux => 53,
            _ => throw new ArgumentException("This operation has no implemented fixed SPOP-to-IZOP sequence.", nameof(opcode))
        };
        return new(checked(cycle.Nanoseconds * cycles));
    }
}
