using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.View;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew
{
    public partial class App : Application
    {
        private static IHost? _host;
        private static Mutex? _mutex;
        private const string MutexName = "CenterHubNew_SingleInstance_Mutex";

        public static IServiceProvider Services => _host?.Services
            ?? throw new InvalidOperationException("Services not initialized");

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                return;
            }

            try
            {
                _host = CreateHostBuilder().Build();
                await _host.StartAsync();

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    lifetime.MainWindow = mainWindow;
                    mainWindow.Show();

                    InitializeGlobalHotkeys(mainWindow);
                    ScheduleUpdateCheck();

                    lifetime.Exit += (_, _) =>
                    {
                        try { _host.Services.GetService<GlobalHotkeyService>()?.Dispose(); } catch { }
                        try { _host.Services.GetService<UpdateService>()?.Dispose(); } catch { }
                        _mutex?.ReleaseMutex();
                        _mutex?.Dispose();
                        try { _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); } catch { }
                        try { _host.Dispose(); } catch { }
                    };
                }
            }
            catch (Exception ex)
            {
                var logger = _host?.Services.GetService<ILogger<App>>();
                logger?.LogCritical(ex, "Fatal startup error");
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static void InitializeGlobalHotkeys(MainWindow mainWindow)
        {
            try
            {
                var hwnd = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;

                var hotkeyService = Services.GetRequiredService<GlobalHotkeyService>();
                hotkeyService.Initialize(hwnd);

                hotkeyService.SetCallback(HotkeyAction.AppShowHide,
                    () => TryPost(() =>
                    {
                        if (mainWindow.IsVisible) mainWindow.Hide();
                        else { mainWindow.Show(); mainWindow.Activate(); }
                    }));

                hotkeyService.SetCallback(HotkeyAction.AutoClickerStartStop,
                    () => TryPost(() => Services.GetService<AutoClickerViewModel>()?.ToggleStartStop()));

                hotkeyService.SetCallback(HotkeyAction.AutoClickerCapturePosition,
                    () => TryPost(() => Services.GetService<AutoClickerViewModel>()?.GetCurrentPosition()));

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
                    catch (Exception ex) { ToastService.Instance.Error($"Mic toggle failed: {ex.Message}"); }
                });

                hotkeyService.SetCallback(HotkeyAction.AudioNextProfile, () => TryPost(() =>
                {
                    var vm = Services.GetService<SoundViewModel>();
                    if (vm != null) vm.ApplyProfileCommand.Execute(((vm.SelectedProfileIndex + 1) % 3).ToString());
                }));

                hotkeyService.SetCallback(HotkeyAction.AudioPrevProfile, () => TryPost(() =>
                {
                    var vm = Services.GetService<SoundViewModel>();
                    if (vm != null) { var p = vm.SelectedProfileIndex - 1; if (p < 0) p = 2; vm.ApplyProfileCommand.Execute(p.ToString()); }
                }));

                hotkeyService.SetCallback(HotkeyAction.ClipboardToggleMonitoring,
                    () => TryPost(() => Services.GetService<ClipboardViewModel>()?.ToggleMonitoringCommand.Execute(null)));

                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay1, () => PlaySoundboardItem(0));
                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay2, () => PlaySoundboardItem(1));
                hotkeyService.SetCallback(HotkeyAction.SoundboardPlay3, () => PlaySoundboardItem(2));

                hotkeyService.SetCallback(HotkeyAction.SoundboardStopPlayback,
                    () => TryPost(() => Services.GetService<SoundboardViewModel>()?.StopSoundCommand.Execute(null)));

                hotkeyService.SetCallback(HotkeyAction.StandingTimerStartStop, () => TryPost(() =>
                {
                    var vm = Services.GetService<StandingViewModel>();
                    if (vm != null) { if (vm.IsStartButtonEnabled) vm.StartTimers(); else vm.StopTimers(); }
                }));

                // ── Window layouts: apply slot N directly via the singleton service
                hotkeyService.SetCallback(HotkeyAction.ApplyLayout1, () => ApplyLayoutSlot(0));
                hotkeyService.SetCallback(HotkeyAction.ApplyLayout2, () => ApplyLayoutSlot(1));
                hotkeyService.SetCallback(HotkeyAction.ApplyLayout3, () => ApplyLayoutSlot(2));
            }
            catch (Exception ex)
            {
                var logger = Services.GetService<ILogger<App>>();
                logger?.LogError(ex, "Failed to initialize global hotkeys");
            }
        }

        /// <summary>
        /// Fire-and-forget background poll of the GitHub Releases API ~10 s
        /// after window-open. Result is fed into MainViewModel via
        /// UpdateService.UpdateChanged, which surfaces the banner.
        /// </summary>
        private static void ScheduleUpdateCheck()
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    var svc = Services.GetService<UpdateService>();
                    if (svc is null) return;
                    await svc.CheckAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Services.GetService<ILogger<App>>()?.LogWarning(ex, "Background update check failed");
                }
            });
        }

        private static void PlaySoundboardItem(int index) =>
            TryPost(() =>
            {
                var vm = Services.GetService<SoundboardViewModel>();
                if (vm?.Sounds != null && vm.Sounds.Count > index)
                    vm.PlaySoundCommand.Execute(vm.Sounds[index]);
            });

        private static void ApplyLayoutSlot(int zeroBasedIndex) =>
            TryPost(() =>
            {
                var svc = Services.GetService<WindowLayoutService>();
                if (svc is null) return;
                if (zeroBasedIndex >= svc.Layouts.Count)
                {
                    ToastService.Instance.Warning($"No layout in slot {zeroBasedIndex + 1}");
                    return;
                }
                var layout = svc.Layouts[zeroBasedIndex];
                var applied = svc.ApplyLayout(layout);
                if (applied > 0)
                    ToastService.Instance.Success($"Applied '{layout.Name}' — {applied} window{(applied == 1 ? "" : "s")} placed");
                else
                    ToastService.Instance.Warning($"'{layout.Name}' — no matching windows are open");
            });

        private static void TryPost(Action action)
        {
            try { Dispatcher.UIThread.Post(action); }
            catch (InvalidOperationException) { }
        }

        private static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<ICacheService, CacheService>();
                    services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
                    services.AddSingleton<ClipboardService>();
                    services.AddSingleton<QuickNotesService>();
                    services.AddSingleton<AutoClickerService>();
                    services.AddSingleton<SoundboardService>();
                    services.AddSingleton<GlobalHotkeyService>();
                    services.AddSingleton<WindowLayoutService>();
                    services.AddSingleton<UpdateService>();
                    services.AddSingleton<WindowsNotificationService>();

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
                    services.AddTransient<FavoritesViewModel>();
                    // Transient — MainViewModel disposes child VMs on shutdown;
                    // the long-lived state lives in WindowLayoutService (singleton).
                    services.AddTransient<WindowLayoutsViewModel>();

                    services.AddTransient<MainWindow>();
                    services.AddTransient<FavoritesWindow>();
                })
                .ConfigureLogging((_, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Information);
                });
    }
}
