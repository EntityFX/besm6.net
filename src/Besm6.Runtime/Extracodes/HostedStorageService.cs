using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Extracodes;
/// <summary>Tape/file services and disk/drum transfers; owns the writable phys_io clone.</summary>
internal sealed partial class HostedStorageService(IRegisterOps cpu, IHostMemory memory, Plotter plotter, HostedStorageBindings tapes, HostedExtracodeTrace trace)
{
    private readonly IRegisterOps _cpu = cpu;
    private readonly IHostMemory _memory = memory;
    private readonly Plotter _plotter = plotter;
    private readonly HostedStorageBindings _tapes = tapes;
    private readonly HostedExtracodeTrace _trace = trace;
    private const int M16 = 14;
    private int _mappedDrum = -1;
    private TapeImage? _physIoDisk;
    internal void MapDrumToDisk(int drum, int diskUnit, TapeImage disk)
    {
        _mappedDrum = drum;
        _physIoDisk = new TapeImage(disk.VolumeId, (byte[])disk.Data.Clone(), readOnly: false);
    }
}
