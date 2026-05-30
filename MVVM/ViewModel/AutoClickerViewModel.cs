using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class AutoClickerViewModel : BaseViewModel
    {
        private readonly AutoClickerService _autoClickerService;
        private DispatcherTimer? _positionCaptureTimer;
        private DispatcherTimer? _runtimeTimer;

        // ===== Target =====
        [ObservableProperty] private int _clickX;
        [ObservableProperty] private int _clickY;

        // ===== Timing =====
        /// <summary>Interval BETWEEN clicks, in seconds (float so 0.05 = 50ms is allowed).</summary>
        [ObservableProperty] private double _intervalSeconds = 1.0;
        /// <summary>How long the arm countdown lasts before the first click is dispatched.</summary>
        [ObservableProperty] private double _armDelaySeconds = 3.0;

        // ===== Behaviour =====
        /// <summary>0 = infinite.</summary>
        [ObservableProperty] private int _maxClicks;
        /// <summary>±N px random jitter around the target each click.</summary>
        [ObservableProperty] private int _jitterPixels;
        [ObservableProperty] private bool _failsafeEnabled = true;

        public List<string> ModeOptions { get; } = new()
        {
            "Silent (no cursor move) — recommended",
            "Teleport to position (visible cursor jump)",
            "Click at current cursor position",
        };
        [ObservableProperty] private int _selectedModeIndex;     // 0..2 mirrors ModeOptions

        public List<string> ButtonOptions { get; } = new() { "Left", "Right", "Middle" };
        [ObservableProperty] private int _selectedButtonIndex;   // 0..2

        // ===== Status / live state =====
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isArming;
        [ObservableProperty] private int _armSecondsRemaining;
        [ObservableProperty] private bool _isCapturingPosition;
        [ObservableProperty] private int _captureCountdown;
        [ObservableProperty] private int _clicksDelivered;
        [ObservableProperty] private string _elapsedDisplay = "00:00";
        [ObservableProperty] private string _statusMessage = "Ready";

        public AutoClickerViewModel(
            AutoClickerService autoClickerService,
            ILogger<AutoClickerViewModel>? logger = null) : base(logger)
        {
            _autoClickerService = autoClickerService;

            var pos = AutoClickerService.GetCurrentMousePosition();
            ClickX = pos.X;
            ClickY = pos.Y;

            _autoClickerService.ClickDelivered += OnClickDelivered;
            _autoClickerService.Stopped += OnServiceStopped;
            _autoClickerService.Arming += OnArming;

            Logger?.LogInformation("AutoClickerViewModel initialized");
        }

        // ===== Hotkey entry point =====
        public void ToggleStartStop()
        {
            if (IsRunning || IsArming) Stop();
            else Start();
        }

        // ===== Commands =====
        [RelayCommand]
        private void Start()
        {
            if (IsRunning || IsArming) return;

            if (IntervalSeconds <= 0)
            {
                ToastService.Instance.Warning("Interval must be greater than 0");
                StatusMessage = "Interval must be greater than 0";
                return;
            }
            if (ArmDelaySeconds < 0)
            {
                ArmDelaySeconds = 0;
            }

            var mode = SelectedModeIndex switch
            {
                1 => ClickMode.TeleportFixed,
                2 => ClickMode.Follow,
                _ => ClickMode.SilentFixed,
            };
            var button = SelectedButtonIndex switch
            {
                1 => MouseClickButton.Right,
                2 => MouseClickButton.Middle,
                _ => MouseClickButton.Left,
            };

            var opts = new AutoClickerOptions
            {
                X = ClickX,
                Y = ClickY,
                IntervalSeconds = IntervalSeconds,
                MaxClicks = Math.Max(0, MaxClicks),
                Mode = mode,
                Button = button,
                ArmDelay = TimeSpan.FromSeconds(Math.Max(0, ArmDelaySeconds)),
                JitterPixels = Math.Max(0, JitterPixels),
                FailsafeEnabled = FailsafeEnabled,
            };

            ClicksDelivered = 0;
            ElapsedDisplay = "00:00";
            IsArming = ArmDelaySeconds > 0;
            ArmSecondsRemaining = (int)Math.Ceiling(ArmDelaySeconds);
            IsRunning = true;
            StatusMessage = IsArming
                ? $"Arming — {ArmSecondsRemaining}s to first click"
                : "Running…";

            _autoClickerService.Start(opts);

            // start the elapsed clock once the arm phase completes; we just update each second
            _runtimeTimer?.Stop();
            _runtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _runtimeTimer.Tick += (_, _) =>
            {
                if (IsDisposed) { _runtimeTimer?.Stop(); return; }
                var e = _autoClickerService.Elapsed;
                ElapsedDisplay = $"{(int)e.TotalMinutes:D2}:{e.Seconds:D2}";
            };
            _runtimeTimer.Start();

            Logger?.LogInformation("AutoClicker started mode={Mode} button={Btn} interval={Int}s arm={Arm}s max={Max}",
                mode, button, IntervalSeconds, ArmDelaySeconds, MaxClicks);
            ToastService.Instance.Success(
                IsArming
                    ? $"Auto-clicker armed — first click in {ArmSecondsRemaining}s"
                    : "Auto-clicker started");
        }

        [RelayCommand]
        private void Stop()
        {
            _autoClickerService.Stop();
            // OnServiceStopped will reset state
        }

        [RelayCommand]
        private void CapturePosition()
        {
            if (IsCapturingPosition) return;

            IsCapturingPosition = true;
            CaptureCountdown = 3;
            StatusMessage = $"Move cursor to target… {CaptureCountdown}";

            _positionCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _positionCaptureTimer.Tick += (_, _) =>
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
                    ToastService.Instance.Success($"Position: ({ClickX}, {ClickY})");
                }
                else
                {
                    StatusMessage = $"Move cursor to target… {CaptureCountdown}";
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
            StatusMessage = $"Position: ({ClickX}, {ClickY})";
            ToastService.Instance.Info($"Set to ({ClickX}, {ClickY})");
        }

        // ===== Service callbacks (raised from background thread) =====
        private void OnArming(int secondsRemaining)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed) return;
                ArmSecondsRemaining = secondsRemaining;
                if (secondsRemaining > 0)
                {
                    IsArming = true;
                    StatusMessage = $"Arming — {secondsRemaining}s to first click";
                }
                else
                {
                    IsArming = false;
                    StatusMessage = "Running…";
                }
            });
        }

        private void OnClickDelivered(int total)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed) return;
                ClicksDelivered = total;
            });
        }

        private void OnServiceStopped(string reason)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed) return;
                IsRunning = false;
                IsArming = false;
                ArmSecondsRemaining = 0;
                _runtimeTimer?.Stop();
                StatusMessage = $"Stopped — {reason} · {ClicksDelivered} clicks";

                if (reason.StartsWith("failsafe", StringComparison.OrdinalIgnoreCase))
                    ToastService.Instance.Warning("Auto-clicker stopped by failsafe");
                else if (reason.StartsWith("reached limit", StringComparison.OrdinalIgnoreCase))
                    ToastService.Instance.Success($"Done — {ClicksDelivered} clicks");
                else
                    ToastService.Instance.Info("Auto-clicker stopped");
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _autoClickerService.ClickDelivered -= OnClickDelivered;
                _autoClickerService.Stopped -= OnServiceStopped;
                _autoClickerService.Arming -= OnArming;
                _autoClickerService.Stop();
                _positionCaptureTimer?.Stop();
                _runtimeTimer?.Stop();
                Logger?.LogInformation("AutoClickerViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
