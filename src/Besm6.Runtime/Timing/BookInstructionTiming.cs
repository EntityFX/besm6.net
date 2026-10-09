namespace Besm6.Runtime.Timing;

/// <summary>Published AU range; average is statistical, never an execution deadline.</summary>
public readonly record struct ArithmeticTimingEnvelope(
    HardwareDuration Minimum, HardwareDuration? Average, HardwareDuration Maximum);

/// <summary>Published UU alternatives retain their printed order; branch mapping is unspecified.</summary>
public readonly record struct BookInstructionTiming(
    HardwareDuration ControlFirst, HardwareDuration? ControlSecond,
    ArithmeticTimingEnvelope? Arithmetic, int PrintedPage);

/// <summary>
/// Saltykov/Makarenko, Programming in Fortran (1976), §4 p21 and appendix1
/// pp247–249, visually verified scans. These are component specifications,
/// not full instruction costs. Unknown opcodes have no invented default.
/// The 110ns unit is specific to this source, not a universal hardware clock.
/// </summary>
public static class BookTimingSpecification
{
    public const uint NanosecondsPerCycle = 110;
    public static HardwareDuration Cycles(ulong cycles) => new(checked(cycles * NanosecondsPerCycle));
    private static ArithmeticTimingEnvelope Au(uint min, uint? averageHalfCycles, uint max) =>
        new(Cycles(min), averageHalfCycles is uint average ? new HardwareDuration(average * 55UL) : null, Cycles(max));

    public static bool TryGet(Opcode opcode, out BookInstructionTiming timing)
    {
        BookInstructionTiming? entry = opcode switch
        {
            Opcode.Atx => new(Cycles(3), null, Au(3, 6, 3), 247),
            Opcode.Stx or Opcode.Xts => new(Cycles(6), null, Au(5, 12, 6), 247),
            Opcode.APlusX or Opcode.AMinusX or Opcode.XMinusA or Opcode.Amx => new(Cycles(3), null, Au(5, 22, 280), 247),
            Opcode.Xta or Opcode.Aex => new(Cycles(3), null, Au(2, 5, 3), 247),
            Opcode.Aax or Opcode.Aox => new(Cycles(3), null, Au(4, 8, 4), 247),
            Opcode.Arx => new(Cycles(3), null, Au(3, 12, 27), 247),
            Opcode.Avx => new(Cycles(3), null, Au(4, 10, 25), 247),
            Opcode.ADivX => new(Cycles(3), null, Au(47, 100, 174), 247),
            Opcode.AMulX => new(Cycles(3), null, Au(15, 36, 162), 247),
            Opcode.Apx or Opcode.Aux => new(Cycles(3), null, Au(53, 106, 53), 247),
            Opcode.Acx => new(Cycles(3), null, Au(54, 112, 78), 247),
            Opcode.Anx => new(Cycles(3), null, Au(7, 64, 78), 247),
            Opcode.EPlusX or Opcode.EMinusX => new(Cycles(3), null, Au(3, 10, 133), 247),
            Opcode.Asx => new(Cycles(3), null, Au(4, null, 68), 247),
            Opcode.Xtr or Opcode.Ntr => new(Cycles(3), null, Au(2, 5, 3), 248),
            Opcode.Rte => new(Cycles(3), null, Au(2, 6, 3), 248),
            Opcode.Yta or Opcode.EPlusN or Opcode.EMinusN => new(Cycles(3), null, Au(3, 10, 133), 248),
            Opcode.Asn => new(Cycles(3), null, Au(4, null, 68), 248),
            Opcode.Ati or Opcode.Sti => new(Cycles(14), null, Au(3, 6, 3), 248),
            Opcode.Ita => new(Cycles(6), null, Au(3, 6, 3), 248),
            Opcode.Its => new(Cycles(9), null, Au(5, 12, 6), 248),
            Opcode.Mtj or Opcode.JPlusM => new(Cycles(6), null, null, 248),
            Opcode.Utc => new(Cycles(4), null, null, 248),
            Opcode.Wtc => new(Cycles(13), null, Au(3, 6, 3), 248),
            Opcode.Vtm or Opcode.Utm => new(Cycles(4), null, null, 249),
            Opcode.Uza or Opcode.U1a => new(Cycles(15), Cycles(12), Au(3, 6, 3), 249),
            Opcode.Uj or Opcode.Vjm or Opcode.Ij => new(Cycles(7), null, null, 249),
            Opcode.Vzm or Opcode.V1m or Opcode.Vlm => new(Cycles(7), Cycles(4), null, 249),
            _ => null
        };
        timing = entry.GetValueOrDefault();
        return entry.HasValue;
    }
}
