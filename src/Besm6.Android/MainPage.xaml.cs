using System.ComponentModel;
using Besm6.Android.ViewModels;
using Microsoft.Maui.Dispatching;

namespace Besm6.Android;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private bool _started;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        await _viewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TerminalText))
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), async () =>
            {
                try
                {
                    await TerminalScroll.ScrollToAsync(0, TerminalScroll.ContentSize.Height, false);
                }
                catch (Exception)
                {
                    // The tab may have been switched before the scroll was applied.
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWaitingInput) && _viewModel.IsWaitingInput)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => TerminalInput.Focus());
        }
    }
}
