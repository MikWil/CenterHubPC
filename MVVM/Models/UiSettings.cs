using System.Collections.Generic;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>How the sidebar is shown. Auto follows the window width.</summary>
    public enum SidebarMode
    {
        Auto,
        Expanded,
        Rail,
    }

    /// <summary>Spacing of cards and rows.</summary>
    public enum UiDensity
    {
        Comfortable,
        Compact,
    }

    /// <summary>Colour theme. System follows Windows' app mode.</summary>
    public enum AppTheme
    {
        Dark,
        Light,
        System,
    }

    /// <summary>The main window's last size and position (null = never saved → default size, centred).</summary>
    public sealed class WindowPlacementSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool Maximized { get; set; }
    }

    /// <summary>
    /// Everything about the app shell the user can change: window, sidebar, zoom, theme, tray,
    /// dashboard. Persisted to %AppData%\CenterHub\ui.json by <c>UiSettingsService</c>.
    /// </summary>
    public sealed class UiSettings
    {
        public WindowPlacementSettings Window { get; set; } = new();

        public SidebarMode Sidebar { get; set; } = SidebarMode.Auto;

        /// <summary>Page keys in the user's order; pages missing from it keep their registry order after it.</summary>
        public List<string> PageOrder { get; set; } = new();

        /// <summary>Page keys hidden from the sidebar (still reachable from the command palette).</summary>
        public List<string> HiddenPages { get; set; } = new();

        /// <summary>Page keys pinned to the top of the sidebar.</summary>
        public List<string> PinnedPages { get; set; } = new();

        /// <summary>Page shown at startup.</summary>
        public string StartPage { get; set; } = "home";

        /// <summary>UI zoom factor, 0.8 – 1.5.</summary>
        public double Zoom { get; set; } = 1.0;

        public UiDensity Density { get; set; } = UiDensity.Comfortable;

        public AppTheme Theme { get; set; } = AppTheme.Dark;

        /// <summary>Accent colour as "#RRGGBB", "windows" = follow the Windows accent; null = CenterHub blue (#4CC2FF).</summary>
        public string? AccentColor { get; set; }

        public bool ShowStatusStrip { get; set; } = true;

        /// <summary>
        /// Version of the defaults this file was written with (0 = before versions existed).
        /// <c>UiSettingsService</c> upgrades older files once; see <c>UiSettingsService.CurrentVersion</c>.
        /// </summary>
        public int SettingsVersion { get; set; }

        /// <summary>Show the tray icon while the window is open too (it always shows while hidden).</summary>
        public bool AlwaysShowTrayIcon { get; set; } = true;

        /// <summary>Minimize hides to the tray (true) or stays on the taskbar like any window (false, the default).</summary>
        public bool MinimizeToTray { get; set; }

        /// <summary>The close button hides to the tray instead of quitting.</summary>
        public bool CloseToTray { get; set; }

        public bool CheckForUpdates { get; set; } = true;

        /// <summary>Card keys on the Home dashboard, in order; empty = the default set.</summary>
        public List<string> DashboardCards { get; set; } = new();

        /// <summary>Card keys in the compact Favorites window, in order; empty = the default set.</summary>
        public List<string> FavoritesCards { get; set; } = new();

        /// <summary>The first-run audio setup wizard has been completed or dismissed.</summary>
        public bool AudioSetupCompleted { get; set; }
    }
}
