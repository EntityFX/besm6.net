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
            ControlDurationsNs = Enum.GetValues<Opcode>().ToDictionary(
                opcode => opcode.ToString(), opcode => (ulong)Besm6Timing.CyclesOf(opcode) * 100),
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
}
