using Besm6.Runtime.Configuration;

namespace Besm6.Android.Services;

/// <summary>Machine selection exposed by the Android launch settings.</summary>
public enum MachineModel
{
    Dubna,
    DubnaBuffered,
    SupervisorFunctional,
    SupervisorHardware,
}

/// <summary>Validated launch settings. A zero limit disables that limit.</summary>
public sealed record RunSettings(
    ExecutionSpeed Speed,
    long InstructionLimit,
    long WallClockLimitMs,
    MachineModel Model,
    bool GuestClock,
    bool CollectStatistics,
    bool DebugMode = false)
{
    public const long MaxInstructionLimit = 1_000_000_000;
    public const long MaxWallClockMs = 3_600_000;

    public static RunSettings Default { get; } = new(
        ExecutionSpeed.Max,
        20_000_000,
        0,
        MachineModel.Dubna,
        GuestClock: false,
        CollectStatistics: false,
        DebugMode: false);

    public bool TryValidate(out string? error)
    {
        if (!Enum.IsDefined(Speed))
        {
            error = "Неизвестная скорость исполнения.";
            return false;
        }
        if (InstructionLimit is < 0 or > MaxInstructionLimit)
        {
            error = $"Лимит команд должен быть от 0 до {MaxInstructionLimit}.";
            return false;
        }
        if (WallClockLimitMs is < 0 or > MaxWallClockMs)
        {
            error = "Лимит времени должен быть от 0 до 3600 секунд.";
            return false;
        }
        if (!Enum.IsDefined(Model))
        {
            error = "Неизвестная модель машины.";
            return false;
        }
        if (DebugMode && Model == MachineModel.SupervisorHardware)
        {
            error = "Пошаговая отладка доступна для моделей Дубна и функционального supervisor.";
            return false;
        }
        error = null;
        return true;
    }
}

public enum RunPhase
{
    Preparing,
    Running,
    Paused,
    WaitingInput,
    Stopping,
    Completed,
    Failed,
}

public sealed record RunOutcome(
    bool Success,
    bool Cancelled,
    bool LimitExceeded,
    long Instructions,
    long K,
    string? Error);

public sealed record MemoryWordView(int Address, ulong Value, string Disassembly);

/// <summary>Independent memory copy requested at an execution boundary.</summary>
public sealed record MemorySnapshot(int Address, MemoryWordView[] Words);

/// <summary>Independent register copy produced on an execution boundary.</summary>
public sealed record RegisterSnapshot(
    string MemoryModel,
    string ExecutionModel,
    string ProcessorProfile,
    uint K,
    bool RightHalf,
    ulong A,
    ulong Y,
    uint R,
    uint C,
    bool ApplyC,
    uint[] M,
    long Instructions,
    Besm6.Runtime.BufferRegisterView[] Buffers,
    Besm6.Architecture.Machine.MachineRegisterView[] Registers);
