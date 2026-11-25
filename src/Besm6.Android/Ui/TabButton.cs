using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Besm6.Android.Ui;

/// <summary>Compact pill tab used for switching the main content area.</summary>
public sealed class TabButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(TabButton), string.Empty,
        propertyChanged: (bindable, _, value) => ((TabButton)bindable)._label.Text = (string?)value ?? string.Empty);

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(TabButton), false,
        propertyChanged: (bindable, _, _) => ((TabButton)bindable).UpdateVisual());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(TabButton));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(TabButton));

    private readonly Border _border;
    private readonly Label _label;

    public TabButton()
    {
        _label = new Label
        {
            FontSize = 10.5,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        _border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            StrokeThickness = 1,
            Padding = new Thickness(8, 3),
            Content = _label,
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (Command?.CanExecute(CommandParameter) == true)
                Command.Execute(CommandParameter);
        };
        _border.GestureRecognizers.Add(tap);
        Content = _border;
        UpdateVisual();

        if (Application.Current is { } app)
            app.RequestedThemeChanged += OnThemeChanged;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => UpdateVisual();

    private void UpdateVisual()
    {
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        Color accent = Color.FromArgb(dark ? "#E06A58" : "#9B2C1F");
        Color inactiveSurface = Color.FromArgb(dark ? "#292623" : "#EAE6DF");
        Color inactiveText = Color.FromArgb(dark ? "#F2EEE8" : "#1D1B19");
        Color border = Color.FromArgb(dark ? "#3C3732" : "#D6D0C7");

        _border.BackgroundColor = IsActive ? accent : inactiveSurface;
        _border.Stroke = new SolidColorBrush(IsActive ? accent : border);
        _label.TextColor = IsActive ? Colors.White : inactiveText;
        _label.FontAttributes = IsActive ? FontAttributes.Bold : FontAttributes.None;
    }
}
