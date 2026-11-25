using Besm6.Runtime.Configuration;
using Microsoft.Maui.Storage;

namespace Besm6.Android.Services;

/// <summary>Persists launch options in MAUI application preferences.</summary>
public sealed class SettingsStore
{
    public RunSettings Load()
    {
        RunSettings defaults = RunSettings.Default;
        string speedName = Preferences.Default.Get("speed", "max");
        if (!ExecutionSpeedNames.TryParse(speedName, out var speed)) speed = defaults.Speed;

        int modelValue = Preferences.Default.Get("model", (int)defaults.Model);
        var model = Enum.IsDefined(typeof(MachineModel), modelValue)
            ? (MachineModel)modelValue
            : defaults.Model;

        var result = new RunSettings(
            speed,
            Preferences.Default.Get("instructionLimit", defaults.InstructionLimit),
            Preferences.Default.Get("wallClockMs", defaults.WallClockLimitMs),
            model,
            Preferences.Default.Get("guestClock", defaults.GuestClock),
            Preferences.Default.Get("statistics", defaults.CollectStatistics),
            Preferences.Default.Get("debugMode", defaults.DebugMode));

        return result.TryValidate(out _) ? result : defaults;
    }

    public void Save(RunSettings settings)
    {
        string speed = settings.Speed switch
        {
            ExecutionSpeed.Original => "original",
            ExecutionSpeed.Virtual => "virtual",
            _ => "max",
        };
        Preferences.Default.Set("speed", speed);
        Preferences.Default.Set("model", (int)settings.Model);
        Preferences.Default.Set("instructionLimit", settings.InstructionLimit);
        Preferences.Default.Set("wallClockMs", settings.WallClockLimitMs);
        Preferences.Default.Set("guestClock", settings.GuestClock);
        Preferences.Default.Set("statistics", settings.CollectStatistics);
        Preferences.Default.Set("debugMode", settings.DebugMode);
    }
}
