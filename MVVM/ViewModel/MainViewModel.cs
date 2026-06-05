using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MainViewModel : BaseViewModel
    {
        private readonly UpdateService? _updateService;

        private MonitoringViewModel? _monitoringVM;
        private SoundViewModel? _soundVM;
        private SoundboardViewModel? _soundboardVM;
        private UtilitiesViewModel? _utilitiesVM;
        private AutoClickerViewModel? _autoClickerVM;
        private ClipboardViewModel? _clipboardVM;
        private StandingViewModel? _standingVM;
        private QuickNotesViewModel? _notesVM;
        private HotkeySettingsViewModel? _hotkeySettingsVM;
        private WindowLayoutsViewModel? _layoutsVM;
        private NetworkViewModel? _networkVM;
        private RandomizerViewModel? _randomizerVM;
        private MetronomeViewModel? _metronomeVM;

        // ─── Update banner state ───
        [ObservableProperty] private bool   _isUpdateAvailable;
        [ObservableProperty] private string _updateVersion = "";
        [ObservableProperty] private string _updateHeadline = "Update available";
        [ObservableProperty] private string _updateBodyPreview = "";
        [ObservableProperty] private bool   _isDownloadingUpdate;
        [ObservableProperty] private double _downloadProgressPercent;
        [ObservableProperty] private string _updateActionText = "Download & install";

        [ObservableProperty]
        private object? _currentView;

        [ObservableProperty]
        private bool isMonitoringSelected = true;

        [ObservableProperty]
        private bool isSoundSelected = false;

        [ObservableProperty]
        private bool isSoundboardSelected = false;

        [ObservableProperty]
        private bool isUtilitiesSelected = false;

        [ObservableProperty]
        private bool isAutoClickerSelected = false;

        [ObservableProperty]
        private bool isClipboardSelected = false;

        [ObservableProperty]
        private bool isStandingSelected = false;

        [ObservableProperty]
        private bool isNotesSelected = false;

        [ObservableProperty]
        private bool isHotkeySettingsSelected = false;

        [ObservableProperty]
        private bool isLayoutsSelected = false;

        [ObservableProperty]
        private bool isNetworkSelected = false;

        [ObservableProperty]
        private bool isRandomizerSelected = false;

        [ObservableProperty]
        private bool isMetronomeSelected = false;

        [ObservableProperty]
        private bool isSidebarExpanded = true;

        [ObservableProperty]
        private GridLength sidebarWidth = new GridLength(220);

        public string SidebarToggleIcon => IsSidebarExpanded ? "◀" : "▶";
        public string SidebarToggleText => IsSidebarExpanded ? "Collapse" : "";

        public MainViewModel(
            UpdateService? updateService = null,
            ILogger<MainViewModel>? logger = null) : base(logger)
        {
            _updateService = updateService;

            // Set initial view to Monitoring
            MonitoringView();

            // Subscribe to the update service so the banner appears whenever a
            // check (running in App startup) finds something newer than us.
            if (_updateService is not null)
            {
                _updateService.UpdateChanged += OnUpdateChanged;
            }

            Logger?.LogInformation("MainViewModel initialized");
        }

        // ─── Update banner ───

        private void OnUpdateChanged(UpdateInfo? info)
        {
            // Always marshal to UI thread — UpdateService raises this from the background check task
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed) return;
                if (info is null)
                {
                    IsUpdateAvailable = false;
                    return;
                }
                IsUpdateAvailable = true;
                UpdateVersion = info.Version;
                UpdateHeadline = $"v{info.Version} is ready to install";
                UpdateBodyPreview = ShortenBody(info.Body);
            });
        }

        private static string ShortenBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "Click Download & install to upgrade.";
            // Strip markdown headers/badges, take first non-empty line, cap length.
            var line = "";
            foreach (var raw in body.Split('\n'))
            {
                var t = raw.Trim().TrimStart('#', '>', '-', '*', ' ').Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (t.StartsWith("![")) continue;       // image badges
                if (t.StartsWith("[")) continue;        // link-only lines
                line = t; break;
            }
            if (string.IsNullOrEmpty(line)) line = body.Trim();
            return line.Length > 140 ? line[..140] + "…" : line;
        }

        [RelayCommand]
        private async Task DownloadAndInstallUpdate()
        {
            if (_updateService?.AvailableUpdate is null || IsDownloadingUpdate) return;

            IsDownloadingUpdate = true;
            UpdateActionText = "Downloading…";
            DownloadProgressPercent = 0;

            var progress = new Progress<(long d, long t)>(tuple =>
            {
                if (tuple.t > 0)
                    DownloadProgressPercent = (double)tuple.d / tuple.t * 100.0;
            });

            string? msiPath = null;
            try
            {
                msiPath = await _updateService.DownloadAsync(progress);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Update download failed");
            }

            if (string.IsNullOrEmpty(msiPath))
            {
                IsDownloadingUpdate = false;
                UpdateActionText = "Download failed — retry";
                DownloadProgressPercent = 0;
                ToastService.Instance.Error("Couldn't download update. Try again later.");
                return;
            }

            UpdateActionText = "Launching installer…";
            ToastService.Instance.Info("Installer launching — CenterHub will close to complete the upgrade.");

            // Small delay so the user sees the toast before we yank the window
            await Task.Delay(800);

            var ok = _updateService.LaunchInstaller(msiPath);
            if (!ok)
            {
                IsDownloadingUpdate = false;
                UpdateActionText = "Could not start msiexec — retry";
                ToastService.Instance.Error("Could not start the installer. See logs for details.");
                return;
            }

            // Shutdown — the closing event recognizes ApplicationShutdown and
            // skips the exit-confirmation prompt automatically.
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                            as IClassicDesktopStyleApplicationLifetime;
            lifetime?.Shutdown();
        }

        [RelayCommand]
        private void SkipUpdate()
        {
            _updateService?.SkipCurrent();
            IsUpdateAvailable = false;
            ToastService.Instance.Info($"Skipped v{UpdateVersion}. You'll be prompted again on the next release.");
        }

        [RelayCommand]
        private void DismissUpdate()
        {
            _updateService?.DismissForSession();
            IsUpdateAvailable = false;
        }

        private void DeselectAll()
        {
            IsMonitoringSelected = false;
            IsSoundSelected = false;
            IsSoundboardSelected = false;
            IsUtilitiesSelected = false;
            IsAutoClickerSelected = false;
            IsClipboardSelected = false;
            IsStandingSelected = false;
            IsNotesSelected = false;
            IsHotkeySettingsSelected = false;
            IsLayoutsSelected = false;
            IsNetworkSelected = false;
            IsRandomizerSelected = false;
            IsMetronomeSelected = false;
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
            SidebarWidth = IsSidebarExpanded ? new GridLength(220) : new GridLength(52);
            OnPropertyChanged(nameof(SidebarToggleIcon));
            OnPropertyChanged(nameof(SidebarToggleText));
        }

        [RelayCommand]
        private void MonitoringView()
        {
            ThrowIfDisposed();
            _monitoringVM ??= App.Services.GetService(typeof(MonitoringViewModel)) as MonitoringViewModel;
            if (_monitoringVM != null)
            {
                CurrentView = _monitoringVM;
                DeselectAll();
                IsMonitoringSelected = true;
                Logger?.LogDebug("Switched to Monitoring view");
            }
        }

        [RelayCommand]
        private void SoundView()
        {
            ThrowIfDisposed();
            _soundVM ??= App.Services.GetService(typeof(SoundViewModel)) as SoundViewModel;
            if (_soundVM != null)
            {
                CurrentView = _soundVM;
                DeselectAll();
                IsSoundSelected = true;
                Logger?.LogDebug("Switched to Sound view");
            }
        }

        [RelayCommand]
        private void SoundboardView()
        {
            ThrowIfDisposed();
            _soundboardVM ??= App.Services.GetService(typeof(SoundboardViewModel)) as SoundboardViewModel;
            if (_soundboardVM != null)
            {
                CurrentView = _soundboardVM;
                DeselectAll();
                IsSoundboardSelected = true;
                Logger?.LogDebug("Switched to Soundboard view");
            }
        }

        [RelayCommand]
        private void UtilitiesView()
        {
            ThrowIfDisposed();
            _utilitiesVM ??= App.Services.GetService(typeof(UtilitiesViewModel)) as UtilitiesViewModel;
            if (_utilitiesVM != null)
            {
                CurrentView = _utilitiesVM;
                DeselectAll();
                IsUtilitiesSelected = true;
                Logger?.LogDebug("Switched to Utilities view");
            }
        }

        [RelayCommand]
        private void AutoClickerView()
        {
            ThrowIfDisposed();
            _autoClickerVM ??= App.Services.GetService(typeof(AutoClickerViewModel)) as AutoClickerViewModel;
            if (_autoClickerVM != null)
            {
                CurrentView = _autoClickerVM;
                DeselectAll();
                IsAutoClickerSelected = true;
                Logger?.LogDebug("Switched to AutoClicker view");
            }
        }

        [RelayCommand]
        private void ClipboardView()
        {
            ThrowIfDisposed();
            _clipboardVM ??= App.Services.GetService(typeof(ClipboardViewModel)) as ClipboardViewModel;
            if (_clipboardVM != null)
            {
                CurrentView = _clipboardVM;
                DeselectAll();
                IsClipboardSelected = true;
                Logger?.LogDebug("Switched to Clipboard view");
            }
        }

        [RelayCommand]
        private void StandingView()
        {
            ThrowIfDisposed();
            _standingVM ??= App.Services.GetService(typeof(StandingViewModel)) as StandingViewModel;
            if (_standingVM != null)
            {
                CurrentView = _standingVM;
                DeselectAll();
                IsStandingSelected = true;
                Logger?.LogDebug("Switched to Standing view");
            }
        }

        [RelayCommand]
        private void NotesView()
        {
            ThrowIfDisposed();
            _notesVM ??= App.Services.GetService(typeof(QuickNotesViewModel)) as QuickNotesViewModel;
            if (_notesVM != null)
            {
                CurrentView = _notesVM;
                DeselectAll();
                IsNotesSelected = true;
                Logger?.LogDebug("Switched to Notes view");
            }
        }

        [RelayCommand]
        private void HotkeySettingsView()
        {
            ThrowIfDisposed();
            _hotkeySettingsVM ??= App.Services.GetService(typeof(HotkeySettingsViewModel)) as HotkeySettingsViewModel;
            if (_hotkeySettingsVM != null)
            {
                CurrentView = _hotkeySettingsVM;
                DeselectAll();
                IsHotkeySettingsSelected = true;
                Logger?.LogDebug("Switched to Hotkey Settings view");
            }
        }

        [RelayCommand]
        private void LayoutsView()
        {
            ThrowIfDisposed();
            _layoutsVM ??= App.Services.GetService(typeof(WindowLayoutsViewModel)) as WindowLayoutsViewModel;
            if (_layoutsVM != null)
            {
                CurrentView = _layoutsVM;
                DeselectAll();
                IsLayoutsSelected = true;
                Logger?.LogDebug("Switched to Window Layouts view");
            }
        }

        [RelayCommand]
        private void NetworkView()
        {
            ThrowIfDisposed();
            _networkVM ??= App.Services.GetService(typeof(NetworkViewModel)) as NetworkViewModel;
            if (_networkVM != null)
            {
                CurrentView = _networkVM;
                DeselectAll();
                IsNetworkSelected = true;
                Logger?.LogDebug("Switched to Network view");
            }
        }

        [RelayCommand]
        private void RandomizerView()
        {
            ThrowIfDisposed();
            _randomizerVM ??= App.Services.GetService(typeof(RandomizerViewModel)) as RandomizerViewModel;
            if (_randomizerVM != null)
            {
                CurrentView = _randomizerVM;
                DeselectAll();
                IsRandomizerSelected = true;
                Logger?.LogDebug("Switched to Randomizer view");
            }
        }

        [RelayCommand]
        private void MetronomeView()
        {
            ThrowIfDisposed();
            _metronomeVM ??= App.Services.GetService(typeof(MetronomeViewModel)) as MetronomeViewModel;
            if (_metronomeVM != null)
            {
                CurrentView = _metronomeVM;
                DeselectAll();
                IsMetronomeSelected = true;
                Logger?.LogDebug("Switched to Metronome view");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                if (_updateService is not null)
                    _updateService.UpdateChanged -= OnUpdateChanged;

                if (CurrentView is IDisposable currentDisposable)
                {
                    currentDisposable.Dispose();
                }

                _monitoringVM?.Dispose();
                _soundVM?.Dispose();
                _soundboardVM?.Dispose();
                _utilitiesVM?.Dispose();
                _autoClickerVM?.Dispose();
                _clipboardVM?.Dispose();
                _standingVM?.Dispose();
                _notesVM?.Dispose();
                _hotkeySettingsVM?.Dispose();
                _layoutsVM?.Dispose();
                _networkVM?.Dispose();
                _randomizerVM?.Dispose();
                _metronomeVM?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
    }
}
