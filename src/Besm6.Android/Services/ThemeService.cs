using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Besm6.Android.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Three-state app theme, persisted between launches.</summary>
public sealed class ThemeService
{
    private const string PreferenceKey = "themeMode";

    public ThemeService()
    {
        int stored = Preferences.Default.Get(PreferenceKey, (int)ThemeMode.System);
        Mode = Enum.IsDefined(typeof(ThemeMode), stored) ? (ThemeMode)stored : ThemeMode.System;
    }

    public ThemeMode Mode { get; private set; }
    public string Label => Mode switch
    {
        ThemeMode.Light => "Светлая",
        ThemeMode.Dark => "Тёмная",
        _ => "Авто",
    };

    public void Apply(Application? application = null)
    {
        application ??= Application.Current;
        if (application is null) return;
        application.UserAppTheme = Mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }

    public void Cycle()
    {
        Mode = Mode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        Preferences.Default.Set(PreferenceKey, (int)Mode);
        Apply();
    }
}
