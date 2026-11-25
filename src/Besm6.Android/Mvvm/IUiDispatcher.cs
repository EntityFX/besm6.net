using Microsoft.Maui.ApplicationModel;

namespace Besm6.Android.Mvvm;

/// <summary>Marshals ViewModel notifications to the MAUI UI thread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
