using CenterHubNew.MVVM.View;
using CenterHubNew.MVVM.ViewModel;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing; // For Icon
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms; // For NotifyIcon
using System.Windows.Input;
using System.Windows.Interop;

namespace CenterHubNew
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ILogger<MainWindow>? _logger;
        private NotifyIcon? _notifyIcon;

        public MainWindow(
            MainViewModel viewModel,
            ILogger<MainWindow>? logger = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _logger = logger;
            DataContext = _viewModel;

            InitializeNotifyIcon();
            _logger?.LogInformation("MainWindow initialized");
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon();
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "circular_connection_icon_155652.ico");
                
                if (System.IO.File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _logger?.LogWarning("Icon file not found: {IconPath}", iconPath);
                }
                
                _notifyIcon.Visible = false;
                _notifyIcon.Text = "CenterHub";
                _notifyIcon.MouseUp += NotifyIcon_MouseUp;

                // Add context menu
                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Sound Controls", null, (s, e) => ShowSoundControlsWindow());
                contextMenu.Items.Add("Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());
                _notifyIcon.ContextMenuStrip = contextMenu;
                
                _logger?.LogDebug("NotifyIcon initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize NotifyIcon");
            }
        }

        private void NotifyIcon_MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
                _notifyIcon!.Visible = false;
                _logger?.LogDebug("Window restored from system tray");
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                // Show context menu at cursor position
                typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_notifyIcon, null);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
                _notifyIcon!.Visible = true;
                _logger?.LogDebug("Window minimized to system tray");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _logger?.LogInformation("Application closing");
            System.Windows.Application.Current.MainWindow?.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const uint WM_SYSCOMMAND = 0x0112;
        private const uint SC_SIZE = 0xF000;
        private const uint WMSZ_TOP = 3;
        private const uint WMSZ_BOTTOM = 6;
        private const uint WMSZ_BOTTOMRIGHT = 8;
        private const uint WMSZ_BOTTOMLEFT = 7;
        private const uint WMSZ_TOPLEFT = 4;
        private const uint WMSZ_TOPRIGHT = 5;
        private const uint WMSZ_LEFT = 1;
        private const uint WMSZ_RIGHT = 2;

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState == WindowState.Maximized) return;
            if (e.ChangedButton != MouseButton.Left) return;
            
            var element = sender as FrameworkElement;
            if (element == null) return;

            e.Handled = true;
            ReleaseCapture();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            
            var cursor = element.Cursor;
            uint resizeCommand = 0;
            
            if (cursor == System.Windows.Input.Cursors.SizeNWSE)
            {
                // Check if it's top-left or bottom-right
                var hAlignment = element.HorizontalAlignment;
                resizeCommand = hAlignment == System.Windows.HorizontalAlignment.Left 
                    ? SC_SIZE + WMSZ_TOPLEFT 
                    : SC_SIZE + WMSZ_BOTTOMRIGHT;
            }
            else if (cursor == System.Windows.Input.Cursors.SizeNESW)
            {
                // Check if it's top-right or bottom-left
                var hAlignment = element.HorizontalAlignment;
                resizeCommand = hAlignment == System.Windows.HorizontalAlignment.Right 
                    ? SC_SIZE + WMSZ_TOPRIGHT 
                    : SC_SIZE + WMSZ_BOTTOMLEFT;
            }
            else if (cursor == System.Windows.Input.Cursors.SizeWE)
            {
                // Check if it's left or right edge
                var hAlignment = element.HorizontalAlignment;
                resizeCommand = hAlignment == System.Windows.HorizontalAlignment.Left 
                    ? SC_SIZE + WMSZ_LEFT 
                    : SC_SIZE + WMSZ_RIGHT;
            }
            else if (cursor == System.Windows.Input.Cursors.SizeNS)
            {
                // Check if it's top or bottom edge
                var vAlignment = element.VerticalAlignment;
                resizeCommand = vAlignment == System.Windows.VerticalAlignment.Top 
                    ? SC_SIZE + WMSZ_TOP 
                    : SC_SIZE + WMSZ_BOTTOM;
            }
            
            if (resizeCommand > 0)
            {
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(resizeCommand), IntPtr.Zero);
            }
        }

        private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Cursor is already set in XAML
        }

        protected override void OnClosed(System.EventArgs e)
        {
            try
            {
                _notifyIcon?.Dispose();
                _viewModel?.Dispose();
                _logger?.LogInformation("MainWindow closed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during MainWindow cleanup");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        private void ShowSoundControlsWindow()
        {
            try
            {
                CenterHubNew.MVVM.View.SoundControlsWindow.ShowSingleton();
                _logger?.LogDebug("Sound controls window opened");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open sound controls window");
            }
        }
    }
}
