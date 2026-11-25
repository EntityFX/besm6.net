using Besm6.Android.Mvvm;
using Besm6.Android.Services;
using Besm6.Android.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;

namespace Besm6.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton(_ => new AssetInstaller(Platform.AppContext));
        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
        builder.Services.AddSingleton(serviceProvider =>
        {
            var installer = serviceProvider.GetRequiredService<AssetInstaller>();
            return new MachineRunService(installer.TapesDirectory, installer.WorkDirectory);
        });
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
