using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CenterHubNew.MVVM.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Timers;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class StandingViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string sittingMinutes = "30";

        [ObservableProperty]
        private string standingMinutes = "30";

        [ObservableProperty]
        private bool isStartButtonEnabled = true;

        private Timer? _sittingTimer;
        private Timer? _standingTimer;
        private double _sittingTimeRemaining;
        private double _standingTimeRemaining;
        private int _sittingMinutes;
        private int _standingMinutes;
        private bool _isRunning;

        public StandingViewModel(ILogger<StandingViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("StandingViewModel initialized");
        }

        private void StartStandingTimer(int standingMinutes, int sittingMinutes)
        {
            try
            {
                _standingMinutes = standingMinutes;
                _sittingMinutes = sittingMinutes;
                _standingTimeRemaining = standingMinutes * 60;

                _isRunning = true;

                _standingTimer = new Timer(1000);
                _standingTimer.Elapsed += StandingTimer_Elapsed;
                _standingTimer.Start();
                
                Logger?.LogInformation("Started standing timer for {Minutes} minutes", standingMinutes);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to start standing timer");
            }
        }

        private void StandingTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isRunning)
            {
                StopAndDisposeTimer(_standingTimer, StandingTimer_Elapsed);
                return;
            }

            _standingTimeRemaining--;

            if (_standingTimeRemaining <= 0)
            {
                StopAndDisposeTimer(_standingTimer, StandingTimer_Elapsed);
                SendSitDownNotification();
                StartSittingTimer(_sittingMinutes, _standingMinutes);
            }
        }

        private void StartSittingTimer(int sittingMinutes, int standingMinutes)
        {
            try
            {
                _sittingTimeRemaining = sittingMinutes * 60;

                _sittingTimer = new Timer(1000);
                _sittingTimer.Elapsed += SittingTimer_Elapsed;
                _sittingTimer.Start();
                
                Logger?.LogInformation("Started sitting timer for {Minutes} minutes", sittingMinutes);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to start sitting timer");
            }
        }

        private void SittingTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isRunning)
            {
                StopAndDisposeTimer(_sittingTimer, SittingTimer_Elapsed);
                return;
            }

            _sittingTimeRemaining--;

            if (_sittingTimeRemaining <= 0)
            {
                StopAndDisposeTimer(_sittingTimer, SittingTimer_Elapsed);
                SendStandUpNotification();
                StartStandingTimer(_standingMinutes, _sittingMinutes);
            }
        }

        private void StopAndDisposeTimer(Timer? timer, ElapsedEventHandler handler)
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer.Elapsed -= handler;
                    timer.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error stopping and disposing timer");
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartTimers))]
        public void StartTimers()
        {
            if (IsDisposed) return;

            try
            {
                if (int.TryParse(SittingMinutes, out int sitMinutes) && int.TryParse(StandingMinutes, out int standMinutes))
                {
                    if (!_isRunning)
                    {
                        StartStandingTimer(standMinutes, sitMinutes);
                        SendStartNotification();
                        IsStartButtonEnabled = false;
                        Logger?.LogInformation("Timers started - Stand: {StandMinutes}, Sit: {SitMinutes}", standMinutes, sitMinutes);
                        ToastService.Instance.Success($"Standing timer started - Stand: {standMinutes}min, Sit: {sitMinutes}min");
                    }
                    else
                    {
                        Logger?.LogWarning("Attempted to start timers while already running");
                        ToastService.Instance.Warning("Timers are already running.");
                    }
                }
                else
                {
                    Logger?.LogWarning("Invalid timer values entered - Sitting: {SittingMinutes}, Standing: {StandingMinutes}", SittingMinutes, StandingMinutes);
                    ToastService.Instance.Warning("Please enter valid numbers for sitting and standing time");
                    ToastService.Instance.Error("Please enter valid numbers for sitting and standing time.");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error starting timers");
                ToastService.Instance.Error($"Error starting timers: {ex.Message}");
            }
        }

        public bool CanStartTimers() => IsStartButtonEnabled && !IsDisposed;

        [RelayCommand]
        public void StopTimers()
        {
            if (IsDisposed) return;

            try
            {
                _isRunning = false;
                StopAndDisposeTimer(_sittingTimer, SittingTimer_Elapsed);
                StopAndDisposeTimer(_standingTimer, StandingTimer_Elapsed);
                SendStopNotification();
                IsStartButtonEnabled = true;
                Logger?.LogInformation("Timers stopped");
                ToastService.Instance.Success("Standing timer stopped");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error stopping timers");
            }
        }

        private void SendStandUpNotification()
        {
            try
            {
                ToastService.Instance.Info("Stand Up! It's time to stand up and stretch!");
                Logger?.LogInformation("Stand up notification sent");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to send stand up notification");
            }
        }

        private void SendSitDownNotification()
        {
            try
            {
                ToastService.Instance.Info("Sit Down! Time to focus on work.");
                Logger?.LogInformation("Sit down notification sent");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to send sit down notification");
            }
        }

        private void SendStartNotification()
        {
            try
            {
                ToastService.Instance.Success($"Timers started — stand {StandingMinutes} min / sit {SittingMinutes} min");
                Logger?.LogInformation("Start notification sent");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to send start notification");
            }
        }

        private void SendStopNotification()
        {
            try
            {
                ToastService.Instance.Info("Standing timers stopped.");
                Logger?.LogInformation("Stop notification sent");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to send stop notification");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try
                {
                    _isRunning = false;
                    StopAndDisposeTimer(_sittingTimer, SittingTimer_Elapsed);
                    StopAndDisposeTimer(_standingTimer, StandingTimer_Elapsed);
                    Logger?.LogInformation("StandingViewModel disposed");
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error disposing StandingViewModel");
                }
            }
            base.Dispose(disposing);
        }
    }
}
