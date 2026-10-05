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

        public static IServiceProvider Services => _host?.Services
            ?? throw new InvalidOperationException("Services not initialized");

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            // Single-instance is enforced earlier, in Program.Main (before Avalonia
            // starts), so a second process never reaches this point.

            try
            {
                _host = CreateHostBuilder().Build();
                await _host.StartAsync();

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    // Theme + accent before the first window, so StaticResource lookups see the right palette.
                    _host.Services.GetRequiredService<ThemeService>().Apply();

                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    lifetime.MainWindow = mainWindow;
                    // Closing the main window quits the app (even if Favorites / Sound
                    // Controls are open); minimizing hides it to the tray instead.
                    lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                    mainWindow.Show();

                    InitializeGlobalHotkeys(mainWindow);
                    StartActivationListener(mainWindow);
                    ScheduleUpdateCheck();

                    // Clipboard history should capture from launch, not only after the
                    // Clipboard page is first opened (its VM owns the polling timer).
                    TryPost(() =>
                    {
                        try { _host.Services.GetService<ClipboardViewModel>(); }
                        catch (Exception ex) { _host.Services.GetService<ILogger<App>>()?.LogWarning(ex, "Clipboard history failed to start"); }
                    });

                    lifetime.Exit += (_, _) =>
                    {
                        try { _host.Services.GetService<GlobalHotkeyService>()?.Dispose(); } catch { }
                        try { _host.Services.GetService<UpdateService>()?.Dispose(); } catch { }
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

        /// <summary>
        /// Listen (on a background thread) for a second instance asking us to surface.
        /// When signaled, restore and foreground the main window.
        /// </summary>
        private static void StartActivationListener(MainWindow mainWindow)
        {
            var ev = Program.ActivateRequested;
            if (ev is null) return;

            var thread = new Thread(() =>
            {
                while (true)
                {
                    try { if (!ev.WaitOne()) break; }
                    catch { break; }

                    try
                    {
                        Dispatcher.UIThread.Post(mainWindow.RestoreFromTray);
                    }
                    catch (InvalidOperationException) { break; } // dispatcher gone — app shutting down
                }
            })
            {
                IsBackground = true,
                Name = "SingleInstanceActivation"
            };
            thread.Start();
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

                // Opens the window if needed, then the palette (Ctrl+K inside the app works without this).
                hotkeyService.SetCallback(HotkeyAction.AppCommandPalette, () => TryPost(() =>
                {
                    if (!mainWindow.IsVisible) mainWindow.RestoreFromTray();
                    mainWindow.Activate();
                    Services.GetService<CenterHubNew.MVVM.Navigation.ShellService>()?.ShowCommandPalette();
                }));

                hotkeyService.SetCallback(HotkeyAction.AudioToggleMic, () =>
                {
                    var muted = Services.GetService<MicrophoneService>()?.ToggleMute();
                    if (muted is null) ToastService.Instance.Error("No microphone to mute");
                    else ToastService.Instance.Success(muted.Value ? "Microphone muted" : "Microphone unmuted");
                });

                // Next / previous cycle the Sound tab's routing presets (Guitar + Discord, Gaming, …).
                hotkeyService.SetCallback(HotkeyAction.AudioNextProfile,
                    () => TryPost(() => Services.GetService<SoundViewModel>()?.Routing?.CyclePreset(+1)));

                hotkeyService.SetCallback(HotkeyAction.AudioPrevProfile,
                    () => TryPost(() => Services.GetService<SoundViewModel>()?.Routing?.CyclePreset(-1)));

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

                // ── Metronome / drum machine: hands stay on the guitar
                hotkeyService.SetCallback(HotkeyAction.MetronomeStartStop,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.TogglePlayCommand.Execute(null)));
                hotkeyService.SetCallback(HotkeyAction.MetronomeFill,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.FillCommand.Execute(null)));
                hotkeyService.SetCallback(HotkeyAction.MetronomeNextPart,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.NextPartCommand.Execute(null)));
                hotkeyService.SetCallback(HotkeyAction.MetronomeTapTempo,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.TapTempoCommand.Execute(null)));
                hotkeyService.SetCallback(HotkeyAction.MetronomeNextSong,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.NextSong()));
                hotkeyService.SetCallback(HotkeyAction.MetronomePrevSong,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.PreviousSong()));

                // ── Guitar looper: the same four actions as a looper pedal
                hotkeyService.SetCallback(HotkeyAction.LooperRecord,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.LooperRecord()));
                hotkeyService.SetCallback(HotkeyAction.LooperStop,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.LooperStop()));
                hotkeyService.SetCallback(HotkeyAction.LooperUndo,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.LooperUndo()));
                hotkeyService.SetCallback(HotkeyAction.LooperClear,
                    () => TryPost(() => Services.GetService<MetronomeViewModel>()?.LooperClear()));
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
                    if (Services.GetService<UiSettingsService>()?.Current.CheckForUpdates == false) return;
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
                    services.AddSingleton<WifiService>();
                    services.AddSingleton<UiSettingsService>();
                    services.AddSingleton<CenterHubNew.MVVM.Navigation.ShellService>();
                    services.AddSingleton<PracticeLogService>();
                    services.AddSingleton<MicrophoneService>();
                    services.AddSingleton<ThemeService>();
                    services.AddSingleton<StartupService>();
                    services.AddSingleton<SettingsBackupService>();
                    services.AddSingleton<MetronomeService>();
                    services.AddSingleton<MetronomeSettingsService>();
                    services.AddSingleton<LooperService>();
                    services.AddSingleton<RandomizerSoundService>();
                    services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
                    services.AddSingleton<IVoicemeeterService, VoicemeeterService>();
                    services.AddSingleton<VoicemeeterSettingsService>();
                    services.AddSingleton<VoicemeeterModeService>();
                    services.AddSingleton<PerAppAudioService>();
                    services.AddSingleton<AudioRoutingService>();

                    services.AddTransient<MainViewModel>();
                    services.AddTransient<HomeViewModel>();
                    // Singletons: global hotkeys act on these, so they must be the SAME
                    // instances the UI shows (transient = every key press got a throwaway
                    // VM — Standing could never stop, timers/handlers leaked per press).
                    services.AddSingleton<SoundViewModel>();
                    services.AddTransient<SoundControlsViewModel>();
                    services.AddSingleton<StandingViewModel>();
                    services.AddTransient<MoveFilesViewModel>();
                    services.AddTransient<ComputerViewModel>();
                    services.AddTransient<NameInputViewModel>();
                    services.AddTransient<MonitoringViewModel>();
                    services.AddTransient<UtilitiesViewModel>();
                    services.AddSingleton<ClipboardViewModel>();
                    services.AddTransient<QuickNotesViewModel>();
                    services.AddSingleton<AutoClickerViewModel>();
                    services.AddSingleton<SoundboardViewModel>();
                    services.AddTransient<JsonStringifyViewModel>();
                    services.AddTransient<ConverterToolsViewModel>();
                    services.AddTransient<HotkeySettingsViewModel>();
                    services.AddTransient<FavoritesViewModel>();
                    // Transient — MainViewModel disposes child VMs on shutdown;
                    // the long-lived state lives in WindowLayoutService (singleton).
                    services.AddTransient<WindowLayoutsViewModel>();
                    services.AddTransient<NetworkViewModel>();
                    services.AddTransient<RandomizerViewModel>();
                    services.AddSingleton<MetronomeViewModel>(); // hotkeys (start/stop, fill, next part) act on it
                    services.AddTransient<VoicemeeterViewModel>();
                    // UI overhaul: shell features live as long as the main window.
                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<StatusStripViewModel>();
                    services.AddSingleton<CommandPaletteViewModel>();
                    services.AddTransient<SetupWizardViewModel>();
                    services.AddTransient<RoutingViewModel>();

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
