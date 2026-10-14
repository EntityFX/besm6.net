using Besm6.Architecture.Machine;
using Besm6.Processor.Memory;
using Besm6.Runtime.Timing.Calendar;

namespace Besm6.Runtime.Timing.Memory;

/// <summary>One accepted read; data return and bank availability are separate instants.</summary>
public readonly record struct MramReadReservation(int Bank, HardwareInstant Started,
    HardwareInstant DataReady, HardwareInstant BankAvailable);

/// <summary>
/// МОЗУ — магнитное оперативное запоминающее устройство; English code name: MRAM.
/// Classical eight-bank read reservation, TO-4 §1.1–1.2, sheet3. The source
/// specifies a minimum2us bank cycle and approximately0.9us data return.
/// No request queue/arbitration, write visibility or CPU integration is inferred.
/// Expanded SIMH storage requires an explicit logical banking choice; its wiring is not inferred.
/// </summary>
public sealed class MramReadTiming
{
    public static HardwareDuration PublishedMinimumCycle => new(2000);
    public static HardwareDuration PublishedApproximateReadLatency => new(900);
    private readonly uint _capacity;
    private readonly HardwareInstant[] _available = new HardwareInstant[8];
    public HardwareDuration BankCycle { get; }
    public HardwareDuration ReadLatency { get; }

    /// <summary>
    /// Latency must be explicitly selected: the published approximate value is
    /// not asserted to be an exact measured deadline. Cycle cannot be shorter
    /// than the published minimum. The caller supplies the source configuration.
    /// </summary>
    public MramReadTiming(MemoryConfiguration configuration, HardwareDuration bankCycle,
        HardwareDuration readLatency, MramBanking banking = MramBanking.DocumentedClassical)
    {
        if (!Enum.IsDefined(banking) || !Enum.IsDefined(configuration))
            throw new ArgumentOutOfRangeException(nameof(banking));
        if (configuration != MemoryConfiguration.Classical32K && banking == MramBanking.DocumentedClassical)
            throw new ArgumentOutOfRangeException(nameof(configuration),
                "Only the documented classical eight-bank geometry has a timing specification.");
        if (bankCycle.Nanoseconds < PublishedMinimumCycle.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(bankCycle));
        if (readLatency.Nanoseconds == 0 || readLatency.Nanoseconds > bankCycle.Nanoseconds)
            throw new ArgumentOutOfRangeException(nameof(readLatency));
        _capacity = configuration == MemoryConfiguration.Classical32K ? ArchitectureConstants.WordCount : 512U * 1024;
        BankCycle = bankCycle;
        ReadLatency = readLatency;
    }

    public HardwareInstant AvailableAt(uint physicalAddress) => _available[BankOf(physicalAddress)];

    /// <summary>
    /// Accepts only an immediately available bank. False does not reserve or
    /// enqueue a request; the UU/arbitration model chooses when to retry.
    /// </summary>
    public bool TryBeginRead(uint physicalAddress, HardwareInstant now, out MramReadReservation reservation)
    {
        int bank = BankOf(physicalAddress);
        reservation = default;
        if (now.Nanoseconds < _available[bank].Nanoseconds) return false;
        var dataReady = now + ReadLatency;
        var available = now + BankCycle; // Validate both instants before mutating the bank.
        reservation = new(bank, now, dataReady, available);
        _available[bank] = available;
        return true;
    }

    private int BankOf(uint address)
    {
        if (address >= _capacity) throw new ArgumentOutOfRangeException(nameof(address));
        return (int)(address & 7);
    }
}
