using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;

namespace Besm6.Runtime.Extracodes;
/// <summary>Owns GOST line buffering, overprints and formatting for E64.</summary>
internal sealed partial class HostedPrintService
{
    private readonly IRegisterOps _cpu;
    private readonly IHostMemory _memory;
    private readonly Action<string> _output;
    private const int M16 = 14;
    internal HostedPrintService(IRegisterOps cpu, IHostMemory memory, Action<string> output)
    {
        _cpu = cpu;
        _memory = memory;
        _output = output;
        Array.Fill(_e64Line, G_SPACE);
    }
}
