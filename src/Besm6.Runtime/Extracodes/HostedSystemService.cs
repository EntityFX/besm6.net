using Besm6.Architecture.Debug;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;

namespace Besm6.Runtime.Extracodes;
/// <summary>Hosted OS queries, watchpoint configuration and command stores.</summary>
internal sealed partial class HostedSystemService(IRegisterOps cpu, IHostMemory memory, IProcessorDebug debug, ICommandMemory commands, Action enableIntercept)
{
    private readonly IRegisterOps _cpu = cpu;
    private readonly IHostMemory _memory = memory;
    private readonly IProcessorDebug _debug = debug;
    private readonly ICommandMemory _commands = commands;
    private readonly Action _enableIntercept = enableIntercept;
    private const int M16 = 14;
}
