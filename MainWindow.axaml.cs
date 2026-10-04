using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.View;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew
{
    public partial class MainWindow : Window
    {
        private const double DefaultWidth = 1360;
        private const double DefaultHeight = 880;
        private const double CaptionButtonsWidth = 150; // 3 x 46 DIP system caption buttons + a little air

        private readonly MainViewModel _viewModel;
        private readonly UiSettingsService? _uiSettings;
        private readonly ILogger<MainWindow>? _logger;
        private NotifyIcon? _notifyIcon;

        // Real exits (tray "Exit") bypass close-to-tray.
        private bool _reallyExit;

        // Last bounds while the window was Normal, and the last non-minimized state — saved to ui.json.
        private PixelPoint? _normalPosition;
        private double? _normalWidth;
        private double? _normalHeight;
        private WindowState _lastShownState = WindowState.Normal;
        private bool _placementReady; // false until Opened, so startup moves aren't recorded

        public MainWindow(
            MainViewModel viewModel,
            UiSettingsService? uiSettings = null,
            ILogger<MainWindow>? logger = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _uiSettings = uiSettings ?? App.Services?.GetService(typeof(UiSettingsService)) as UiSettingsService;
            _logger = logger;
            DataContext = _viewModel;

            RestorePlacement();
            _lastShownState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _viewModel.SetWindowWidth(Width);

            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
            PositionChanged += (_, _) => TrackNormalPosition();
            Resized += (_, e) =>
            {
                TrackNormalSize(e.ClientSize.Width, e.ClientSize.Height);
                _viewModel.SetWindowWidth(e.ClientSize.Width);
            };

            // Tunnel: zoom / palette / Esc shortcuts work whatever has focus.
            AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
            AddHandler(PointerWheelChangedEvent, Window_PointerWheel, RoutingStrategies.Tunnel);
            SidebarPanel.AddHandler(Avalonia.Controls.Button.ClickEvent, SidebarPanel_Click);

            if (_uiSettings != null)
                _uiSettings.Changed += OnUiSettingsChanged;
        }

        // ─── Placement memory ───

        /// <summary>Apply the saved bounds if they are still on a screen; otherwise centre at a size that fits.</summary>
        private void RestorePlacement()
        {
            var saved = _uiSettings?.Current.Window;
            double width = saved?.Width ?? DefaultWidth;
            double height = saved?.Height ?? DefaultHeight;

            try
            {
                var screens = Screens.All;
                if (saved?.X is int x && saved.Y is int y && IsReachable(x, y, width, height))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Width = Math.Max(width, MinWidth);
                    Height = Math.Max(height, MinHeight);
                    Position = new PixelPoint(x, y);
                }
                else
                {
                    var screen = Screens.Primary ?? screens.FirstOrDefault();
                    if (screen != null)
                    {
                        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                        width = Math.Min(width, screen.WorkingArea.Width / scale * 0.9);
                        height = Math.Min(height, screen.WorkingArea.Height / scale * 0.9);
                    }
                    Width = Math.Max(width, MinWidth);
                    Height = Math.Max(height, MinHeight);
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not restore the window placement");
            }

            if (saved?.Maximized == true)
                WindowState = WindowState.Maximized;
        }

        /// <summary>True when the window's title strip would be at least partly on some current screen.</summary>
        private bool IsReachable(int x, int y, double width, double height)
        {
            foreach (var screen in Screens.All)
            {
                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                var strip = new PixelRect(x, y, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(46 * scale)));
                var overlap = screen.WorkingArea.Intersect(strip);
                if (overlap.Width >= 100 && overlap.Height >= 20) return true;
            }
            return false;
        }

        private void TrackNormalPosition()
        {
            // A minimized window reports a parked position (-32000); only Normal bounds are worth keeping.
            if (!_placementReady || WindowState != WindowState.Normal) return;
            if (Position.X <= -30000 || Position.Y <= -30000) return;
            _normalPosition = Position;
        }

        private void TrackNormalSize(double width, double height)
        {
            if (!_placementReady || WindowState != WindowState.Normal) return;
            if (width < 1 || height < 1) return;

            // Maximizing resizes the window before the state flips; don't mistake that for a Normal size.
            var screen = Screens.ScreenFromWindow(this);
            if (screen != null)
            {
                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                if (width * scale >= screen.WorkingArea.Width * 0.98 && height * scale >= screen.WorkingArea.Height * 0.98)
                    return;
            }
            _normalWidth = width;
            _normalHeight = height;
        }

        private void SavePlacement()
        {
            if (_uiSettings == null || !_placementReady) return;

            var position = _normalPosition;
            var width = _normalWidth;
            var height = _normalHeight;
            var maximized = _lastShownState == WindowState.Maximized;

            _uiSettings.Update(s =>
            {
                if (position != null && width != null && height != null)
                {
                    s.Window.X = position.Value.X;
                    s.Window.Y = position.Value.Y;
                    s.Window.Width = width;
                    s.Window.Height = height;
                }
                s.Window.Maximized = maximized;
            });
        }

        private void MainWindow_Opened(object? sender, EventArgs e)
        {
            InitializeNotifyIcon();
            EnsureWindowVisible();

            if (WindowState == WindowState.Normal)
            {
                _normalPosition = Position;
                _normalWidth = ClientSize.Width;
                _normalHeight = ClientSize.Height;
            }
            _placementReady = true;

            _viewModel.SetWindowWidth(ClientSize.Width);
            UpdateCaptionSpacer();
        }

        private void EnsureWindowVisible()
        {
            if (WindowState != WindowState.Normal) return;

            var screen = Screens.ScreenFromWindow(this);
            if (screen == null) return;

            var bounds = screen.WorkingArea;
            if (Width > bounds.Width) Width = Math.Max(bounds.Width * 0.9, MinWidth);
            if (Height > bounds.Height) Height = Math.Max(bounds.Height * 0.9, MinHeight);

            var pos = Position;
            if (pos.X < bounds.X) Position = Position.WithX(bounds.X);
            if (pos.Y < bounds.Y) Position = Position.WithY(bounds.Y);
            if (pos.X + Width > bounds.X + bounds.Width)
                Position = Position.WithX((int)(bounds.X + bounds.Width - Width));
            if (pos.Y + Height > bounds.Y + bounds.Height)
                Position = Position.WithY((int)(bounds.Y + bounds.Height - Height));
        }

        // ─── Tray ───

        private void InitializeNotifyIcon()
        {
            // Create exactly once. Show()/Hide() cycles can re-fire Opened, and we must
            // never spin up a second tray icon (that was the duplicate-icon bug).
            if (_notifyIcon != null) return;

            try
            {
                _notifyIcon = new NotifyIcon();
                var iconPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Images",
                    "circular_connection_icon_155652.ico");

                if (System.IO.File.Exists(iconPath))
                    _notifyIcon.Icon = new Icon(iconPath);

                _notifyIcon.Visible = false;
                _notifyIcon.Text = "CenterHub";
                _notifyIcon.MouseUp += NotifyIcon_MouseUp;

                BuildTrayMenu();
                UpdateTrayIconVisibility();

                _logger?.LogDebug("NotifyIcon initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize NotifyIcon");
            }
        }

        /// <summary>(Re)builds the tray icon's context menu. Call again after the menu's contents change.</summary>
        private void BuildTrayMenu()
        {
            if (_notifyIcon == null) return;

            var menu = new ContextMenuStrip();

            // Rebuilt every time it opens so check marks, the mute state and the metronome label are current.
            menu.Opening += (_, _) =>
            {
                try { PopulateTrayMenu(menu); }
                catch (Exception ex) { _logger?.LogError(ex, "Failed to refresh the tray menu"); }
            };
            try { PopulateTrayMenu(menu); }
            catch (Exception ex) { _logger?.LogError(ex, "Failed to build the tray menu"); }

            var old = _notifyIcon.ContextMenuStrip;
            _notifyIcon.ContextMenuStrip = menu;
            old?.Dispose();
        }

        /// <summary>Fills the tray menu with the current state of presets, microphone and metronome.</summary>
        private void PopulateTrayMenu(ContextMenuStrip menu)
        {
            var previous = menu.Items.Cast<ToolStripItem>().ToList();
            menu.Items.Clear();
            foreach (var item in previous) item.Dispose();

            var routing = ResolveService<SoundViewModel>()?.Routing;
            var microphone = ResolveService<MicrophoneService>();
            var metronome = ResolveService<MetronomeViewModel>();

            // Open (default action, bold)
            var open = new ToolStripMenuItem("Open CenterHub");
            open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += (_, _) => PostTrayAction(RestoreFromTray, "open");
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            // Presets > one per preset (check on the active one), then Direct
            var presets = new ToolStripMenuItem("Presets") { Enabled = routing != null };
            if (routing != null)
            {
                foreach (var chip in routing.PresetChips.ToList())
                {
                    var captured = chip;
                    var entry = new ToolStripMenuItem(captured.Name) { Checked = captured.IsActive };
                    entry.Click += (_, _) => PostTrayAction(() => routing.ApplyPresetCommand.Execute(captured), "apply preset");
                    presets.DropDownItems.Add(entry);
                }
                if (presets.DropDownItems.Count > 0) presets.DropDownItems.Add(new ToolStripSeparator());

                var direct = new ToolStripMenuItem("Direct (no Banana)") { Checked = routing.IsDirect };
                direct.Click += (_, _) => PostTrayAction(() => routing.GoDirectCommand.Execute(null), "direct mode");
                presets.DropDownItems.Add(direct);
            }
            menu.Items.Add(presets);

            // Microphone
            bool? muted = microphone?.IsMuted;
            var mute = new ToolStripMenuItem("Mute microphone") { Checked = muted == true, Enabled = muted != null };
            mute.Click += (_, _) => PostTrayAction(() => microphone?.ToggleMute(), "toggle microphone");
            menu.Items.Add(mute);

            // Metronome
            var metronomeText = metronome is { IsPlaying: true } ? $"Stop metronome · {metronome.Bpm} BPM" : "Start metronome";
            var metronomeItem = new ToolStripMenuItem(metronomeText) { Enabled = metronome != null };
            metronomeItem.Click += (_, _) => PostTrayAction(() => metronome?.TogglePlayCommand.Execute(null), "toggle metronome");
            menu.Items.Add(metronomeItem);

            // Voicemeeter
            var restart = new ToolStripMenuItem("Restart Voicemeeter") { Enabled = routing != null };
            restart.Click += (_, _) => PostTrayAction(() => routing?.RestartVoicemeeterCommand.Execute(null), "restart Voicemeeter");
            menu.Items.Add(restart);
            menu.Items.Add(new ToolStripSeparator());

            // Windows
            menu.Items.Add("Favorites Panel", null, (_, _) => OpenFavoritesPanel());
            menu.Items.Add("Sound Controls", null, (_, _) => OpenSoundControlsWindow());
            menu.Items.Add("Settings", null, (_, _) => PostTrayAction(() =>
            {
                RestoreFromTray();
                ResolveService<CenterHubNew.MVVM.Navigation.ShellService>()?.NavigateTo("settings");
            }, "open settings"));
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        }

        private static T? ResolveService<T>() where T : class
        {
            try { return App.Services?.GetService(typeof(T)) as T; }
            catch { return null; }
        }

        /// <summary>Run a tray-menu action on the UI thread; a failing action is logged, never thrown into the menu.</summary>
        private void PostTrayAction(Action action, string what)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { action(); }
                    catch (Exception ex) { _logger?.LogError(ex, "Tray menu action failed: {Action}", what); }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Could not run tray menu action: {Action}", what);
            }
        }

        /// <summary>Quit for real, even with "close to tray" on.</summary>
        public void ExitApplication()
        {
            _reallyExit = true;
            try { Dispatcher.UIThread.Post(() => Close()); }
            catch (InvalidOperationException) { }
        }

        /// <summary>The tray icon is always there while the window is hidden, and optionally while it is shown.</summary>
        private void UpdateTrayIconVisibility()
        {
            if (_notifyIcon == null) return;
            _notifyIcon.Visible = !IsVisible || (_uiSettings?.Current.AlwaysShowTrayIcon ?? false);
        }

        private void HideToTray()
        {
            SavePlacement();
            // Hide() removes the taskbar button on its own; we never toggle ShowInTaskbar
            // (that recreates the native window → duplicate taskbar/tray icons).
            Hide();
            if (_notifyIcon != null)
                _notifyIcon.Visible = true;
        }

        /// <summary>
        /// Bring the window to the foreground from a hidden/minimized/tray state.
        /// Called when a second instance is launched (single-instance activation).
        /// </summary>
        public void RestoreFromTray()
        {
            // Un-minimize back to what it was (Normal or Maximized), then show and focus. We never
            // touch ShowInTaskbar (changing it recreates the native window → duplicate taskbar/tray icons).
            if (WindowState == WindowState.Minimized)
                WindowState = _lastShownState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            Show();
            Activate();
            // Nudge to front without staying pinned on top.
            Topmost = true;
            Topmost = false;
            UpdateTrayIconVisibility();
        }

        private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try { Dispatcher.UIThread.Post(RestoreFromTray); }
                catch (InvalidOperationException) { }
            }
        }

        private void OnUiSettingsChanged(UiSettings settings)
        {
            try { Dispatcher.UIThread.Post(UpdateTrayIconVisibility); }
            catch (InvalidOperationException) { }
        }

        // ─── Window state ───

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == WindowStateProperty)
            {
                var state = WindowState;
                if (state == WindowState.Minimized)
                {
                    var previous = change.GetOldValue<WindowState>();
                    if (previous != WindowState.Minimized) _lastShownState = previous;

                    // MinimizeToTray off = minimize to the taskbar like any window.
                    if (_uiSettings?.Current.MinimizeToTray ?? true)
                        HideToTray();
                }
                else
                {
                    _lastShownState = state;
                    SavePlacement();
                }
            }
            else if (change.Property == WindowDecorationMarginProperty)
            {
                UpdateCaptionSpacer();
            }
        }

        /// <summary>Keep the title bar's right edge clear of the system caption buttons.</summary>
        private void UpdateCaptionSpacer()
        {
            if (CaptionSpacer == null) return;
            CaptionSpacer.Width = Math.Clamp(WindowDecorationMargin.Right, CaptionButtonsWidth, 300);
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (e.ClickCount >= 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                BeginMoveDrag(e);
        }

        // ─── Keyboard / wheel ───

        private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            var mods = e.KeyModifiers;

            if (e.Key == Key.Escape && mods == KeyModifiers.None)
            {
                if (_viewModel.Overlay != null) { _viewModel.CloseOverlay(); e.Handled = true; }
                else if (_viewModel.IsDrawerOpen) { _viewModel.CloseDrawer(); e.Handled = true; }
                return;
            }

            // Ctrl only (Ctrl+Alt is AltGr on many layouts).
            if ((mods & KeyModifiers.Control) == 0 || (mods & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            var shift = (mods & KeyModifiers.Shift) != 0;

            switch (e.Key)
            {
                case Key.OemPlus:
                case Key.Add:
                    _viewModel.ZoomBy(0.1);
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    _viewModel.ZoomBy(-0.1);
                    break;
                case Key.D0:
                case Key.NumPad0:
                    _viewModel.ResetZoom();
                    break;
                case Key.K when !shift:
                case Key.P when shift:
                    _viewModel.ShowCommandPalette();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void Window_PointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0 || e.Delta.Y == 0) return;
            _viewModel.ZoomBy(e.Delta.Y > 0 ? 0.1 : -0.1);
            e.Handled = true;
        }

        // ─── Sidebar drawer / overlay passthrough ───

        private void SidebarPanel_Click(object? sender, RoutedEventArgs e)
        {
            // A click on the page that is already selected doesn't navigate, but should still close the drawer.
            if (e.Source is Avalonia.Controls.RadioButton)
                _viewModel.CloseDrawer();
        }

        private void DrawerBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _viewModel.CloseDrawer();
            e.Handled = true;
        }

        private void OverlayBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only a press on the dimmed area itself; presses inside the overlay content bubble up with another source.
            if (!ReferenceEquals(e.Source, sender)) return;
            if (_viewModel.Overlay?.CloseOnBackdropClick == true)
            {
                _viewModel.CloseOverlay();
                e.Handled = true;
            }
        }

        // ─── Favorites / sound windows ───

        private void FavoritesButton_Click(object? sender, RoutedEventArgs e)
        {
            OpenFavoritesPanel();
        }

        private FavoritesWindow? _favoritesWindow;

        private void OpenFavoritesPanel()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // One panel: a second press brings the open one forward instead of stacking another.
                    if (_favoritesWindow is { } open)
                    {
                        open.Activate();
                        return;
                    }

                    var favWindow = App.Services.GetService(typeof(FavoritesWindow)) as FavoritesWindow;
                    if (favWindow == null) return;
                    _favoritesWindow = favWindow;
                    favWindow.Closed += (_, _) => { if (ReferenceEquals(_favoritesWindow, favWindow)) _favoritesWindow = null; };
                    favWindow.Show();
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open favorites panel");
            }
        }

        private void OpenSoundControlsWindow()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                    CenterHubNew.MVVM.View.SoundControlsWindow.ShowSingleton());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open sound controls window");
            }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            // The native close button only hides to the tray when asked to. Real exits (tray Exit,
            // update installer, app/OS shutdown) are never cancelled.
            var userClose = !_reallyExit && e.CloseReason == WindowCloseReason.WindowClosing;
            if (userClose && (_uiSettings?.Current.CloseToTray ?? false))
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            // Closing the main window quits the app (ShutdownMode is OnMainWindowClose).
            // Save the placement, then clean up the tray icon + view model.
            try
            {
                SavePlacement();
                if (_uiSettings != null) _uiSettings.Changed -= OnUiSettingsChanged;
                if (_notifyIcon != null) _notifyIcon.Visible = false;
                _notifyIcon?.Dispose();
                _viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during MainWindow cleanup");
            }
        }
    }
}
