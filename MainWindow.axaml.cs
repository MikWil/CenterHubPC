using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CenterHubNew.MVVM.View;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ILogger<MainWindow>? _logger;
        private NotifyIcon? _notifyIcon;
        private bool _exitConfirmed;

        public MainWindow(
            MainViewModel viewModel,
            ILogger<MainWindow>? logger = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _logger = logger;
            DataContext = _viewModel;

            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
            KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            // Escape dismisses the exit dialog if open
            if (e.Key == Avalonia.Input.Key.Escape && ExitOverlay.IsVisible)
            {
                ExitOverlay.IsVisible = false;
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            // Triggers Closing — which will show the confirmation overlay
            Close();
        }

        private void ConfirmExit_Click(object? sender, RoutedEventArgs e)
        {
            _exitConfirmed = true;
            ExitOverlay.IsVisible = false;
            Close();
        }

        private void CancelExit_Click(object? sender, RoutedEventArgs e)
        {
            ExitOverlay.IsVisible = false;
        }

        private void MainWindow_Opened(object? sender, EventArgs e)
        {
            InitializeNotifyIcon();
            EnsureWindowVisible();
        }

        private void EnsureWindowVisible()
        {
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

        private void InitializeNotifyIcon()
        {
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

                var menu = new ContextMenuStrip();
                menu.Items.Add("Favorites Panel", null, (_, _) => OpenFavoritesPanel());
                menu.Items.Add("Sound Controls", null, (_, _) => OpenSoundControlsWindow());
                menu.Items.Add("-");
                menu.Items.Add("Exit", null, (_, _) =>
                    { try { Dispatcher.UIThread.Post(() => Close()); } catch (InvalidOperationException) { } });
                _notifyIcon.ContextMenuStrip = menu;

                _logger?.LogDebug("NotifyIcon initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize NotifyIcon");
            }
        }

        private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try { Dispatcher.UIThread.Post(() => { Show(); Activate(); _notifyIcon!.Visible = false; }); }
                catch (InvalidOperationException) { }
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WindowStateProperty)
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                    if (_notifyIcon != null)
                        _notifyIcon.Visible = true;
                }
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void FavoritesButton_Click(object? sender, RoutedEventArgs e)
        {
            OpenFavoritesPanel();
        }

        private void OpenFavoritesPanel()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var favWindow = App.Services.GetService(typeof(FavoritesWindow)) as FavoritesWindow;
                    if (favWindow == null) return;

                    if (!favWindow.IsVisible)
                        favWindow.Show();
                    else
                        favWindow.Activate();
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
            // Skip confirmation for OS shutdown / app-wide shutdown reasons —
            // those are not user-initiated cancellable events.
            var isOsShutdown =
                e.CloseReason == WindowCloseReason.OSShutdown ||
                e.CloseReason == WindowCloseReason.ApplicationShutdown;

            if (!_exitConfirmed && !isOsShutdown)
            {
                e.Cancel = true;
                ExitOverlay.IsVisible = true;
                Activate();
                return;
            }

            try
            {
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
