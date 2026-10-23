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
    bool Statistics = false,
    string? Text = null,
    string? InputTarget = null,
    string? MediaId = null,
    string Kind = "Disk",
    int Unit = 24,
    string Volume = "0",
    bool ReadOnly = true,
    bool Replace = false,
    int MediaOffset = 0,
    int MediaCount = 32,
    string MachineModel = "dubna",
    bool DecodeMemory = true);

public sealed record WorkerEvent(string Type, object? Data);

public sealed record StateData(string Status);
public sealed record OutputData(string Channel, string Text);
public sealed record ErrorData(string Message, string? Stack);
public sealed record CompletionData(bool Success, bool Stopped, bool LimitExceeded, long K, long Instructions, string? ErrorMessage);
public sealed record RegisterData(string K, bool RightHalf, string A, string Y, string R, string C, string[] M, long Instructions, string Disassembly);
public sealed record MemoryWord(string Address, string Hex, string? Disassembly = null);
public sealed record MemoryData(MemoryWord[] Words);
public sealed record PlotterData(int Page, int WatanabeBytes, int CalcompBytes, int TektronixBytes);
public sealed record StorageDeviceData(string Kind, string Unit, string Volume, bool ReadOnly, int Words);
public sealed record SessionFileData(string Name, long Bytes);
public sealed record StorageData(StorageDeviceData[] Mounts, SessionFileData[] Files);
public sealed record MediaWord(int Offset, string Hex, string Disassembly, string Text);
public sealed record MediaViewData(string Id, int TotalWords, MediaWord[] Words, int? LoadedAddress = null);
public sealed record PunchCardData(string[] Rows);
