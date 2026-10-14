using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Extracodes;
/// <summary>Loader-owned tape and file operations required by the storage ABI.</summary>
internal sealed record HostedStorageBindings(Func<int, TapeImage?> DiskByUnit, Func<int, TapeImage?> DrumByUnit, Func<long, int, bool> MountTape, Func<long, int, bool, bool>? MountTapeWithMode, Func<long, int> FindTape, Action<long> ReleaseTapes, Func<ulong, ulong, bool, uint> FileSearch, Func<int, uint, bool, uint, int> FileMount, Action<int, int> ScratchMount);
