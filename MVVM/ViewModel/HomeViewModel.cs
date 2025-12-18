using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Windows.Threading;
using System.Diagnostics;
using CenterHubNew.MVVM.Services;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class HomeViewModel : BaseViewModel
    {
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _systemMonitorTimer;
        private readonly ISystemMonitorService _systemMonitorService;
        private readonly SoundViewModel _soundViewModel;

        [ObservableProperty]
        private string topLeftMessageDate = string.Empty;

        [ObservableProperty]
        private string topLeftMessageClock = string.Empty;

        [ObservableProperty]
        private float cpuUsage;

        [ObservableProperty]
        private float cpuTemperature;

        [ObservableProperty]
        private float cpuMaxTemperature;

        [ObservableProperty]
        private float gpuUsage;

        [ObservableProperty]
        private string gpuName = string.Empty;

        [ObservableProperty]
        private string gpuDriverVersion = string.Empty;

        [ObservableProperty]
        private long gpuMemory;

        [ObservableProperty]
        private float gpuTemperature;

        [ObservableProperty]
        private float gpuMaxTemperature;

        [ObservableProperty]
        private long totalMemory;

        [ObservableProperty]
        private long usedMemory;

        [ObservableProperty]
        private long totalDiskSpace;

        [ObservableProperty]
        private long usedDiskSpace;

        [ObservableProperty]
        private List<DiskInfo> disks = new();

        [ObservableProperty]
        private float memoryUsagePercent;

        // Expose ViewModels for unified dashboard
        public SoundViewModel SoundViewModel => _soundViewModel;
        
        private StandingViewModel? _standingViewModel;
        private MoveFilesViewModel? _moveFilesViewModel;
        private ComputerViewModel? _computerViewModel;
        private NameInputViewModel? _nameInputViewModel;

        public StandingViewModel StandingViewModel
        {
            get
            {
                if (_standingViewModel == null)
                {
                    _standingViewModel = App.Services.GetService(typeof(StandingViewModel)) as StandingViewModel;
                }
                return _standingViewModel!;
            }
        }

        public MoveFilesViewModel MoveFilesViewModel
        {
            get
            {
                if (_moveFilesViewModel == null)
                {
                    _moveFilesViewModel = App.Services.GetService(typeof(MoveFilesViewModel)) as MoveFilesViewModel;
                }
                return _moveFilesViewModel!;
            }
        }

        public ComputerViewModel ComputerViewModel
        {
            get
            {
                if (_computerViewModel == null)
                {
                    _computerViewModel = App.Services.GetService(typeof(ComputerViewModel)) as ComputerViewModel;
                }
                return _computerViewModel!;
            }
        }

        public NameInputViewModel NameInputViewModel
        {
            get
            {
                if (_nameInputViewModel == null)
                {
                    _nameInputViewModel = App.Services.GetService(typeof(NameInputViewModel)) as NameInputViewModel;
                }
                return _nameInputViewModel!;
            }
        }

        public HomeViewModel(
            ISystemMonitorService systemMonitorService,
            SoundViewModel soundViewModel,
            ILogger<HomeViewModel>? logger = null) : base(logger)
        {
            _systemMonitorService = systemMonitorService ?? throw new ArgumentNullException(nameof(systemMonitorService));
            _soundViewModel = soundViewModel ?? throw new ArgumentNullException(nameof(soundViewModel));

            var dateTime = DateTime.Now;
            TopLeftMessageDate = $"{dateTime:M} {dateTime:yyyy}";
            TopLeftMessageClock = dateTime.ToString("HH:mm:ss");

            // Initialize clock update timer (every second)
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += ClockTimer_Tick;

            // Initialize system monitor timer (every 2 seconds)
            _systemMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _systemMonitorTimer.Tick += SystemMonitorTimer_Tick;

            // Start timers
            StartTimers();
            _ = UpdateSystemInfoAsync(); // Initial system info fetch
            
            Logger?.LogInformation("HomeViewModel initialized");
        }

        private void StartTimers()
        {
            if (!IsDisposed)
            {
                _clockTimer.Start();
                _systemMonitorTimer.Start();
                Logger?.LogDebug("Timers started");
            }
        }

        private void StopTimers()
        {
            _clockTimer.Stop();
            _systemMonitorTimer.Stop();
            Logger?.LogDebug("Timers stopped");
        }

        private void ClockTimer_Tick(object? sender, EventArgs e)
        {
            UpdateClock();
        }

        private async void SystemMonitorTimer_Tick(object? sender, EventArgs e)
        {
            await UpdateSystemInfoAsync();
        }

        private async Task UpdateSystemInfoAsync()
        {
            if (IsDisposed) return;

            try
            {
                var systemInfo = await _systemMonitorService.GetSystemInfoAsync();
                
                CpuUsage = systemInfo.CpuUsage;
                CpuTemperature = systemInfo.CpuTemperature;
                CpuMaxTemperature = systemInfo.CpuMaxTemperature;
                GpuName = systemInfo.GpuInfo.Name;
                GpuUsage = systemInfo.GpuInfo.Usage;
                GpuDriverVersion = systemInfo.GpuInfo.DriverVersion;
                GpuMemory = systemInfo.GpuInfo.VideoMemory;
                GpuTemperature = systemInfo.GpuInfo.GpuTemperature;
                GpuMaxTemperature = systemInfo.GpuInfo.GpuMaxTemperature;
                TotalMemory = systemInfo.MemoryInfo.TotalPhysicalMemory;
                UsedMemory = systemInfo.MemoryInfo.UsedPhysicalMemory;
                Disks = systemInfo.Disks;
                MemoryUsagePercent = TotalMemory > 0 ? (float)UsedMemory / TotalMemory * 100 : 0;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error updating system info");
                Debug.WriteLine($"Error updating system info: {ex.Message}");
            }
        }

        private void UpdateClock()
        {
            if (IsDisposed) return;

            var now = DateTime.Now;
            TopLeftMessageClock = now.ToString("HH:mm:ss");
            
            // Update date if day changed
            var currentDate = $"{now:M} {now:yyyy}";
            if (TopLeftMessageDate != currentDate)
            {
                TopLeftMessageDate = currentDate;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                StopTimers();
                
                // Unsubscribe from events
                if (_clockTimer != null)
                {
                    _clockTimer.Tick -= ClockTimer_Tick;
                }
                if (_systemMonitorTimer != null)
                {
                    _systemMonitorTimer.Tick -= SystemMonitorTimer_Tick;
                }

                Logger?.LogInformation("HomeViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
