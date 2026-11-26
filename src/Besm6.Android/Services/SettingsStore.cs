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
            Preferences.Default.Get("debugMode", defaults.DebugMode),
            Preferences.Default.Get("useWallClock", defaults.UseWallClock),
            Preferences.Default.Get("loopDetect", defaults.LoopDetect),
            Preferences.Default.Get("hangDetect", defaults.HangDetect),
            Preferences.Default.Get("forexAsFortran", defaults.ForexAsFortran));

        return result.TryValidate(out _) ? result : defaults;
    }

    public DisplaySettings LoadDisplay()
    {
        DisplaySettings defaults = DisplaySettings.Default;
        int rawBase = Preferences.Default.Get("numberBase", (int)defaults.NumberBase);
        var numberBase = Enum.IsDefined(typeof(NumberBase), rawBase)
            ? (NumberBase)rawBase
            : defaults.NumberBase;
        var result = new DisplaySettings(
            Preferences.Default.Get("outputFontFamily", defaults.OutputFontFamily),
            Preferences.Default.Get("outputFontSize", defaults.OutputFontSize),
            Preferences.Default.Get("editorFontSize", defaults.EditorFontSize),
            Preferences.Default.Get("memoryWords", defaults.MemoryWords),
            numberBase);
        return result.TryValidate(out _) ? result : defaults;
    }

    public void SaveDisplay(DisplaySettings settings)
    {
        Preferences.Default.Set("outputFontFamily", settings.OutputFontFamily);
        Preferences.Default.Set("outputFontSize", settings.OutputFontSize);
        Preferences.Default.Set("editorFontSize", settings.EditorFontSize);
        Preferences.Default.Set("memoryWords", settings.MemoryWords);
        Preferences.Default.Set("numberBase", (int)settings.NumberBase);
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
        Preferences.Default.Set("useWallClock", settings.UseWallClock);
        Preferences.Default.Set("loopDetect", settings.LoopDetect);
        Preferences.Default.Set("hangDetect", settings.HangDetect);
        Preferences.Default.Set("forexAsFortran", settings.ForexAsFortran);
    }
}
