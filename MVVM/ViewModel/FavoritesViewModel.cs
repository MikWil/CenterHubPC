using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CenterHubNew.MVVM.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class FavoritesViewModel : BaseViewModel
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _timer;

        [ObservableProperty] private float cpuUsage;
        [ObservableProperty] private float gpuUsage;
        [ObservableProperty] private float memoryUsagePercent;
        [ObservableProperty] private long usedMemory;
        [ObservableProperty] private long totalMemory;
        [ObservableProperty] private float cpuTemperature;
        [ObservableProperty] private float gpuTemperature;
        [ObservableProperty] private string gpuName = string.Empty;

        public SoundViewModel Sound { get; }

        public FavoritesViewModel(
            ISystemMonitorService monitorService,
            SoundViewModel soundViewModel,
            ILogger<FavoritesViewModel>? logger = null) : base(logger)
        {
            _monitorService = monitorService;
            Sound = soundViewModel;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => _ = RefreshAsync();
            _timer.Start();
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (IsDisposed) return;
            try
            {
                var info = await _monitorService.GetSystemInfoAsync().ConfigureAwait(false);
                if (IsDisposed) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsDisposed) return;
                    CpuUsage = info.CpuUsage;
                    CpuTemperature = info.CpuTemperature;
                    GpuUsage = info.GpuInfo.Usage;
                    GpuTemperature = info.GpuInfo.GpuTemperature;
                    GpuName = info.GpuInfo.Name;
                    TotalMemory = info.MemoryInfo.TotalPhysicalMemory;
                    UsedMemory = info.MemoryInfo.UsedPhysicalMemory;
                    MemoryUsagePercent = TotalMemory > 0 ? (float)UsedMemory / TotalMemory * 100f : 0f;
                });
            }
            catch (InvalidOperationException) { /* dispatcher shut down — app is closing */ }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "FavoritesViewModel: failed to refresh system info");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
                _timer.Stop();
            base.Dispose(disposing);
        }
    }
}
