using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static IHost? _host;
        private static Mutex? _mutex;
        private const string MutexName = "CenterHubNew_SingleInstance_Mutex";

        public static IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Services not initialized");

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Check for existing instance
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                MessageBox.Show("CenterHub is already running. Only one instance is allowed.", 
                    "CenterHub", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            try
            {
                App._host = CreateHostBuilder(e.Args).Build();
                await App._host.StartAsync();

                var mainWindow = App._host.Services.GetRequiredService<MainWindow>();
                
                // Ensure window is visible on screen
                EnsureWindowVisible(mainWindow);
                
                mainWindow.Show();

                // Initialize global hotkeys after the window has a handle
                InitializeGlobalHotkeys(mainWindow);

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void InitializeGlobalHotkeys(Window mainWindow)
        {
            try
            {
                var handle = new WindowInteropHelper(mainWindow).Handle;
                if (handle == IntPtr.Zero) return;

                var hotkeyService = Services.GetRequiredService<GlobalHotkeyService>();
                hotkeyService.Initialize(handle);

                // ── Wire up action callbacks ──────────────────────────────────

                // App: Show / Hide
                hotkeyService.SetCallback(HotkeyAction.AppShowHide, () =>
                {
                    if (mainWindow.IsVisible)
                    {
                        mainWindow.Hide();
                    }
                    else
                    {
                        mainWindow.Show();
                        mainWindow.WindowState = WindowState.Normal;
                        mainWindow.Activate();
                    }
                });

                // Auto Clicker: Start / Stop
                hotkeyService.SetCallback(HotkeyAction.AutoClickerStartStop, () =>
                {
                    var vm = Services.GetService(typeof(AutoClickerViewModel)) as AutoClickerViewModel;
                    vm?.ToggleStartStop();
                });

                // Auto Clicker: Capture Position
                hotkeyService.SetCallback(HotkeyAction.AutoClickerCapturePosition, () =>
                {
                    var vm = Services.GetService(typeof(AutoClickerViewModel)) as AutoClickerViewModel;
                    vm?.GetCurrentPosition();
                });

                // Audio: Toggle Mic Mute
                hotkeyService.SetCallback(HotkeyAction.AudioToggleMic, () =>
                {
                    try
                    {
                        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                        using var mic = enumerator.GetDefaultAudioEndpoint(
                            NAudio.CoreAudioApi.DataFlow.Capture,
                            NAudio.CoreAudioApi.Role.Communications);
                        if (mic != null)
                        {
                            mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute;
                            var status = mic.AudioEndpointVolume.Mute ? "muted" : "unmuted";
                            ToastService.Instance.Success($"Microphone {status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ToastService.Instance.Error($"Mic toggle failed: {ex.Message}");
                    }
                });

                // Audio: Next Sound Profile
                hotkeyService.SetCallback(HotkeyAction.AudioNextProfile, () =>
                {
                    var vm = Services.GetService(typeof(SoundViewModel)) as SoundViewModel;
                    if (vm != null)
                    {
                        var nextIndex = (vm.SelectedProfileIndex + 1) % 3;
                        if (nextIndex < 0) nextIndex = 0;
                        vm.ApplyProfileCommand.Execute(nextIndex.ToString());
                    }
                });

                // Audio: Previous Sound Profile
                hotkeyService.SetCallback(HotkeyAction.AudioPrevProfile, () =>
                {
                    var vm = Services.GetService(typeof(SoundViewModel)) as SoundViewModel;
                    if (vm != null)
                    {
                        var prevIndex = vm.SelectedProfileIndex - 1;
                        if (prevIndex < 0) prevIndex = 2;
                        vm.ApplyProfileCommand.Execute(prevIndex.ToString());
                    }
                });

                // Clipboard: Toggle Monitoring
                hotkeyService.SetCallback(HotkeyAction.ClipboardToggleMonitoring, () =>
                {
                    var vm = Services.GetService(typeof(ClipboardViewModel)) as ClipboardViewModel;
                    vm?.ToggleMonitoringCommand.Execute(null);
                });

                // Soundboard: Play Sound 1/2/3
                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay1, () => PlaySoundboardItem(0));
                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay2, () => PlaySoundboardItem(1));
                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay3, () => PlaySoundboardItem(2));

                // Soundboard: Stop
                hotkeyService.SetCallback(HotkeyAction.SoundboardStopPlayback, () =>
                {
                    var vm = Services.GetService(typeof(SoundboardViewModel)) as SoundboardViewModel;
                    vm?.StopSoundCommand.Execute(null);
                });

                // Standing Timer: Start / Stop
                hotkeyService.SetCallback(HotkeyAction.StandingTimerStartStop, () =>
                {
                    var vm = Services.GetService(typeof(StandingViewModel)) as StandingViewModel;
                    if (vm != null)
                    {
                        if (vm.IsStartButtonEnabled)
                            vm.StartTimers();
                        else
                            vm.StopTimers();
                    }
                });
            }
            catch (Exception ex)
            {
                var logger = Services.GetService(typeof(ILogger<App>)) as ILogger<App>;
                logger?.LogError(ex, "Failed to initialize global hotkeys");
            }
        }

        private static void PlaySoundboardItem(int index)
        {
            var vm = Services.GetService(typeof(SoundboardViewModel)) as SoundboardViewModel;
            if (vm?.Sounds != null && vm.Sounds.Count > index)
            {
                vm.PlaySoundCommand.Execute(vm.Sounds[index]);
            }
        }

        private void EnsureWindowVisible(Window window)
        {
            // Get screen dimensions
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // If window is larger than screen, resize it
            if (window.Width > screenWidth)
            {
                window.Width = Math.Max(screenWidth * 0.9, window.MinWidth);
            }
            if (window.Height > screenHeight)
            {
                window.Height = Math.Max(screenHeight * 0.9, window.MinHeight);
            }

            // Ensure window is within screen bounds
            if (window.Left + window.Width > screenWidth)
            {
                window.Left = Math.Max(0, screenWidth - window.Width);
            }
            if (window.Top + window.Height > screenHeight)
            {
                window.Top = Math.Max(0, screenHeight - window.Height);
            }
            if (window.Left < 0)
            {
                window.Left = 0;
            }
            if (window.Top < 0)
            {
                window.Top = 0;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // Dispose global hotkeys
            try
            {
                var hotkeyService = App._host?.Services.GetService(typeof(GlobalHotkeyService)) as GlobalHotkeyService;
                hotkeyService?.Dispose();
            }
            catch { /* shutting down */ }

            // Release mutex
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();

            // Stop and dispose host
            if (App._host != null)
            {
                await App._host.StopAsync();
                App._host.Dispose();
            }

            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<ICacheService, CacheService>();
                    services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
                    services.AddSingleton<ClipboardService>();
                    services.AddSingleton<QuickNotesService>();
                    services.AddSingleton<AutoClickerService>();
                    services.AddSingleton<SoundboardService>();
                    services.AddSingleton<GlobalHotkeyService>();

                    // Register ViewModels
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<SoundViewModel>();
                    services.AddTransient<SoundControlsViewModel>();
                    services.AddTransient<StandingViewModel>();
                    services.AddTransient<MoveFilesViewModel>();
                    services.AddTransient<ComputerViewModel>();
                    services.AddTransient<NameInputViewModel>();
                    services.AddTransient<MonitoringViewModel>();
                    services.AddTransient<UtilitiesViewModel>();
                    services.AddTransient<ClipboardViewModel>();
                    services.AddTransient<QuickNotesViewModel>();
                    services.AddTransient<AutoClickerViewModel>();
                    services.AddTransient<SoundboardViewModel>();
                    services.AddTransient<JsonStringifyViewModel>();
                    services.AddTransient<ConverterToolsViewModel>();
                    services.AddTransient<HotkeySettingsViewModel>();

                    // Register Views
                    services.AddTransient<MainWindow>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Information);
                });
    }
}
