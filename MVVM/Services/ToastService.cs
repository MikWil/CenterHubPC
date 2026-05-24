using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Services;

public enum ToastType
{
    Success,
    Error,
    Info,
    Warning
}

public partial class ToastMessage : ObservableObject
{
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private ToastType _type = ToastType.Info;
    [ObservableProperty] private bool _isVisible = true;
    public Guid Id { get; } = Guid.NewGuid();
}

public class ToastService
{
    private static ToastService? _instance;
    public static ToastService Instance => _instance ??= new ToastService();
    
    public ObservableCollection<ToastMessage> Toasts { get; } = new();
    
    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        var toast = new ToastMessage { Message = message, Type = type };

        void ShowOnUiThread()
        {
            Toasts.Add(toast);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                toast.IsVisible = false;
                Toasts.Remove(toast);
            };
            timer.Start();
        }

        if (Dispatcher.UIThread.CheckAccess())
            ShowOnUiThread();
        else
        {
            try { Dispatcher.UIThread.Post(ShowOnUiThread); }
            catch (InvalidOperationException) { /* dispatcher already shut down */ }
        }
    }
    
    public void Success(string message) => Show(message, ToastType.Success);
    public void Error(string message) => Show(message, ToastType.Error, 5000);
    public void Info(string message) => Show(message, ToastType.Info);
    public void Warning(string message) => Show(message, ToastType.Warning, 4000);
}

