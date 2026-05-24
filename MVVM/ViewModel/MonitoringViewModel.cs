using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using Avalonia.Threading;
using System.Diagnostics;
using CenterHubNew.MVVM.Services;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MonitoringViewModel : BaseViewModel
    {
        private readonly DispatcherTimer _systemMonitorTimer;
        private readonly ISystemMonitorService _systemMonitorService;

        [ObservableProperty]
        private float cpuUsage;

        [ObservableProperty]
        private float cpuTemperature;

        [ObservableProperty]
        private float cpuMaxTemperature;

        [ObservableProperty]
        private float gpuUsage;

        [ObservableProperty]
        private float gpuTemperature;

        [ObservableProperty]
        private float gpuMaxTemperature;

        [ObservableProperty]
        private long totalMemory;

        [ObservableProperty]
        private long usedMemory;

        [ObservableProperty]
        private float memoryUsagePercent;

        [ObservableProperty]
        private List<DiskInfo> disks = new();

        public MonitoringViewModel(
            ISystemMonitorService systemMonitorService,
            ILogger<MonitoringViewModel>? logger = null) : base(logger)
        {
            _systemMonitorService = systemMonitorService ?? throw new ArgumentNullException(nameof(systemMonitorService));

            _systemMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _systemMonitorTimer.Tick += SystemMonitorTimer_Tick;
            _systemMonitorTimer.Start();
            
            _ = UpdateSystemInfoAsync();
            Logger?.LogInformation("MonitoringViewModel initialized");
        }

        private void SystemMonitorTimer_Tick(object? sender, EventArgs e)
        {
            _ = UpdateSystemInfoAsync();
        }

        private async Task UpdateSystemInfoAsync()
        {
            if (IsDisposed) return;
            try
            {
                var systemInfo = await _systemMonitorService.GetSystemInfoAsync().ConfigureAwait(false);
                if (IsDisposed) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsDisposed) return;
                    CpuUsage = systemInfo.CpuUsage;
                    CpuTemperature = systemInfo.CpuTemperature;
                    CpuMaxTemperature = systemInfo.CpuMaxTemperature;
                    GpuUsage = systemInfo.GpuInfo.Usage;
                    GpuTemperature = systemInfo.GpuInfo.GpuTemperature;
                    GpuMaxTemperature = systemInfo.GpuInfo.GpuMaxTemperature;
                    TotalMemory = systemInfo.MemoryInfo.TotalPhysicalMemory;
                    UsedMemory = systemInfo.MemoryInfo.UsedPhysicalMemory;
                    Disks = systemInfo.Disks;
                    MemoryUsagePercent = TotalMemory > 0 ? (float)UsedMemory / TotalMemory * 100 : 0;
                });
            }
            catch (InvalidOperationException) { /* dispatcher shut down — app is closing */ }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error updating system info");
                Debug.WriteLine($"Error updating system info: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _systemMonitorTimer.Stop();
                _systemMonitorTimer.Tick -= SystemMonitorTimer_Tick;
                Logger?.LogInformation("MonitoringViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

