using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Threading.Tasks;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class ComputerViewModel : BaseViewModel
    {
        [ObservableProperty]
        private DateTime? selectedDate = DateTime.Now;

        [ObservableProperty]
        private string selectedTime = DateTime.Now.ToString("HH:mm");

        public string SelectedDateText
        {
            get => SelectedDate?.ToString("MM/dd/yyyy") ?? "";
            set
            {
                if (DateTime.TryParse(value, out var date))
                {
                    SelectedDate = date;
                    OnPropertyChanged();
                }
            }
        }

        partial void OnSelectedDateChanged(DateTime? value)
        {
            OnPropertyChanged(nameof(SelectedDateText));
        }

        [ObservableProperty]
        private string statusMessage = string.Empty;

        public ComputerViewModel(ILogger<ComputerViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("ComputerViewModel initialized");
        }

        [RelayCommand]
        private Task ScheduleShutdown()
        {
            if (IsDisposed) return Task.CompletedTask;

            try
            {
                if (SelectedDate == null || string.IsNullOrWhiteSpace(SelectedTime))
                {
                    StatusMessage = "Please select a date and time.";
                    Logger?.LogWarning("Shutdown scheduled attempted without date/time selection");
                    ToastService.Instance.Warning("Please select a date and time");
                    return Task.CompletedTask;
                }

                if (!TimeSpan.TryParseExact(
                        SelectedTime.Trim(),
                        new[] { @"h\:mm", @"hh\:mm", @"H\:mm", @"HH\:mm" },
                        CultureInfo.InvariantCulture,
                        out var time))
                {
                    StatusMessage = "Invalid time format. Use HH:mm.";
                    Logger?.LogWarning("Invalid time format entered: {Time}", SelectedTime);
                    ToastService.Instance.Warning("Invalid time format. Use HH:mm");
                    return Task.CompletedTask;
                }

                var shutdownDateTime = SelectedDate.Value.Date + time;
                var seconds = (int)(shutdownDateTime - DateTime.Now).TotalSeconds;

                if (seconds <= 0)
                {
                    StatusMessage = "Selected time is in the past.";
                    Logger?.LogWarning("Shutdown scheduled for past time: {DateTime}", shutdownDateTime);
                    ToastService.Instance.Warning("Selected time is in the past");
                    return Task.CompletedTask;
                }

                var psi = new ProcessStartInfo("shutdown", $"/s /t {seconds}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                StatusMessage = $"Shutdown scheduled for {shutdownDateTime}.";
                Logger?.LogInformation("Shutdown scheduled for {DateTime} ({Seconds} seconds from now)", shutdownDateTime, seconds);
                ToastService.Instance.Success($"Shutdown scheduled for {shutdownDateTime:MM/dd/yyyy HH:mm}");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to schedule shutdown");
                StatusMessage = $"Failed to schedule shutdown: {ex.Message}";
                ToastService.Instance.Error($"Failed to schedule shutdown: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        [RelayCommand]
        private void CancelShutdown()
        {
            if (IsDisposed) return;

            try
            {
                var psi = new ProcessStartInfo("shutdown", "/a")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                StatusMessage = "Shutdown cancelled.";
                Logger?.LogInformation("Shutdown cancelled");
                ToastService.Instance.Success("Shutdown cancelled");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to cancel shutdown");
                StatusMessage = $"Failed to cancel shutdown: {ex.Message}";
                ToastService.Instance.Error($"Failed to cancel shutdown: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("ComputerViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
} 