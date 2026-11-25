using Besm6.Android.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Besm6.Android;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, ThemeService theme)
    {
        InitializeComponent();
        _services = services;
        theme.Apply(this);
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<MainPage>());
}
