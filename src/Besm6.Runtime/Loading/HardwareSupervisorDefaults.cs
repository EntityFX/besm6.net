using System.Globalization;
using Besm6.Architecture.Isa;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Profiling;

namespace Besm6.Runtime.Loading;

/// <summary>
/// Explicit defaults for a supervisor hardware run without a machine JSON.
/// The values mirror examples/machine-timing.json; a .dub program supplies the
/// memory image and entry point through the normal job loader.
/// </summary>
public static class HardwareSupervisorDefaults
{
    public static MachineJobConfiguration Create(ExecutionSpeed speed, long instructionLimit,
        string memoryConfiguration = "classical32k") => new()
    {
        ExecutionModel = "hardware",
        Speed = speed.ToString().ToLowerInvariant(),
        MemoryConfiguration = memoryConfiguration,
        InstructionLimit = instructionLimit,
        ControlFlags = 0x403,
        MemoryWords = [],
        Timing = new()
        {
            BankCycleNs = 2000,
            ReadLatencyNs = 900,
            WriteVisibilityNs = 1500,
            ReadSampling = "cycleStart",
            Banking = "documentedClassical",
            BufferHitNs = 100,
            ArithmeticCycleNs = 100,
            ControlDurationsNs = ControlDurations(),
            ArithmeticDurationsNs = new(),
            Devices = new()
            {
                ExchangeReadyNs = 20000,
                ConsoleReplyNs = 700,
                PaperFeedNs = 900,
                PrinterCharacterNs = 100,
                PrinterZeroNs = 50,
                TimerPeriodNs = 500,
                SerialPeriodNs = 30,
                DmaIssueIntervalNs = 0
            }
        }
    };

    private static Dictionary<string, ulong> ControlDurations()
    {
        var durations = Enum.GetValues<Opcode>().ToDictionary(
            opcode => opcode.ToString(), Cycles);
        // BESM-6 extracodes are numeric opcodes (octal 050–077 and long 0200/0210),
        // not Opcode enum members, so the supervisor also needs explicit numeric keys.
        for (uint code = 0x28; code <= 0x3F; code++)
            durations.Add(code.ToString(CultureInfo.InvariantCulture), Cycles((Opcode)code));
        durations.Add(((uint)0x80).ToString(CultureInfo.InvariantCulture), Cycles((Opcode)0x80));
        durations.Add(((uint)0x88).ToString(CultureInfo.InvariantCulture), Cycles((Opcode)0x88));
        return durations;
    }

    private static ulong Cycles(Opcode opcode) =>
        (ulong)Besm6Timing.CyclesOf(opcode) * Besm6Timing.NanosecondsPerCycle;
}
