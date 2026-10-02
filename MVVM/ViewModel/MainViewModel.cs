using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MainViewModel : BaseViewModel
    {
        private readonly UpdateService? _updateService;
        private readonly VoicemeeterModeService? _modeService;
        private readonly IVoicemeeterService? _voicemeeter;
        private readonly AudioRoutingService? _routing;

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

        // ─── "What's new" popup (shown once after an update) ───
        [ObservableProperty] private bool   _isWhatsNewOpen;
        [ObservableProperty] private string _whatsNewTitle = "";
        [ObservableProperty] private string _whatsNewBody  = "";

        // ─── Voicemeeter crash-recovery prompt ───
        [ObservableProperty] private bool   _isAudioRestoreOpen;
        [ObservableProperty] private string _audioRestoreBody =
            "Windows audio is still routed through Voicemeeter, but Voicemeeter isn't running — you may hear nothing. " +
            "Start Voicemeeter to bring your routing back, or switch back to your previous audio devices.";

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
            VoicemeeterModeService? modeService = null,
            IVoicemeeterService? voicemeeter = null,
            AudioRoutingService? routing = null,
            ILogger<MainViewModel>? logger = null) : base(logger)
        {
            _updateService = updateService;
            _modeService = modeService
                ?? App.Services?.GetService(typeof(VoicemeeterModeService)) as VoicemeeterModeService;
            _voicemeeter = voicemeeter
                ?? App.Services?.GetService(typeof(IVoicemeeterService)) as IVoicemeeterService;
            _routing = routing
                ?? App.Services?.GetService(typeof(AudioRoutingService)) as AudioRoutingService;

            // Set initial view to Monitoring
            MonitoringView();

            // Subscribe to the update service so the banner appears whenever a
            // check (running in App startup) finds something newer than us.
            if (_updateService is not null)
            {
                _updateService.UpdateChanged += OnUpdateChanged;
                _ = ShowWhatsNewIfUpdatedAsync();
            }

            CheckInterruptedVoicemeeterSession();

            Logger?.LogInformation("MainViewModel initialized");
        }

        // ─── Voicemeeter crash-recovery ───

        /// <summary>
        /// With the Sound board, Voicemeeter routing being active at exit is the normal state, so
        /// only speak up when it's actually broken: routing is engaged but Banana isn't running
        /// (Windows still points at Voicemeeter's virtual devices → silence).
        /// </summary>
        private void CheckInterruptedVoicemeeterSession()
        {
            if (_modeService is null || _voicemeeter is null) return;
            _ = Task.Run(() =>
            {
                try
                {
                    if (!_modeService.HasInterruptedSession(out _)) return;
                    if (!_voicemeeter.IsInstalled) return;

                    // Banana often starts alongside CenterHub (both at sign-in) and needs a few
                    // seconds; only prompt if it's still down after ~12 s.
                    for (int i = 0; i < 12; i++)
                    {
                        if (_voicemeeter.RefreshStatus() == VoicemeeterStatus.Running) return; // routing intact
                        System.Threading.Thread.Sleep(1000);
                    }
                    if (_voicemeeter.RefreshStatus() == VoicemeeterStatus.Running) return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed) return;
                        if (_modeService.AutoRestoreEnabled) _ = RestoreAudioNowAsync();
                        else IsAudioRestoreOpen = true;
                    });
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Voicemeeter startup check failed");
                }
            });
        }

        /// <summary>Bring routing back: relaunch Banana and reapply the active Sound preset.</summary>
        [RelayCommand]
        private async Task StartVoicemeeterNowAsync()
        {
            IsAudioRestoreOpen = false;
            if (_routing is null) return;
            try
            {
                var presets = _routing.LoadPresets();
                var active = presets.FirstOrDefault(p => p.Id == _routing.ActivePresetId) ?? presets.FirstOrDefault();
                if (active is null) return;
                ToastService.Instance.Info("Starting Voicemeeter…");
                var result = await _routing.ApplyPresetAsync(active);
                if (result.Success) ToastService.Instance.Success(result.Message);
                else ToastService.Instance.Error(result.Message);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to start Voicemeeter from the startup prompt");
                ToastService.Instance.Error("Could not start Voicemeeter.");
            }
        }

        [RelayCommand]
        private async Task RestoreAudioNowAsync()
        {
            IsAudioRestoreOpen = false;
            if (_modeService is null) return;
            try
            {
                var result = await _modeService.RestoreInterruptedAsync();
                if (result.Success) ToastService.Instance.Success(result.Message);
                else ToastService.Instance.Warning(result.Message);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to restore audio after interrupted session");
                ToastService.Instance.Error("Could not restore audio devices.");
            }
        }

        [RelayCommand]
        private void DismissAudioRestore()
        {
            // Leave everything as-is. The prompt only appears while Voicemeeter is down, so if it
            // still is next launch, asking again is the right thing to do.
            IsAudioRestoreOpen = false;
        }

        // ─── "What's new" popup ───

        private async Task ShowWhatsNewIfUpdatedAsync()
        {
            try
            {
                var notes = await _updateService!.TryGetUpdateNotesAsync().ConfigureAwait(false);
                if (notes is null) return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (IsDisposed) return;
                    WhatsNewTitle = $"What's new in v{notes.Value.version}";
                    WhatsNewBody  = CleanReleaseNotes(notes.Value.body);
                    IsWhatsNewOpen = true;
                });
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to show What's New popup");
            }
        }

        [RelayCommand]
        private void CloseWhatsNew() => IsWhatsNewOpen = false;

        /// <summary>Lightly de-markdown the GitHub release body for plain-text display.</summary>
        private static string CleanReleaseNotes(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("![")) continue;        // image badges
                if (trimmed == "---" || trimmed == "***") continue; // horizontal rules

                // Headings → bare text
                while (trimmed.StartsWith("#")) trimmed = trimmed[1..];
                // Bullets → •
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    trimmed = "  • " + trimmed[2..];
                // Strip inline emphasis/backticks
                trimmed = trimmed.Replace("**", "").Replace("`", "");

                sb.AppendLine(trimmed.TrimEnd());
            }
            return sb.ToString().Trim();
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
            ToastService.Instance.Info("Updating — CenterHub will close and reopen automatically when it's done.");

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

        // ─── Selection flag → navigation ───
        // The sidebar RadioButtons bind IsChecked TwoWay to these flags. A mouse click also
        // runs the Command, but keyboard arrows (and accessibility tools) only flip IsChecked —
        // which used to move the highlight without changing the page. Navigate from the flag too.
        private bool _navigatingFromFlag;

        private void NavigateFromFlag(bool selected, Action navigate)
        {
            if (!selected || _navigatingFromFlag) return;
            _navigatingFromFlag = true;
            try { navigate(); }
            finally { _navigatingFromFlag = false; }
        }

        partial void OnIsMonitoringSelectedChanged(bool value)       => NavigateFromFlag(value, MonitoringView);
        partial void OnIsSoundSelectedChanged(bool value)            => NavigateFromFlag(value, SoundView);
        partial void OnIsSoundboardSelectedChanged(bool value)       => NavigateFromFlag(value, SoundboardView);
        partial void OnIsUtilitiesSelectedChanged(bool value)        => NavigateFromFlag(value, UtilitiesView);
        partial void OnIsAutoClickerSelectedChanged(bool value)      => NavigateFromFlag(value, AutoClickerView);
        partial void OnIsClipboardSelectedChanged(bool value)        => NavigateFromFlag(value, ClipboardView);
        partial void OnIsStandingSelectedChanged(bool value)         => NavigateFromFlag(value, StandingView);
        partial void OnIsNotesSelectedChanged(bool value)            => NavigateFromFlag(value, NotesView);
        partial void OnIsHotkeySettingsSelectedChanged(bool value)   => NavigateFromFlag(value, HotkeySettingsView);
        partial void OnIsLayoutsSelectedChanged(bool value)          => NavigateFromFlag(value, LayoutsView);
        partial void OnIsNetworkSelectedChanged(bool value)          => NavigateFromFlag(value, NetworkView);
        partial void OnIsRandomizerSelectedChanged(bool value)       => NavigateFromFlag(value, RandomizerView);
        partial void OnIsMetronomeSelectedChanged(bool value)        => NavigateFromFlag(value, MetronomeView);

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

                // Dispose each page independently: one throwing Dispose used to skip the rest
                // (including Notes, whose Dispose saves the open note). Notes goes first.
                SafeDispose(_notesVM);
                SafeDispose(CurrentView as IDisposable);
                SafeDispose(_monitoringVM);
                SafeDispose(_soundVM);
                SafeDispose(_soundboardVM);
                SafeDispose(_utilitiesVM);
                SafeDispose(_autoClickerVM);
                SafeDispose(_clipboardVM);
                SafeDispose(_standingVM);
                SafeDispose(_hotkeySettingsVM);
                SafeDispose(_layoutsVM);
                SafeDispose(_networkVM);
                SafeDispose(_randomizerVM);
                SafeDispose(_metronomeVM);
            }
            base.Dispose(disposing);
        }

        private void SafeDispose(IDisposable? d)
        {
            if (d is null) return;
            try { d.Dispose(); }
            catch (Exception ex) { Logger?.LogWarning(ex, "Error disposing {Type}", d.GetType().Name); }
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }
}
