namespace Besm6.AspNetCore.Web.Protocol;

public sealed record WorkerCommand(
    string Type,
    string? JobPath = null,
    string? Mode = null,
    string? Speed = null,
    long InstructionLimit = 20_000_000,
    long WallClockLimitMs = 0,
    int MemoryAddress = -1,
    int MemoryCount = 32,
    bool GuestClock = false,
    bool Statistics = false);

public sealed record WorkerEvent(string Type, object? Data);

public sealed record StateData(string Status);
public sealed record OutputData(string Channel, string Text);
public sealed record ErrorData(string Message, string? Stack);
public sealed record CompletionData(bool Success, bool Stopped, bool LimitExceeded, long K, long Instructions, string? ErrorMessage);
public sealed record RegisterData(string K, bool RightHalf, string A, string Y, string R, string C, string[] M, long Instructions, string Disassembly);
public sealed record MemoryWord(string Address, string Hex);
public sealed record MemoryData(MemoryWord[] Words);
public sealed record PlotterData(int Page, int WatanabeBytes, int CalcompBytes, int TektronixBytes);
public sealed record StorageDeviceData(string Kind, string Unit, string Volume, bool ReadOnly, int Words);
public sealed record SessionFileData(string Name, long Bytes);
public sealed record StorageData(StorageDeviceData[] Mounts, SessionFileData[] Files);
