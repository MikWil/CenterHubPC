using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MainViewModel : BaseViewModel, IShellHost
    {
        // Sidebar geometry and the window widths where Auto switches layout.
        private const double ExpandedWidth = 220;
        private const double RailWidth = 56;
        private const double DrawerWidth = 260;
        private const double AutoExpandedMinWidth = 1200;
        private const double DrawerBelowWidth = 760;

        private enum SidebarLayout { Expanded, Rail, Hidden }

        private readonly UpdateService? _updateService;
        private readonly VoicemeeterModeService? _modeService;
        private readonly IVoicemeeterService? _voicemeeter;
        private readonly AudioRoutingService? _routing;
        private readonly UiSettingsService? _uiSettings;

        // Page view-models by page key, resolved from DI on first visit.
        private readonly Dictionary<string, object> _pageVms = new(StringComparer.OrdinalIgnoreCase);

        private SidebarLayout _layout = SidebarLayout.Expanded;
        private double _windowWidth = 1360;
        private double _appliedZoom = 1.0;
        private string _navSignature = "";
        private bool _navigating;
        private IOverlayViewModel? _ownedOverlay; // transient overlay we created (disposed on close)
        private CommandPaletteViewModel? _commandPaletteVm;

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

        // ─── Navigation ───
        [ObservableProperty]
        private object? _currentView;

        /// <summary>Key of the page being shown (also set for pages hidden from the sidebar).</summary>
        [ObservableProperty]
        private string _currentPageKey = "";

        [ObservableProperty]
        private IReadOnlyList<NavGroupViewModel> _navGroups = Array.Empty<NavGroupViewModel>();

        // ─── Sidebar (derived from UiSettings.Sidebar + the window width) ───
        /// <summary>Full sidebar (labels + headers).</summary>
        [ObservableProperty] private bool _isSidebarExpanded = true;
        /// <summary>Icons only; the sidebar gets the "rail" style class.</summary>
        [ObservableProperty] private bool _isSidebarRail;
        /// <summary>No room for a sidebar column: a hamburger opens it as a drawer.</summary>
        [ObservableProperty] private bool _isSidebarHidden;
        [ObservableProperty] private bool _isDrawerOpen;
        /// <summary>Width of the sidebar's grid column (0 when hidden).</summary>
        [ObservableProperty] private GridLength _sidebarWidth = new GridLength(ExpandedWidth);
        /// <summary>Width of the sidebar panel itself (the drawer overlays the content).</summary>
        [ObservableProperty] private double _sidebarPanelWidth = ExpandedWidth;
        [ObservableProperty] private bool _isSidebarPanelVisible = true;

        // ─── Zoom / density / status strip ───
        [ObservableProperty] private ITransform _zoomTransform = new ScaleTransform(1, 1);
        [ObservableProperty] private bool _isCompact;
        [ObservableProperty] private bool _isStatusStripVisible;

        // ─── Overlay layer ───
        [ObservableProperty] private IOverlayViewModel? _overlay;

        public bool HasOverlay => Overlay != null;

        public StatusStripViewModel? StatusStrip { get; }

        /// <summary>Every page, for the command palette.</summary>
        public IReadOnlyList<PageDescriptor> AllPages => PageRegistry.All;

        public string SidebarToggleIcon => IsSidebarRail ? "▶" : "◀";
        public string SidebarToggleTooltip => IsSidebarRail ? "Expand sidebar" : "Collapse sidebar";

        public MainViewModel(
            UpdateService? updateService = null,
            VoicemeeterModeService? modeService = null,
            IVoicemeeterService? voicemeeter = null,
            AudioRoutingService? routing = null,
            UiSettingsService? uiSettings = null,
            ILogger<MainViewModel>? logger = null) : base(logger)
        {
            _updateService = updateService;
            _modeService = modeService
                ?? App.Services?.GetService(typeof(VoicemeeterModeService)) as VoicemeeterModeService;
            _voicemeeter = voicemeeter
                ?? App.Services?.GetService(typeof(IVoicemeeterService)) as IVoicemeeterService;
            _routing = routing
                ?? App.Services?.GetService(typeof(AudioRoutingService)) as AudioRoutingService;
            _uiSettings = uiSettings
                ?? App.Services?.GetService(typeof(UiSettingsService)) as UiSettingsService;
            StatusStrip = App.Services?.GetService(typeof(StatusStripViewModel)) as StatusStripViewModel;

            // Pages, cards, the status strip and the palette drive the shell through this.
            (App.Services?.GetService(typeof(ShellService)) as ShellService)?.Attach(this);

            var settings = _uiSettings?.Current ?? new UiSettings();
            _appliedZoom = settings.Zoom;
            ZoomTransform = new ScaleTransform(_appliedZoom, _appliedZoom);
            IsCompact = settings.Density == UiDensity.Compact;
            IsStatusStripVisible = settings.ShowStatusStrip && StatusStrip is not null;
            RecomputeSidebar();
            RebuildNav(settings);

            // Start page from settings (falls back to Home if it no longer exists).
            NavigateTo(PageRegistry.Find(settings.StartPage)?.Key ?? "home");

            if (_uiSettings is not null)
                _uiSettings.Changed += OnUiSettingsChanged;

            // Subscribe to the update service so the banner appears whenever a
            // check (running in App startup) finds something newer than us.
            if (_updateService is not null)
            {
                _updateService.UpdateChanged += OnUpdateChanged;
                _ = ShowWhatsNewIfUpdatedAsync();
            }

            CheckInterruptedVoicemeeterSession();
            GuardDesktopOutput();
            ScheduleFirstRunSetup();

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

        /// <summary>
        /// Windows' main output must not sit on an app slot (everything on the PC would then go
        /// wherever that slot is routed — e.g. to Discord). The routing service corrects it whenever
        /// the default device changes; this also checks once at startup and tells the user.
        /// </summary>
        private void GuardDesktopOutput()
        {
            if (_routing is null) return;

            _routing.DesktopOutputRestored += OnDesktopOutputRestored;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Banana may still be starting alongside us at sign-in.
                    for (int i = 0; i < 15 && !IsDisposed; i++)
                    {
                        if (_routing.RefreshStatus()) break;
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    if (!IsDisposed) await _routing.KeepDesktopOutputAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { Logger?.LogWarning(ex, "Startup check of Windows' main output failed"); }
            });
        }

        private void OnDesktopOutputRestored(string slotDevice) =>
            ToastService.Instance.Warning(
                $"Windows was playing everything into an app slot ({slotDevice}), so others could hear all your apps. Moved it back to Desktop & Discord.");

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

        // ─── Navigation ───

        /// <summary>
        /// Show a page. Unknown or blank keys go to Home. A page hidden from the sidebar is still
        /// shown (no sidebar item is selected then).
        /// </summary>
        public void NavigateTo(string key)
        {
            if (IsDisposed) return;

            var page = PageRegistry.Find(key) ?? PageRegistry.Find("home");
            if (page is null) return;

            if (!_pageVms.TryGetValue(page.Key, out object? vm))
            {
                vm = App.Services?.GetService(page.ViewModelType);
                if (vm is null)
                {
                    Logger?.LogWarning("No view-model registered for page {Page}", page.Key);
                    return;
                }
                _pageVms[page.Key] = vm;
            }

            CurrentView = vm;
            CurrentPageKey = page.Key;
            SelectNavItem(page.Key);
            IsDrawerOpen = false;
            Logger?.LogDebug("Switched to {Page} view", page.Key);
        }

        // The sidebar RadioButtons bind IsChecked TwoWay to NavItemViewModel.IsSelected. A mouse click
        // and UI automation / keyboard (which only flip IsChecked) both end up here; the guard stops
        // our own selection updates from navigating again.
        private void OnNavItemSelected(NavItemViewModel item)
        {
            if (_navigating || IsDisposed) return;
            NavigateTo(item.Key);
        }

        private IEnumerable<NavItemViewModel> NavItems => NavGroups.SelectMany(g => g.Items);

        private void SelectNavItem(string key) => SelectNavItem(NavItems, key);

        private void SelectNavItem(IEnumerable<NavItemViewModel> items, string key)
        {
            _navigating = true;
            try
            {
                foreach (var item in items)
                    item.IsSelected = string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
            }
            finally { _navigating = false; }
        }

        private void RebuildNav(UiSettings settings)
        {
            foreach (var item in NavItems) item.Selected -= OnNavItemSelected;

            var groups = NavModelBuilder.Build(settings);
            var items = groups.SelectMany(g => g.Items).ToList();
            foreach (var item in items)
            {
                item.Selected += OnNavItemSelected;
                item.Tooltip = IsSidebarRail ? item.Title : item.Description;
            }

            // Select before publishing so the new RadioButtons are created already checked.
            SelectNavItem(items, CurrentPageKey);
            _navSignature = NavModelBuilder.Signature(settings);
            NavGroups = groups;
        }

        private void ApplyNavTooltips()
        {
            foreach (var item in NavItems)
                item.Tooltip = IsSidebarRail ? item.Title : item.Description;
        }

        // ─── Settings → shell ───

        private void OnUiSettingsChanged(UiSettings _)
        {
            if (Dispatcher.UIThread.CheckAccess()) ApplySettings();
            else Dispatcher.UIThread.Post(ApplySettings);
        }

        private void ApplySettings()
        {
            if (IsDisposed || _uiSettings is null) return;
            var s = _uiSettings.Current;

            if (Math.Abs(s.Zoom - _appliedZoom) > 0.0001)
            {
                _appliedZoom = s.Zoom;
                ZoomTransform = new ScaleTransform(_appliedZoom, _appliedZoom);
            }
            IsCompact = s.Density == UiDensity.Compact;
            IsStatusStripVisible = s.ShowStatusStrip && StatusStrip is not null;
            RecomputeSidebar();

            // Window placement is saved through the same service; only rebuild when the sidebar model changed.
            if (NavModelBuilder.Signature(s) != _navSignature)
                RebuildNav(s);
        }

        // ─── Sidebar ───

        /// <summary>The window reports its width so Auto can pick expanded / rail / drawer.</summary>
        public void SetWindowWidth(double width)
        {
            if (!(width > 0) || Math.Abs(width - _windowWidth) < 0.5) return;
            _windowWidth = width;
            RecomputeSidebar();
        }

        private void RecomputeSidebar()
        {
            var mode = _uiSettings?.Current.Sidebar ?? SidebarMode.Auto;

            // Below the drawer threshold there is no room for a column, whatever the setting says.
            SidebarLayout layout;
            if (_windowWidth < DrawerBelowWidth) layout = SidebarLayout.Hidden;
            else layout = mode switch
            {
                SidebarMode.Expanded => SidebarLayout.Expanded,
                SidebarMode.Rail => SidebarLayout.Rail,
                _ => _windowWidth >= AutoExpandedMinWidth ? SidebarLayout.Expanded : SidebarLayout.Rail,
            };
            _layout = layout;

            if (layout != SidebarLayout.Hidden) IsDrawerOpen = false;

            IsSidebarHidden = layout == SidebarLayout.Hidden;
            IsSidebarRail = layout == SidebarLayout.Rail;
            IsSidebarExpanded = layout != SidebarLayout.Rail;
            SidebarWidth = new GridLength(layout switch
            {
                SidebarLayout.Hidden => 0,
                SidebarLayout.Rail => RailWidth,
                _ => ExpandedWidth,
            });
            SidebarPanelWidth = layout switch
            {
                SidebarLayout.Hidden => DrawerWidth,
                SidebarLayout.Rail => RailWidth,
                _ => ExpandedWidth,
            };
            UpdateSidebarPanelVisible();
        }

        private void UpdateSidebarPanelVisible() =>
            IsSidebarPanelVisible = _layout != SidebarLayout.Hidden || IsDrawerOpen;

        partial void OnIsDrawerOpenChanged(bool value) => UpdateSidebarPanelVisible();

        partial void OnIsSidebarRailChanged(bool value)
        {
            ApplyNavTooltips();
            OnPropertyChanged(nameof(SidebarToggleIcon));
            OnPropertyChanged(nameof(SidebarToggleTooltip));
        }

        /// <summary>Bottom button: switch between the full sidebar and the icon rail (remembered).</summary>
        [RelayCommand]
        private void ToggleSidebar()
        {
            if (_uiSettings is null || _layout == SidebarLayout.Hidden) return;
            var next = _layout == SidebarLayout.Expanded ? SidebarMode.Rail : SidebarMode.Expanded;
            _uiSettings.Update(s => s.Sidebar = next);
        }

        /// <summary>Hamburger: open or close the sidebar drawer (only exists while the sidebar is hidden).</summary>
        [RelayCommand]
        private void ToggleDrawer()
        {
            if (!IsSidebarHidden) return;
            IsDrawerOpen = !IsDrawerOpen;
        }

        public void CloseDrawer() => IsDrawerOpen = false;

        /// <summary>For the command palette: collapse/expand the sidebar, or open/close the drawer when there is no room.</summary>
        void IShellHost.ToggleSidebar()
        {
            if (IsSidebarHidden) ToggleDrawer();
            else ToggleSidebar();
        }

        // ─── Zoom ───

        /// <summary>Change the UI zoom by <paramref name="delta"/> (0.1 = 10 %); clamped to 80–150 %.</summary>
        public void ZoomBy(double delta) => SetZoom((_uiSettings?.Current.Zoom ?? 1.0) + delta);

        public void ResetZoom() => SetZoom(1.0);

        private void SetZoom(double zoom)
        {
            if (_uiSettings is null || IsDisposed) return;
            zoom = Math.Round(Math.Clamp(zoom, 0.8, 1.5), 2);
            _uiSettings.Update(s => s.Zoom = zoom);
            ToastService.Instance.Info($"Zoom {(int)Math.Round(zoom * 100)} %");
        }

        // ─── Overlay layer (command palette, setup wizard) ───

        partial void OnOverlayChanged(IOverlayViewModel? value) => OnPropertyChanged(nameof(HasOverlay));

        /// <summary>Show a full-window overlay, closing the current one first.</summary>
        public void ShowOverlay(IOverlayViewModel overlay)
        {
            if (overlay is null || IsDisposed) return;
            if (Overlay is not null) CloseOverlay();

            overlay.CloseRequested += OnOverlayCloseRequested;
            Overlay = overlay;
            try { overlay.OnShown(); }
            catch (Exception ex) { Logger?.LogWarning(ex, "Overlay OnShown failed"); }
        }

        public void CloseOverlay()
        {
            var overlay = Overlay;
            if (overlay is null) return;

            overlay.CloseRequested -= OnOverlayCloseRequested;
            try { overlay.OnClosed(); }
            catch (Exception ex) { Logger?.LogWarning(ex, "Overlay OnClosed failed"); }
            Overlay = null;

            // A wizard we created is ours to dispose; the palette is a shared singleton.
            if (ReferenceEquals(overlay, _ownedOverlay))
            {
                _ownedOverlay = null;
                SafeDispose(overlay as IDisposable);
            }

            // The wizard saves devices behind the Sound page's back — let its pickers re-read them.
            if (overlay is SetupWizardViewModel)
            {
                try
                {
                    var sound = App.Services?.GetService(typeof(SoundViewModel)) as SoundViewModel;
                    sound?.Voicemeeter?.Reload();
                    sound?.Routing?.RefreshState();
                }
                catch (Exception ex) { Logger?.LogDebug(ex, "Could not refresh the Sound page after setup"); }
            }
        }

        private void OnOverlayCloseRequested()
        {
            if (Dispatcher.UIThread.CheckAccess()) CloseOverlay();
            else Dispatcher.UIThread.Post(CloseOverlay);
        }

        public void ShowCommandPalette()
        {
            if (IsDisposed) return;
            _commandPaletteVm ??= App.Services?.GetService(typeof(CommandPaletteViewModel)) as CommandPaletteViewModel;
            object? vm = _commandPaletteVm;
            if (vm is IOverlayViewModel overlay) ShowOverlay(overlay);
            else Logger?.LogDebug("Command palette is not an overlay yet");
        }

        public void ShowSetupWizard()
        {
            if (IsDisposed) return;
            var vm = App.Services?.GetService(typeof(SetupWizardViewModel));
            if (vm is IOverlayViewModel overlay)
            {
                _ownedOverlay = overlay;
                ShowOverlay(overlay);
            }
            else
            {
                SafeDispose(vm as IDisposable);
                Logger?.LogDebug("Setup wizard is not an overlay yet");
            }
        }

        /// <summary>
        /// First run with Voicemeeter installed but no headphones chosen yet: offer the audio setup
        /// wizard. The checks (registry, JSON) run off the UI thread; the overlay opens on it.
        /// </summary>
        private void ScheduleFirstRunSetup()
        {
            if (_uiSettings is null || _uiSettings.Current.AudioSetupCompleted || _voicemeeter is null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_voicemeeter.IsInstalled) return;
                    var settingsService = App.Services?.GetService(typeof(VoicemeeterSettingsService)) as VoicemeeterSettingsService;
                    if (settingsService is null) return;
                    if (!string.IsNullOrWhiteSpace(settingsService.Load().Settings.MonitorDeviceName)) return;

                    // Let the window finish appearing before covering it.
                    await Task.Delay(1500).ConfigureAwait(false);

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed || _uiSettings.Current.AudioSetupCompleted || Overlay is not null) return;
                        ShowSetupWizard();
                    });
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "First-run setup check failed");
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                if (_updateService is not null)
                    _updateService.UpdateChanged -= OnUpdateChanged;
                if (_routing is not null)
                    _routing.DesktopOutputRestored -= OnDesktopOutputRestored;
                if (_uiSettings is not null)
                    _uiSettings.Changed -= OnUiSettingsChanged;

                foreach (var item in NavItems) item.Selected -= OnNavItemSelected;
                try { CloseOverlay(); }
                catch (Exception ex) { Logger?.LogWarning(ex, "Error closing the overlay"); }

                // Dispose each page independently: one throwing Dispose used to skip the rest
                // (including Notes, whose Dispose saves the open note). Notes goes first.
                var pages = _pageVms.Values.ToList();
                foreach (var notes in pages.OfType<QuickNotesViewModel>()) SafeDispose(notes);
                foreach (var page in pages)
                {
                    if (page is QuickNotesViewModel) continue;
                    SafeDispose(page as IDisposable);
                }
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
