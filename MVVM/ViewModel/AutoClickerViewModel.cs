using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class AutoClickerViewModel : BaseViewModel
    {
        private readonly AutoClickerService _autoClickerService;
        private readonly GlobalHotkeyService _hotkeyService;
        private DispatcherTimer? _positionCaptureTimer;
        private bool _hotkeysRegistered;

        [ObservableProperty]
        private int _clickX;

        [ObservableProperty]
        private int _clickY;

        [ObservableProperty]
        private double _intervalSeconds = 1.0;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isCapturingPosition;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private int _captureCountdown;

        public AutoClickerViewModel(
            AutoClickerService autoClickerService,
            ILogger<AutoClickerViewModel>? logger = null) : base(logger)
        {
            _autoClickerService = autoClickerService;
            _hotkeyService = new GlobalHotkeyService();
            
            // Get initial mouse position
            var pos = AutoClickerService.GetCurrentMousePosition();
            ClickX = pos.X;
            ClickY = pos.Y;

            // Register hotkeys when main window is loaded
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    var handle = new WindowInteropHelper(mainWindow).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        RegisterHotkeys(handle);
                    }
                    else
                    {
                        mainWindow.Loaded += (s, e) =>
                        {
                            var h = new WindowInteropHelper(mainWindow).Handle;
                            RegisterHotkeys(h);
                        };
                    }
                }
            });

            Logger?.LogInformation("AutoClickerViewModel initialized");
        }

        private void RegisterHotkeys(IntPtr handle)
        {
            if (_hotkeysRegistered) return;
            
            _hotkeyService.OnStartStopPressed += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (IsRunning)
                        Stop();
                    else
                        Start();
                });
            };

            _hotkeyService.OnSetPositionPressed += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    GetCurrentPosition();
                });
            };

            _hotkeyService.Register(handle);
            _hotkeysRegistered = true;
            Logger?.LogInformation("Global hotkeys registered: Ctrl+K (start/stop), Ctrl+P (set position)");
        }

        [RelayCommand]
        private void Start()
        {
            if (IsRunning) return;

            if (IntervalSeconds <= 0)
            {
                StatusMessage = "Interval must be greater than 0";
                return;
            }

            _autoClickerService.Start(ClickX, ClickY, IntervalSeconds);
            IsRunning = true;
            StatusMessage = $"Clicking at ({ClickX}, {ClickY}) every {IntervalSeconds}s";
            Logger?.LogInformation("AutoClicker started");
        }

        [RelayCommand]
        private void Stop()
        {
            _autoClickerService.Stop();
            IsRunning = false;
            StatusMessage = "Stopped";
            Logger?.LogInformation("AutoClicker stopped");
        }

        [RelayCommand]
        private void CapturePosition()
        {
            if (IsCapturingPosition) return;

            IsCapturingPosition = true;
            CaptureCountdown = 3;
            StatusMessage = $"Move mouse to target... {CaptureCountdown}";

            _positionCaptureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _positionCaptureTimer.Tick += (s, e) =>
            {
                CaptureCountdown--;
                
                if (CaptureCountdown <= 0)
                {
                    _positionCaptureTimer.Stop();
                    var pos = AutoClickerService.GetCurrentMousePosition();
                    ClickX = pos.X;
                    ClickY = pos.Y;
                    IsCapturingPosition = false;
                    StatusMessage = $"Position captured: ({ClickX}, {ClickY})";
                    Logger?.LogInformation("Position captured: ({X}, {Y})", ClickX, ClickY);
                }
                else
                {
                    StatusMessage = $"Move mouse to target... {CaptureCountdown}";
                }
            };

            _positionCaptureTimer.Start();
        }

        [RelayCommand]
        private void GetCurrentPosition()
        {
            var pos = AutoClickerService.GetCurrentMousePosition();
            ClickX = pos.X;
            ClickY = pos.Y;
            StatusMessage = $"Current position: ({ClickX}, {ClickY})";
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _autoClickerService.Stop();
                _positionCaptureTimer?.Stop();
                _hotkeyService.Dispose();
                Logger?.LogInformation("AutoClickerViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

