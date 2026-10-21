using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Processor.Cpu;
using Besm6.Runtime.Extracodes;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime;
/// <summary>Hosted extracode dispatch; each service owns its own state and host dependencies.</summary>
public sealed class ExtracodeHandler : IDisposable
{
    private readonly IRegisterOps _registers;
    private readonly HostedMathService _math;
    private readonly HostedPrintService _print;
    private readonly HostedTerminalService _terminal;
    private readonly HostedStorageService _storage;
    private readonly HostedSystemService _system;
    private readonly HostedClockService _clock = new();
    private readonly HostedExtracodeTrace _trace = new();
    private readonly HostedHangDetector _hang = new();
    public bool UseWallClock { get => _clock.UseWallClock; set => _clock.UseWallClock = value; }
    internal Func<double>? GuestTimeSeconds { get => _clock.GuestTimeSeconds; set => _clock.GuestTimeSeconds = value; }
    public bool HangDetect { get => _hang.Enabled; set => _hang.Enabled = value; }

    public void MapDrumToDisk(int drum, int diskUnit, TapeImage disk) => _storage.MapDrumToDisk(drum, diskUnit, disk);
    public void FinishOutput() => _print.FinishOutput();
    public ExtracodeHandler(MachineCore machine, Func<long, TapeImage?> diskByTapeId, Func<int, TapeImage?> diskByUnit, Func<int, TapeImage?> drumByUnit, Action<string>? output = null, Func<string, string>? input = null, Func<long, int, bool>? mountTape = null, Func<long, int>? findTape = null, Action<long>? releaseTapes = null, Func<long, int, bool, bool>? mountTapeWithMode = null, Func<ulong, ulong, bool, uint>? fileSearch = null, Func<int, uint, bool, uint, int>? fileMount = null, Action<int, int>? scratchMount = null, Action<string>? terminalOutput = null)
    {
        _registers = machine.Cpu;
        Action<string> write = output ?? (s => Console.Write(s));
        Func<string, string> read = input ?? (p =>
        {
            Console.Write(p);
            return Console.ReadLine() ?? "";
        });
        var tapes = new HostedStorageBindings(diskByUnit, drumByUnit, mountTape ?? ((id, unit) => false), mountTapeWithMode, findTape ?? (id => 0), releaseTapes ?? (mask =>
        {
        }), fileSearch ?? ((disc, file, writeMode) => 0), fileMount ?? ((unit, offset, writeMode, fileOffset) => 8), scratchMount ?? ((unit, zones) =>
        {
        }));
        _math = new(machine.Cpu, machine.Memory, machine.Plotter, _clock);
        _print = new(machine.Cpu, machine.Memory, write);
        _terminal = new(machine.Cpu, machine.Memory, machine.Plotter, machine.Puncher, terminalOutput ?? write, read, _print.FinishOutput);
        _storage = new(machine.Cpu, machine.Memory, machine.Plotter, tapes, _trace);
        _system = new(machine.Cpu, machine.Memory, machine.Cpu, machine.CommandMemory,
            () => machine.Cpu.InterceptCount = 1);
    }

    public bool Handle(ExtracodeCall call)
    {
        _trace.Observe(call, _registers);
        _hang.Observe(call.Code, _registers.K);
        Extracode code = call.Code;
        uint aex = call.EffectiveAddress;
        switch (code)
        {
            case Extracode.E50:
                _math.E50();
                return true;
            case Extracode.E51:
                _math.E51();
                return true;
            case Extracode.E52:
                _math.E52();
                return true;
            case Extracode.E53:
                _math.E53();
                return true;
            case Extracode.E54:
                _math.E54();
                return true;
            case Extracode.E55:
                _math.E55();
                return true;
            case Extracode.E56:
                _math.E56();
                return true;
            case Extracode.E57:
                _storage.E57();
                return true;
            case Extracode.E61:
                _terminal.E61();
                return true;
            case Extracode.E63:
                _system.E63();
                return true;
            case Extracode.E64:
                _print.E64(aex);
                return true;
            case Extracode.E65:
                _system.E65();
                return true;
            case Extracode.E67:
                _system.E67();
                return true;
            case Extracode.E70:
                _storage.E70();
                return true;
            case Extracode.E71:
                _terminal.E71();
                return true;
            case Extracode.E72:
                _system.E72();
                return true;
            case Extracode.E73:
                return true;
            case Extracode.E74:
                throw new ProcessorException("");
            case Extracode.E75:
                _system.E75();
                return true;
            case Extracode.E76:
                _system.E76();
                return true;
            case Extracode.E20:
                return true;
            case Extracode.E21:
                return true;
            default:
                return false;
        }
    }

    public bool Handle(int opcode, uint effectiveAddress) => Handle(new ExtracodeCall((Extracode)opcode, effectiveAddress, 0, 0, false));
    public void Dispose() => _trace.Dispose();
}
