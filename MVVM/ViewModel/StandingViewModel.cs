using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.Timers;
using System.Windows;

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
                    }
                    else
                    {
                        Logger?.LogWarning("Attempted to start timers while already running");
                        MessageBox.Show("Timers are already running.");
                    }
                }
                else
                {
                    Logger?.LogWarning("Invalid timer values entered - Sitting: {SittingMinutes}, Standing: {StandingMinutes}", SittingMinutes, StandingMinutes);
                    MessageBox.Show("Please enter valid numbers for sitting and standing time.");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error starting timers");
                MessageBox.Show($"Error starting timers: {ex.Message}");
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
                new ToastContentBuilder()
                    .AddText("Stand Up!")
                    .AddText("It's time to stand up and stretch!")
                    .Show();
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
                new ToastContentBuilder()
                    .AddText("Sit Down!")
                    .AddText("Time to sit down and focus on work!")
                    .Show();
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
                new ToastContentBuilder()
                    .AddText("Timers Started")
                    .AddText($"Stand for {StandingMinutes} minutes, then sit for {SittingMinutes} minutes.")
                    .Show();
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
                new ToastContentBuilder()
                    .AddText("Timers Stopped")
                    .AddText("The sitting and standing cycle has been stopped.")
                    .Show();
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
