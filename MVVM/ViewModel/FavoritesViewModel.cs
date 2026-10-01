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
        [ObservableProperty] private double usedMemory;
        [ObservableProperty] private double totalMemory;
        [ObservableProperty] private float cpuTemperature;
        [ObservableProperty] private float gpuTemperature;
        [ObservableProperty] private string gpuName = string.Empty;

        // GpuUsage is -1 when unavailable: show "N/A" and an empty bar rather than "-1.0%".
        public string GpuUsageText => GpuUsage < 0 ? "N/A" : $"{GpuUsage:F1}%";
        public float GpuUsageBar => Math.Max(0f, GpuUsage);
        partial void OnGpuUsageChanged(float value)
        {
            OnPropertyChanged(nameof(GpuUsageText));
            OnPropertyChanged(nameof(GpuUsageBar));
        }

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
                    MemoryUsagePercent = TotalMemory > 0 ? (float)(UsedMemory / TotalMemory * 100.0) : 0f;
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
