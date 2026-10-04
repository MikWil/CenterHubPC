using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>One accent-colour choice on the Settings page.</summary>
    public sealed partial class AccentSwatch : ObservableObject
    {
        private readonly SettingsViewModel _owner;

        /// <param name="name">Shown as a tooltip and next to the swatches.</param>
        /// <param name="storedValue">What goes into <c>UiSettings.AccentColor</c> ("#RRGGBB", or null for CenterHub blue).</param>
        /// <param name="displayHex">The colour painted on the swatch.</param>
        public AccentSwatch(string name, string? storedValue, string displayHex, SettingsViewModel owner)
        {
            Name = name;
            StoredValue = storedValue;
            Brush = new SolidColorBrush(Color.Parse(displayHex));
            _owner = owner;
        }

        public string Name { get; }
        public string? StoredValue { get; }
        public IBrush Brush { get; }

        [ObservableProperty] private bool isSelected;

        [RelayCommand]
        private void Select() => _owner.SelectAccent(this);
    }

    /// <summary>One page in the Settings page's sidebar list: show / pin / move.</summary>
    public sealed partial class SidebarPageRow : ObservableObject
    {
        private readonly SettingsViewModel _owner;

        public SidebarPageRow(PageDescriptor page, SettingsViewModel owner)
        {
            Key = page.Key;
            Title = page.Title;
            Glyph = page.Glyph;
            Group = page.Group;
            CanHide = !SettingsViewModel.IsProtectedPage(page.Key);
            _owner = owner;
        }

        public string Key { get; }
        public string Title { get; }
        public string Glyph { get; }
        public string Group { get; }

        /// <summary>Home and Settings always stay in the sidebar.</summary>
        public bool CanHide { get; }

        [ObservableProperty] private bool isShown = true;
        [ObservableProperty] private bool isPinned;
        [ObservableProperty] private bool canMoveUp;
        [ObservableProperty] private bool canMoveDown;

        partial void OnIsShownChanged(bool value) => _owner.OnRowChanged(this);
        partial void OnIsPinnedChanged(bool value) => _owner.OnRowChanged(this);

        [RelayCommand]
        private void MoveUp() => _owner.MoveRow(this, -1);

        [RelayCommand]
        private void MoveDown() => _owner.MoveRow(this, +1);

        [RelayCommand]
        private void TogglePin() => IsPinned = !IsPinned;
    }

    /// <summary>
    /// The Settings page: every control writes straight through <see cref="UiSettingsService.Update"/> (no Save
    /// button) and the page follows outside changes (zoom shortcut, restore from backup).
    /// </summary>
    public partial class SettingsViewModel : BaseViewModel
    {
        private const string DefaultAccentHex = "#4CC2FF";
        private const string WindowsAccentValue = "windows";

        private readonly UiSettingsService _ui;
        private readonly StartupService _startup;
        private readonly SettingsBackupService _backup;
        private readonly ShellService _shell;
        private readonly Dictionary<string, SidebarPageRow> _rowsByKey = new(StringComparer.OrdinalIgnoreCase);
        private int _syncDepth; // > 0 while we copy settings into the properties (hooks must not write back)

        public SettingsViewModel(
            UiSettingsService ui,
            StartupService startup,
            SettingsBackupService backup,
            ShellService shell,
            ILogger<SettingsViewModel>? logger = null) : base(logger)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _startup = startup ?? throw new ArgumentNullException(nameof(startup));
            _backup = backup ?? throw new ArgumentNullException(nameof(backup));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));

            StartPageOptions = PageRegistry.All.Select(p => p.Title).ToList();

            var windowsAccent = ReadWindowsAccentHex();
            Swatches = new ObservableCollection<AccentSwatch>
            {
                new("CenterHub blue", null, DefaultAccentHex, this),
                new("Windows accent", WindowsAccentValue, windowsAccent, this), // stored as "windows": ThemeService follows the live Windows accent
                new("Violet", "#A78BFA", "#A78BFA", this),
                new("Pink", "#F472B6", "#F472B6", this),
                new("Red", "#F87171", "#F87171", this),
                new("Orange", "#FB923C", "#FB923C", this),
                new("Green", "#5DDB7C", "#5DDB7C", this),
                new("Teal", "#2DD4BF", "#2DD4BF", this),
            };

            PageRows = new ObservableCollection<SidebarPageRow>();
            foreach (var page in PageRegistry.All)
            {
                var row = new SidebarPageRow(page, this);
                _rowsByKey[page.Key] = row;
                PageRows.Add(row);
            }

            Sync(_ui.Current, includeStartup: true);
            _ui.Changed += OnUiSettingsChanged;
        }

        // ─── Options for the pickers ───

        public IReadOnlyList<string> StartPageOptions { get; }
        public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Dark", "Light", "Follow Windows" };
        public IReadOnlyList<string> DensityOptions { get; } = new[] { "Comfortable", "Compact" };
        public IReadOnlyList<string> SidebarOptions { get; } = new[] { "Auto", "Expanded", "Icons only" };

        public ObservableCollection<AccentSwatch> Swatches { get; }
        public ObservableCollection<SidebarPageRow> PageRows { get; }

        public string DataFolderPath => _backup.DataFolder;

        // ─── General ───

        [ObservableProperty] private string? selectedStartPage;
        [ObservableProperty] private bool startWithWindows;
        [ObservableProperty] private bool checkForUpdates;

        // ─── Window and tray ───

        [ObservableProperty] private bool minimizeToTray;
        [ObservableProperty] private bool closeToTray;
        [ObservableProperty] private bool alwaysShowTrayIcon;

        // ─── Appearance ───

        [ObservableProperty] private int themeIndex;
        [ObservableProperty] private int densityIndex;
        [ObservableProperty] private bool showStatusStrip;
        [ObservableProperty] private string accentName = "CenterHub blue";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ZoomText))]
        private double zoomPercent = 100;

        public string ZoomText => $"{(int)Math.Round(ZoomPercent)} %";

        // ─── Sidebar ───

        [ObservableProperty] private int sidebarIndex;

        // ─── Data ───

        [ObservableProperty] private string dataStatus = "";

        // ─── Hooks: property → settings ───

        private bool Skip => _syncDepth > 0 || IsDisposed;

        partial void OnSelectedStartPageChanged(string? value)
        {
            if (Skip || value is null) return;
            var page = PageRegistry.All.FirstOrDefault(p => p.Title == value);
            if (page != null) Write(s => s.StartPage = page.Key);
        }

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (Skip) return;
            if (_startup.SetEnabled(value)) return;

            ToastService.Instance.Error("Couldn't change the Windows startup entry.");
            Silently(() => StartWithWindows = _startup.IsEnabled);
        }

        partial void OnCheckForUpdatesChanged(bool value) { if (!Skip) Write(s => s.CheckForUpdates = value); }
        partial void OnMinimizeToTrayChanged(bool value) { if (!Skip) Write(s => s.MinimizeToTray = value); }
        partial void OnCloseToTrayChanged(bool value) { if (!Skip) Write(s => s.CloseToTray = value); }
        partial void OnAlwaysShowTrayIconChanged(bool value) { if (!Skip) Write(s => s.AlwaysShowTrayIcon = value); }
        partial void OnShowStatusStripChanged(bool value) { if (!Skip) Write(s => s.ShowStatusStrip = value); }

        partial void OnThemeIndexChanged(int value)
        {
            if (Skip || !Enum.IsDefined(typeof(AppTheme), value)) return;
            Write(s => s.Theme = (AppTheme)value);
        }

        partial void OnDensityIndexChanged(int value)
        {
            if (Skip || !Enum.IsDefined(typeof(UiDensity), value)) return;
            Write(s => s.Density = (UiDensity)value);
        }

        partial void OnSidebarIndexChanged(int value)
        {
            if (Skip || !Enum.IsDefined(typeof(SidebarMode), value)) return;
            Write(s => s.Sidebar = (SidebarMode)value);
        }

        partial void OnZoomPercentChanged(double value)
        {
            if (Skip) return;
            var snapped = Math.Clamp(Math.Round(value / 10.0) * 10.0, 80, 150);
            Write(s => s.Zoom = snapped / 100.0);
        }

        // ─── Commands ───

        [RelayCommand]
        private void ResetZoom()
        {
            if (IsDisposed) return;
            ZoomPercent = 100;
        }

        [RelayCommand]
        private void ResetSidebar()
        {
            if (IsDisposed) return;
            Write(s =>
            {
                s.PageOrder.Clear();
                s.HiddenPages.Clear();
                s.PinnedPages.Clear();
            });
        }

        [RelayCommand]
        private void RunAudioSetup()
        {
            if (IsDisposed) return;
            _shell.ShowSetupWizard();
        }

        [RelayCommand]
        private void OpenDataFolder()
        {
            if (IsDisposed) return;
            try
            {
                var folder = _backup.DataFolder;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Could not open the data folder");
                ToastService.Instance.Error($"Couldn't open the folder: {ex.Message}");
            }
        }

        /// <summary>Back up to the zip the view's save dialog picked.</summary>
        [RelayCommand]
        private void BackUpTo(string? path)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var count = _backup.CreateBackup(path);
                DataStatus = $"Backed up {count} settings file{(count == 1 ? "" : "s")} to {path}";
                ToastService.Instance.Success($"Settings backed up ({count} file{(count == 1 ? "" : "s")})");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Backup failed");
                DataStatus = $"Backup failed: {ex.Message}";
                ToastService.Instance.Error(DataStatus);
            }
        }

        /// <summary>Restore from the zip the view's open dialog picked.</summary>
        [RelayCommand]
        private void RestoreFrom(string? path)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var result = _backup.Restore(path);
                if (result.Success)
                {
                    _ui.Reload();
                    DataStatus = $"{result.Message} Your previous settings were saved to {result.SafetyBackupPath}.";
                    ToastService.Instance.Success("Settings restored — restart CenterHub to apply everything");
                }
                else
                {
                    DataStatus = result.Message;
                    ToastService.Instance.Error(result.Message);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Restore failed");
                DataStatus = $"Restore failed: {ex.Message}";
                ToastService.Instance.Error(DataStatus);
            }
        }

        /// <summary>Re-read what can change outside the app (the Windows startup entry). Called when the page is shown.</summary>
        [RelayCommand]
        private void Refresh()
        {
            if (IsDisposed) return;
            Sync(_ui.Current, includeStartup: true);
        }

        // ─── Called by the swatch / row objects ───

        internal void SelectAccent(AccentSwatch swatch)
        {
            if (IsDisposed) return;
            Write(s => s.AccentColor = swatch.StoredValue);
        }

        internal static bool IsProtectedPage(string key) =>
            string.Equals(key, "home", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "settings", StringComparison.OrdinalIgnoreCase);

        internal void OnRowChanged(SidebarPageRow row)
        {
            if (Skip) return;
            Write(s =>
            {
                SetMembership(s.HiddenPages, row.Key, !row.IsShown && row.CanHide);
                SetMembership(s.PinnedPages, row.Key, row.IsPinned);
            });
        }

        /// <summary>Swap a page with its neighbour inside the same sidebar group. The first move writes the full order.</summary>
        internal void MoveRow(SidebarPageRow row, int delta)
        {
            if (IsDisposed) return;
            Write(s =>
            {
                var order = EffectiveOrder(s).ToList();
                var index = order.FindIndex(p => string.Equals(p.Key, row.Key, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return;

                var group = order[index].Group;
                var other = index + delta;
                while (other >= 0 && other < order.Count &&
                       !string.Equals(order[other].Group, group, StringComparison.OrdinalIgnoreCase))
                    other += delta;
                if (other < 0 || other >= order.Count) return;

                (order[index], order[other]) = (order[other], order[index]);
                s.PageOrder = order.Select(p => p.Key).ToList();
            });
        }

        // ─── Settings → properties ───

        private void OnUiSettingsChanged(UiSettings settings)
        {
            if (IsDisposed) return;
            if (Dispatcher.UIThread.CheckAccess())
                Sync(settings, includeStartup: false);
            else
                Dispatcher.UIThread.Post(() => { if (!IsDisposed) Sync(settings, includeStartup: false); });
        }

        private void Sync(UiSettings s, bool includeStartup)
        {
            _syncDepth++;
            try
            {
                SelectedStartPage = PageRegistry.Find(s.StartPage)?.Title ?? PageRegistry.All[0].Title;
                CheckForUpdates = s.CheckForUpdates;
                MinimizeToTray = s.MinimizeToTray;
                CloseToTray = s.CloseToTray;
                AlwaysShowTrayIcon = s.AlwaysShowTrayIcon;
                ThemeIndex = (int)s.Theme;
                DensityIndex = (int)s.Density;
                SidebarIndex = (int)s.Sidebar;
                ShowStatusStrip = s.ShowStatusStrip;
                ZoomPercent = Math.Round(s.Zoom * 100.0);
                if (includeStartup) StartWithWindows = _startup.IsEnabled;

                SyncAccent(s);
                SyncRows(s);
            }
            finally { _syncDepth--; }
        }

        private void SyncAccent(UiSettings s)
        {
            var current = s.AccentColor ?? DefaultAccentHex;
            var match = Swatches.FirstOrDefault(w =>
                string.Equals(w.StoredValue ?? DefaultAccentHex, current, StringComparison.OrdinalIgnoreCase));
            foreach (var swatch in Swatches) swatch.IsSelected = ReferenceEquals(swatch, match);
            AccentName = match?.Name ?? $"Custom {current}";
        }

        private void SyncRows(UiSettings s)
        {
            var order = EffectiveOrder(s).ToList();
            var hidden = new HashSet<string>(s.HiddenPages, StringComparer.OrdinalIgnoreCase);
            var pinned = new HashSet<string>(s.PinnedPages, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < order.Count; i++)
            {
                var row = _rowsByKey[order[i].Key];
                var at = PageRows.IndexOf(row);
                if (at >= 0 && at != i) PageRows.Move(at, i);

                var group = order[i].Group;
                row.IsShown = !row.CanHide || !hidden.Contains(row.Key);
                row.IsPinned = pinned.Contains(row.Key);
                row.CanMoveUp = order.Take(i).Any(p => string.Equals(p.Group, group, StringComparison.OrdinalIgnoreCase));
                row.CanMoveDown = order.Skip(i + 1).Any(p => string.Equals(p.Group, group, StringComparison.OrdinalIgnoreCase));
            }
        }

        // ─── Helpers ───

        /// <summary>The pages as the sidebar lays them out: grouped, and inside a group by the user's order.</summary>
        internal static IEnumerable<PageDescriptor> EffectiveOrder(UiSettings s)
        {
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < s.PageOrder.Count; i++) rank.TryAdd(s.PageOrder[i], i);
            int RankOf(PageDescriptor p) => rank.TryGetValue(p.Key, out var r) ? r : int.MaxValue;

            foreach (var group in PageRegistry.Groups)
            {
                foreach (var page in PageRegistry.All
                             .Where(p => string.Equals(p.Group, group, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(RankOf)) // stable: pages without a rank keep registry order
                    yield return page;
            }
        }

        private static void SetMembership(List<string> list, string key, bool member)
        {
            var present = list.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (member && !present) list.Add(key);
            else if (!member && present) list.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        }

        private void Write(Action<UiSettings> change)
        {
            try { _ui.Update(change); }
            catch (Exception ex) { Logger?.LogError(ex, "Could not save a setting"); }
        }

        private void Silently(Action action)
        {
            _syncDepth++;
            try { action(); }
            finally { _syncDepth--; }
        }

        /// <summary>The Windows accent colour as "#RRGGBB" (Windows' default blue if the registry can't be read).</summary>
        private static string ReadWindowsAccentHex()
        {
            try
            {
                const string dwm = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";

                // AccentColor is 0xAABBGGRR.
                if (Registry.GetValue(dwm, "AccentColor", null) is int abgr)
                {
                    var v = unchecked((uint)abgr);
                    return $"#{v & 0xFF:X2}{(v >> 8) & 0xFF:X2}{(v >> 16) & 0xFF:X2}";
                }

                // ColorizationColor is 0xAARRGGBB.
                if (Registry.GetValue(dwm, "ColorizationColor", null) is int argb)
                {
                    var v = unchecked((uint)argb);
                    return $"#{(v >> 16) & 0xFF:X2}{(v >> 8) & 0xFF:X2}{v & 0xFF:X2}";
                }
            }
            catch { /* fall through */ }
            return "#0078D4";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ui.Changed -= OnUiSettingsChanged;
            base.Dispose(disposing);
        }
    }
}
