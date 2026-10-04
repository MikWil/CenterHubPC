using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>CPU / GPU / RAM load with small bars, refreshed every 2 s (off the UI thread) while the page is on screen.</summary>
    public sealed partial class SystemCardViewModel : DashboardCardViewModel
    {
        private readonly ISystemMonitorService _monitor;
        private DispatcherTimer? _timer;
        private int _refreshing;

        [ObservableProperty] private double _cpuPercent;
        [ObservableProperty] private string _cpuText = "–";
        [ObservableProperty] private string _cpuDetail = "";
        [ObservableProperty] private double _gpuPercent;
        [ObservableProperty] private string _gpuText = "–";
        [ObservableProperty] private string _gpuDetail = "";
        [ObservableProperty] private double _ramPercent;
        [ObservableProperty] private string _ramText = "–";
        [ObservableProperty] private string _ramDetail = "";

        public SystemCardViewModel(DashboardCardInfo info, ISystemMonitorService monitor, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            _monitor = monitor;
        }

        /// <summary>"58°C", or "" when the sensor is unavailable (negative).</summary>
        public static string TemperatureText(float celsius) => celsius >= 0 ? $"{celsius:F0}°C" : "";

        protected override void OnActiveChanged(bool active)
        {
            if (active)
            {
                _timer ??= CreateTimer();
                _timer.Start();
                _ = RefreshAsync();
            }
            else
            {
                _timer?.Stop();
            }
        }

        private DispatcherTimer CreateTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                if (IsDisposed) return;
                _ = RefreshAsync();
            };
            return timer;
        }

        private async Task RefreshAsync()
        {
            if (IsDisposed) return;
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return; // one sample at a time
            try
            {
                var info = await _monitor.GetSystemInfoAsync().ConfigureAwait(false);
                if (IsDisposed) return;

                float cpu = info.CpuUsage;
                float cpuTemp = info.CpuTemperature;
                float gpu = info.GpuInfo.Usage;
                float gpuTemp = info.GpuInfo.GpuTemperature;
                double total = info.MemoryInfo.TotalPhysicalMemory;
                double used = info.MemoryInfo.UsedPhysicalMemory;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsDisposed) return;
                    CpuPercent = cpu;
                    CpuText = $"{cpu:F0}%";
                    CpuDetail = TemperatureText(cpuTemp);

                    // The GPU load is -1 when unavailable: no bar and "N/A" rather than "-1%".
                    GpuPercent = Math.Max(0, gpu);
                    GpuText = gpu < 0 ? "N/A" : $"{gpu:F0}%";
                    GpuDetail = TemperatureText(gpuTemp);

                    double ram = total > 0 ? used / total * 100.0 : 0;
                    RamPercent = ram;
                    RamText = $"{ram:F0}%";
                    RamDetail = total > 0 ? $"{used:F1} / {total:F1} GB" : "";
                });
            }
            catch (InvalidOperationException) { /* dispatcher shut down — app is closing */ }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "System card: failed to refresh system info");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _timer?.Stop();
                _timer = null;
            }
            base.Dispose(disposing);
        }
    }
}
