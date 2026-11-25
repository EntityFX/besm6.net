using Besm6.Android.ViewModels;

namespace Besm6.Android;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private bool _started;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        await _viewModel.InitializeAsync();
    }
}
