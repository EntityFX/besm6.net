using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Runtime.Devices;

namespace Besm6.Runtime.Extracodes;
/// <summary>Terminal, plotter and punched-card services with an explicit print flush boundary.</summary>
internal sealed partial class HostedTerminalService(IRegisterOps cpu, IHostMemory memory, Plotter plotter, Puncher puncher, Action<string> output, Func<string, string> input, Action finishPrint)
{
    private readonly IRegisterOps _cpu = cpu;
    private readonly IHostMemory _memory = memory;
    private readonly Plotter _plotter = plotter;
    private readonly Puncher _puncher = puncher;
    private readonly Action<string> _output = output;
    private readonly Func<string, string> _input = input;
    private readonly Action _finishPrint = finishPrint;
    private const int M16 = 14;
}
