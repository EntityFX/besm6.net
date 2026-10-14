using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Runtime.Devices;

namespace Besm6.Runtime.Extracodes;
/// <summary>Numeric extracodes and their E50 ABI, including persistent parser position.</summary>
internal sealed partial class HostedMathService(IRegisterOps cpu, IHostMemory memory, Plotter plotter, HostedClockService clock)
{
    private readonly IRegisterOps _cpu = cpu;
    private readonly IHostMemory _memory = memory;
    private readonly Plotter _plotter = plotter;
    private readonly HostedClockService _clock = clock;
    private const int M16 = 14;
}
