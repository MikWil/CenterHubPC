using System;
using System.IO;
using System.Linq;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Loads and saves <see cref="UiSettings"/> (%AppData%\CenterHub\ui.json). One shared instance
    /// (DI singleton); change it through <see cref="Update"/> so every listener hears about it.
    /// </summary>
    public sealed class UiSettingsService
    {
        private readonly ILogger<UiSettingsService>? _logger;
        private readonly string _filePath;
        private readonly object _gate = new();

        private static readonly JsonSerializerSettings Json = new()
        {
            Formatting = Formatting.Indented,
            Converters = { new TolerantStringEnumConverter() },
        };

        public UiSettingsService(ILogger<UiSettingsService>? logger = null) : this(logger, storageFolder: null) { }

        /// <param name="storageFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        public UiSettingsService(ILogger<UiSettingsService>? logger, string? storageFolder)
        {
            _logger = logger;
            var folder = storageFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "ui.json");
            Current = Load();
        }

        /// <summary>The live settings. Read freely; change only through <see cref="Update"/>.</summary>
        public UiSettings Current { get; private set; }

        /// <summary>Raised (on the caller's thread) after <see cref="Update"/> changed and saved the settings.</summary>
        public event Action<UiSettings>? Changed;

        /// <summary>Apply a change, keep the values in range, save, and tell the listeners.</summary>
        public void Update(Action<UiSettings> change)
        {
            lock (_gate)
            {
                change(Current);
                Sanitize(Current);
                SaveLocked();
            }
            try { Changed?.Invoke(Current); }
            catch (Exception ex) { _logger?.LogWarning(ex, "A UI settings listener threw"); }
        }

        /// <summary>Re-read ui.json (after a settings restore) and tell the listeners.</summary>
        public void Reload()
        {
            lock (_gate) Current = Load();
            try { Changed?.Invoke(Current); }
            catch (Exception ex) { _logger?.LogWarning(ex, "A UI settings listener threw"); }
        }

        private UiSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var loaded = JsonConvert.DeserializeObject<UiSettings>(File.ReadAllText(_filePath), Json);
                    if (loaded != null)
                    {
                        Upgrade(loaded);
                        Sanitize(loaded);
                        return loaded;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger?.LogError(ex, "ui.json is corrupt; quarantining it");
                AtomicFile.QuarantineCorrupt(_filePath);
            }
            catch (Exception ex) { _logger?.LogError(ex, "Error loading ui.json"); }

            return new UiSettings { SettingsVersion = CurrentVersion };
        }

        /// <summary>Bumped whenever a default changes in a way existing files should pick up once.</summary>
        internal const int CurrentVersion = 2;

        /// <summary>One-time changes for files written by an older version (saved with the next change).</summary>
        internal static void Upgrade(UiSettings s)
        {
            if (s.SettingsVersion < 2)
            {
                // 7.1: minimize stays on the taskbar and the tray icon is always there. Files from
                // 7.0 hold the old defaults (hide to tray, icon only while hidden), not a choice.
                s.MinimizeToTray = false;
                s.AlwaysShowTrayIcon = true;
            }
            s.SettingsVersion = CurrentVersion;
        }

        private void SaveLocked()
        {
            try { AtomicFile.WriteAllText(_filePath, JsonConvert.SerializeObject(Current, Json)); }
            catch (Exception ex) { _logger?.LogError(ex, "Error saving ui.json"); }
        }

        /// <summary>Keeps every value usable whatever the file said.</summary>
        internal static void Sanitize(UiSettings s)
        {
            s.Window ??= new WindowPlacementSettings();
            if (s.Window.Width is < 400 or > 10000) s.Window.Width = null;
            if (s.Window.Height is < 300 or > 10000) s.Window.Height = null;

            s.PageOrder = (s.PageOrder ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            s.HiddenPages = (s.HiddenPages ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            s.PinnedPages = (s.PinnedPages ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            s.DashboardCards = (s.DashboardCards ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            s.FavoritesCards = (s.FavoritesCards ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();

            if (string.IsNullOrWhiteSpace(s.StartPage)) s.StartPage = "home";
            if (double.IsNaN(s.Zoom) || s.Zoom < 0.8 || s.Zoom > 1.5) s.Zoom = Math.Clamp(double.IsNaN(s.Zoom) ? 1.0 : s.Zoom, 0.8, 1.5);
            if (!Enum.IsDefined(s.Sidebar)) s.Sidebar = SidebarMode.Auto;
            if (!Enum.IsDefined(s.Density)) s.Density = UiDensity.Comfortable;
            if (!Enum.IsDefined(s.Theme)) s.Theme = AppTheme.Dark;
            if (string.Equals(s.AccentColor, ThemeService.WindowsAccent, StringComparison.OrdinalIgnoreCase))
                s.AccentColor = ThemeService.WindowsAccent;
            else if (s.AccentColor != null && !System.Text.RegularExpressions.Regex.IsMatch(s.AccentColor, "^#[0-9A-Fa-f]{6}$"))
                s.AccentColor = null;
        }
    }
}
