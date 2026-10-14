using Besm6.Architecture.Isa;
using Besm6.Processor.Memory;
using Besm6.Runtime.Modeling;
using Besm6.Runtime.Timing.Calendar;
using Besm6.Runtime.Timing.Devices;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Runtime.Machine;

public enum MachineExecutionModel { Functional, Hardware }

/// <summary>Explicit logical UU (УУ), AU (АУ), MRAM (МОЗУ) and device deadlines.
/// No omitted variable instruction duration is inferred from an average table.</summary>
public sealed class HardwareMachineConfiguration
{
    internal HardwareExecutionConfiguration Execution { get; }
    public MramPortConfiguration Memory { get; }
    public SupervisorIoHardwareTiming Devices { get; }

    public HardwareMachineConfiguration(MramPortConfiguration memory, HardwareDuration bufferHit,
        HardwareDuration arithmeticCycle, IReadOnlyDictionary<Opcode, HardwareDuration> controlDurations,
        SupervisorIoHardwareTiming devices, IReadOnlyDictionary<Opcode, HardwareDuration>? arithmeticDurations = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        _ = new MramReadTiming(MemoryConfiguration.Classical32K, memory.BankCycle, memory.ReadLatency, memory.Banking);
        if (!Enum.IsDefined(memory.ReadSampling) || memory.Arbitration != MramArbitration.FifoPerBank ||
            memory.WriteVisibility.Nanoseconds == 0 || memory.WriteVisibility.Nanoseconds > memory.BankCycle.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(memory));
        Execution = new(memory, bufferHit, arithmeticCycle, controlDurations, arithmeticDurations);
        Memory = memory; Devices = devices;
    }
}
