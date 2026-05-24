using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using Avalonia.Threading;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class AutoClickerViewModel : BaseViewModel
    {
        private readonly AutoClickerService _autoClickerService;
        private DispatcherTimer? _positionCaptureTimer;

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
            
            // Get initial mouse position
            var pos = AutoClickerService.GetCurrentMousePosition();
            ClickX = pos.X;
            ClickY = pos.Y;

            Logger?.LogInformation("AutoClickerViewModel initialized");
        }

        /// <summary>
        /// Toggle start/stop, called by the global hotkey system.
        /// </summary>
        public void ToggleStartStop()
        {
            if (IsRunning)
                Stop();
            else
                Start();
        }

        [RelayCommand]
        private void Start()
        {
            if (IsRunning) return;

            if (IntervalSeconds <= 0)
            {
                StatusMessage = "Interval must be greater than 0";
                ToastService.Instance.Warning("Interval must be greater than 0");
                return;
            }

            _autoClickerService.Start(ClickX, ClickY, IntervalSeconds);
            IsRunning = true;
            StatusMessage = $"Clicking at ({ClickX}, {ClickY}) every {IntervalSeconds}s";
            Logger?.LogInformation("AutoClicker started");
            ToastService.Instance.Success($"Auto-clicker started at ({ClickX}, {ClickY})");
        }

        [RelayCommand]
        private void Stop()
        {
            _autoClickerService.Stop();
            IsRunning = false;
            StatusMessage = "Stopped";
            Logger?.LogInformation("AutoClicker stopped");
            ToastService.Instance.Success("Auto-clicker stopped");
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
                    ToastService.Instance.Success($"Position captured: ({ClickX}, {ClickY})");
                }
                else
                {
                    StatusMessage = $"Move mouse to target... {CaptureCountdown}";
                }
            };

            _positionCaptureTimer.Start();
        }

        [RelayCommand]
        public void GetCurrentPosition()
        {
            var pos = AutoClickerService.GetCurrentMousePosition();
            ClickX = pos.X;
            ClickY = pos.Y;
            StatusMessage = $"Current position: ({ClickX}, {ClickY})";
            ToastService.Instance.Info($"Position set: ({ClickX}, {ClickY})");
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _autoClickerService.Stop();
                _positionCaptureTimer?.Stop();
                Logger?.LogInformation("AutoClickerViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
