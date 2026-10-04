using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.View;
using CenterHubNew.MVVM.ViewModel;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Color = Avalonia.Media.Color;

// Headless checks for the Metronome page. Nothing here opens a window, and the audio
// checks run at master volume 0, so it is safe while CenterHub is open and the PC is in use.
//
//   dotnet run --project tools/page-render -- <outDir>             render the page to PNGs
//   dotnet run --project tools/page-render -- <outDir> audio       real device, volume 0: beat timing (exit 0 = pass)
//   dotnet run --project tools/page-render -- <outDir> audio-diag  compare output configurations on the default device
//
// NOT silent — restarts Voicemeeter Banana several times (audio drops for a few seconds each):
//   dotnet run --project tools/page-render -- <outDir> voicemeeter  exercise start / restart / recovery on the real Banana,
//                                                                   then leave it running with the active preset applied

/// <summary>The app's theme without the app: no DI host, tray icon, hotkeys or audio services.</summary>
internal sealed class RenderApp : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#FF4CC2FF") };
        Styles.Add(fluent);
        Styles.Add(new StyleInclude(new Uri("avares://CenterHubNew/"))
        {
            Source = new Uri("avares://CenterHubNew/Resources/Styles/Theme.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://CenterHubNew/"))
        {
            Source = new Uri("avares://CenterHubNew/Resources/Styles/Controls.axaml"),
        });
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "centerhub-render");
        string mode = args.Length > 1 ? args[1] : "render";
        Directory.CreateDirectory(outDir);

        // Before Avalonia is set up: its synchronization context would capture the awaits and,
        // with nothing pumping the dispatcher, deadlock them.
        if (mode == "voicemeeter")
        {
            bool quick = args.Length > 2 && args[2] == "quick";
            return Task.Run(() => VoicemeeterChecks.RunAsync(quick)).GetAwaiter().GetResult();
        }
        if (mode == "com-probe")
            return Task.Run(ComProbe).GetAwaiter().GetResult();
        if (mode == "render-demo")
            return RenderDemos(outDir);
        if (mode == "build-drumkit")
            return DrumKitBuilder.Build(args[2], args.Length > 3 ? args[3] : Path.Combine("Assets", "Drums", "acoustic.chdk"));
        if (mode == "perapp-probe")
            return Task.Run(PerAppProbe).GetAwaiter().GetResult();
        if (mode == "default-guard")
            return Task.Run(DefaultGuardCheck).GetAwaiter().GetResult();

        AppBuilder.Configure<RenderApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        switch (mode)
        {
            case "audio": return SilentDeviceCheck();
            case "audio-diag": DeviceRateDiag(); return 0;
            case "widths": RenderWidths(outDir); return 0;
            case "palette": RenderPalette(outDir); return 0;
            default: RenderMetronome(outDir); return 0;
        }
    }

    /// <summary>
    /// Renders a few styles offline with a given kit to WAV files you can listen to — the same
    /// engine and settings the app uses (humanize 0.08), with a fill and a part change in each.
    /// </summary>
    private static int RenderDemos(string outDir)
    {
        var demos = new (string Id, DrumKitKind Kit, int Bars)[]
        {
            ("rock-8ths", DrumKitKind.Acoustic, 10),
            ("funk-16ths", DrumKitKind.Acoustic, 8),
            ("blues-shuffle", DrumKitKind.Acoustic, 8),
            ("jazz-swing", DrumKitKind.Acoustic, 8),
            ("rock-8ths", DrumKitKind.Rock, 10),   // the old synthesized kit, for comparison
        };

        foreach (var (id, kitKind, bars) in demos)
        {
            var style = DrumStyleLibrary.Find(id)!;
            var engine = new DrumMachineEngine(new DrumKit(kitKind))
            {
                Bpm = style.DefaultBpm, MasterVolume = 0.8f, Humanize = 0.08f,
                ClickEnabled = false, IntroEnabled = true, AutoFillEveryBars = 4,
            };
            engine.SetStyle(style);
            engine.Start();

            int framesPerBar = (int)(44100 * 60.0 / style.DefaultBpm * style.Beats);
            string file = Path.Combine(outDir, $"demo-{id}-{kitKind.ToString().ToLowerInvariant()}.wav");
            using (var writer = new WaveFileWriter(file, new WaveFormat(44100, 16, 2)))
            {
                var buffer = new float[1024];
                long total = (long)framesPerBar * bars, done = 0;
                bool partRequested = false, outroRequested = false;
                while (engine.IsPlaying || !engine.IsIdle)
                {
                    if (!partRequested && done > framesPerBar * (bars / 2.0 - 0.5)) { engine.RequestNextPart(); partRequested = true; }
                    if (!outroRequested && done > total - framesPerBar * 2.5) { engine.RequestOutro(); outroRequested = true; }
                    engine.Read(buffer, 0, buffer.Length);
                    writer.WriteSamples(buffer, 0, buffer.Length);
                    while (engine.TryDequeueEvent(out _)) { }
                    done += buffer.Length / 2;
                    if (done > total + 44100 * 4) break;   // ring-out limit
                }
            }
            Console.WriteLine($"{file}  ({new FileInfo(file).Length / 1048576.0:F1} MB)");
        }
        return 0;
    }

    /// <summary>
    /// Does the in-app "send an app to a slot" call do what it says on this Windows build — or does
    /// it move Windows' MAIN output? Assigns this probe's own (silent) process, then puts things back.
    /// </summary>
    private static async Task<int> PerAppProbe()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var audio = new AudioDeviceService();
        using var perApp = new PerAppAudioService();
        var before = audio.GetDefaultPlayback();
        var commsBefore = audio.GetDefaultCommunicationsPlayback();
        Console.WriteLine($"Windows build {Environment.OSVersion.Version.Build}");
        Console.WriteLine($"main output before: {before?.Name}; communications: {commsBefore?.Name}");

        // A target that is NOT the current main output, so a change is unmistakable.
        var target = audio.GetPlaybackDevices().FirstOrDefault(d =>
            d.Name.StartsWith("Voicemeeter VAIO3 Input", StringComparison.OrdinalIgnoreCase) && d.Id != before?.Id);
        if (target is null) { Console.WriteLine("no spare device to test with"); return 1; }

        bool reported = perApp.SetAppRenderDevice(Environment.ProcessId, target.Id);
        await Task.Delay(1200);
        var after = audio.GetDefaultPlayback();
        var commsAfter = audio.GetDefaultCommunicationsPlayback();
        Console.WriteLine($"assign this process -> \"{target.Name}\": call reported {(reported ? "success" : "failure")}");
        Console.WriteLine($"main output after:  {after?.Name}; communications: {commsAfter?.Name}");

        bool movedMain = after?.Id != before?.Id || commsAfter?.Id != commsBefore?.Id;
        Console.WriteLine(movedMain
            ? "RESULT: the call MOVED WINDOWS' MAIN OUTPUT — it is not a per-app assignment on this build"
            : "RESULT: the main output did not move");

        perApp.ClearApp(Environment.ProcessId);
        if (movedMain)
        {
            if (before is not null) await audio.SetDefaultPlaybackAsync(before, communicationsToo: false);
            Console.WriteLine($"restored main output: {audio.GetDefaultPlayback()?.Name}");
        }
        return 0;
    }

    /// <summary>
    /// The real guard on the real machine: if Windows' main output sits on an app slot it is moved
    /// to "Voicemeeter Input"; then the main output is deliberately put on the AUX slot to see the
    /// guard move it back by itself. Ends with the main output on "Voicemeeter Input".
    /// </summary>
    private static async Task<int> DefaultGuardCheck()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string temp = Path.Combine(Path.GetTempPath(), "CenterHubGuard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        using var vm = new VoicemeeterService();
        var audio = new AudioDeviceService();
        var routing = new AudioRoutingService(vm, audio, new VoicemeeterSettingsService(null, temp), new PerAppAudioService());
        var restored = new List<string>();
        routing.DesktopOutputRestored += slot => { lock (restored) restored.Add(slot); };

        Console.WriteLine($"Banana running: {routing.RefreshStatus()}");
        Console.WriteLine($"main output now: {audio.GetDefaultPlayback()?.Name}");
        bool moved = await routing.KeepDesktopOutputAsync();
        Console.WriteLine($"guard moved it: {moved} -> {audio.GetDefaultPlayback()?.Name}");

        Console.WriteLine("putting the main output on the AUX slot (what happened to you)...");
        await audio.SetDefaultPlaybackByNameAsync("Voicemeeter AUX Input", communicationsToo: false);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 6000 && AudioRoutingService.IsSlotInput(audio.GetDefaultPlayback()?.Name))
            await Task.Delay(100);
        string? final = audio.GetDefaultPlayback()?.Name;
        Console.WriteLine($"after {clock.ElapsedMilliseconds} ms: {final} (guard fired {restored.Count} time(s) in total)");

        try { Directory.Delete(temp, true); } catch { }
        bool ok = final?.StartsWith("Voicemeeter Input", StringComparison.OrdinalIgnoreCase) == true;
        Console.WriteLine(ok ? "PASS" : "FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>Does device enumeration keep working after the default device has been switched? (changes nothing)</summary>
    private static async Task<int> ComProbe()
    {
        var audio = new AudioDeviceService();
        int Count() => audio.GetPlaybackDevices().Count;
        string Thread() => $"thread {Environment.CurrentManagedThreadId} {System.Threading.Thread.CurrentThread.GetApartmentState()}";

        // Which playback devices does some app (Voicemeeter, usually) hold exclusively? Silent.
        foreach (var device in audio.GetPlaybackDevices().Where(d => !AudioRoutingService.IsVoicemeeterDevice(d.Name)))
        {
            var locked = audio.IsPlaybackDeviceLocked(new AudioEndpointRef { Id = device.Id, Name = device.Name });
            Console.WriteLine($"  {device.Name}: {(locked switch { true => "LOCKED (exclusive)", false => "shared", _ => "unknown" })}");
        }

        Console.WriteLine($"before: {Count()} playback devices ({Thread()})");
        var current = audio.GetDefaultPlayback();
        Console.WriteLine($"default playback: {current?.Name}");
        // Re-assert the device that is already the default: exercises the switching code, changes nothing.
        bool ok = current is not null && await audio.SetDefaultPlaybackAsync(current, communicationsToo: false);
        Console.WriteLine($"set default (same device) ok={ok}");
        Console.WriteLine($"right after: {Count()} playback devices ({Thread()})");
        var other = await Task.Run(() => (Count(), Thread()));
        Console.WriteLine($"on another pool thread: {other.Item1} ({other.Item2})");
        var sta = new System.Threading.Thread(() => Console.WriteLine($"on an STA thread: {Count()} ({Thread()})"));
        sta.SetApartmentState(ApartmentState.STA); sta.Start(); sta.Join();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Console.WriteLine($"after a full GC: {Count()} playback devices");
        for (int i = 0; i < 6; i++)
        {
            var timer = Stopwatch.StartNew();
            ok = current is not null && await audio.SetDefaultPlaybackAsync(current, communicationsToo: false);
            Console.WriteLine($"round {i + 2}: set default ok={ok} in {timer.ElapsedMilliseconds} ms, then {Count()} playback / {audio.GetRecordingDevices().Count} recording devices, default='{audio.GetDefaultPlayback()?.Name}'");
        }
        return Count() > 0 ? 0 : 1;
    }

    // ───────────────────────── page render ─────────────────────────

    private static void RenderMetronome(string outDir)
    {
        // Settings go to a throwaway folder — never the real %AppData%.
        string temp = Path.Combine(Path.GetTempPath(), "CenterHubRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        using var service = new MetronomeService();
        var vm = new MetronomeViewModel(service, new MetronomeSettingsService(null, temp));
        var window = new Window
        {
            Width = 1000,
            Height = 1500,
            Background = new SolidColorBrush(Color.Parse("#0F0F16")),
            Content = new MetronomeView { DataContext = vm },
        };
        window.Show();

        void Shot(string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!;
            string path = Path.Combine(outDir, name + ".png");
            frame.Save(path);
            Console.WriteLine(path);
        }

        Shot("metronome-1-click");

        vm.IsDrumsMode = true;
        Shot("metronome-2-drums");

        // Fake a live position (no audio is opened): bar 3, beat 2, mid-fill.
        var style = vm.SelectedStyle!;
        var fill = style.Parts[0].Fills[0];
        var onPosition = typeof(MetronomeViewModel).GetMethod("OnPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        vm.IsPlaying = true;
        onPosition.Invoke(vm, new object[]
        {
            new DrumEngineEvent(0, style.StepsPerBeat, fill.Steps, 2, style.Beats, true, 3, DrumSection.Fill, 0, fill, false),
        });
        Shot("metronome-3-drums-playing");

        // Stop (the engine never started, so the view-model resets), then the widest grid (24 steps).
        onPosition.Invoke(vm, new object[]
        {
            new DrumEngineEvent(0, 0, fill.Steps, 1, style.Beats, true, 3, DrumSection.Stopped, 0, null, false),
        });
        vm.SelectedStyle = DrumStyleLibrary.All.OrderByDescending(s => s.StepsPerBar).First();
        Shot("metronome-4-wide-grid");

        vm.Dispose();
        try { Directory.Delete(temp, true); } catch { }
    }

    /// <summary>The command palette as the shell shows it (inside the dimmed overlay layer), empty and filtered.</summary>
    private static void RenderPalette(string outDir)
    {
        var vm = new CommandPaletteViewModel();
        var window = new Window
        {
            Width = 1100, Height = 760,
            Background = new SolidColorBrush(Color.Parse("#0F0F16")),
            Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#A8000000")),
                Child = new CommandPaletteView { DataContext = vm },
            },
        };
        window.Show();

        void Shot(string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!;
            string path = Path.Combine(outDir, name + ".png");
            frame.Save(path);
            Console.WriteLine($"{path}  results={vm.Results.Count}");
        }

        vm.OnShown();
        Shot("palette-1-empty");
        vm.Query = "metro";
        Shot("palette-2-metro");
        vm.Query = "tempo 120";
        Shot("palette-3-tempo");
        vm.Dispose();
    }

    /// <summary>The Metronome page at several window widths — does it reflow instead of clipping?</summary>
    private static void RenderWidths(string outDir)
    {
        string temp = Path.Combine(Path.GetTempPath(), "CenterHubRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var service = new MetronomeService();
        var vm = new MetronomeViewModel(service, new MetronomeSettingsService(null, temp)) { IsDrumsMode = true };

        foreach (int width in new[] { 480, 640, 900, 1300 })
        {
            var view = new MetronomeView { DataContext = vm };
            var window = new Window
            {
                Width = width, Height = 1400,
                Background = new SolidColorBrush(Color.Parse("#0F0F16")),
                Content = view,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!;
            string path = Path.Combine(outDir, $"metronome-w{width}.png");
            frame.Save(path);
            var root = view.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Classes.Contains("root"));
            var title = view.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("page-title"));
            Console.WriteLine($"{path}  view={view.Bounds.Width:F0} root={root?.Bounds.Width:F0} narrow={root?.Classes.Contains(":narrow")}" +
                              $"  title={title?.FontSize} templated={title?.TemplatedParent is not null}");
            window.Close();
        }

        // Light theme, switched live (pages use DynamicResource, so this must repaint everything).
        {
            var view = new MetronomeView { DataContext = vm };
            var window = new Window { Width = 900, Height = 1400, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            window.Background = new SolidColorBrush(Color.Parse("#F3F3F7"));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!;
            string path = Path.Combine(outDir, "metronome-light.png");
            frame.Save(path);
            Console.WriteLine(path);
            window.Close();
            Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
        vm.Dispose();
        try { Directory.Delete(temp, true); } catch { }
    }

    // ───────────────────────── silent audio checks ─────────────────────────

    /// <summary>
    /// Drives the REAL <see cref="MetronomeService"/> (real device, real event pump) at master
    /// volume 0 and checks that beats are reported on time. Returns 0 when timing is good.
    /// </summary>
    private static int SilentDeviceCheck()
    {
        using var service = new MetronomeService();
        service.Engine.MasterVolume = 0f;
        service.Engine.Bpm = 120;
        service.Engine.SetStyle(DrumStyleLibrary.Find("rock-8ths"));
        service.Engine.IntroEnabled = false;

        var clock = Stopwatch.StartNew();
        var beats = new List<double>();
        bool stopped = false;
        service.PositionChanged += e =>
        {
            if (e.Section == DrumSection.Stopped) stopped = true;
            else if (e.IsBeatStart) beats.Add(clock.Elapsed.TotalMilliseconds);
        };

        // The headless dispatcher never fires DispatcherTimers, so tick the service's pump by hand.
        var pumpTick = typeof(MetronomeService).GetMethod("OnPumpTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var outputField = typeof(MetronomeService).GetField("_output", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Pump(int ms)
        {
            long until = clock.ElapsedMilliseconds + ms;
            while (clock.ElapsedMilliseconds < until)
            {
                Dispatcher.UIThread.RunJobs();
                pumpTick.Invoke(service, new object?[] { null, EventArgs.Empty });
                Thread.Sleep(10);
            }
        }

        clock.Restart();
        service.Start();
        if (!service.IsPlaying) { Console.WriteLine("FAIL: could not open an audio output"); return 1; }
        Console.WriteLine($"output: {outputField.GetValue(service)?.GetType().Name}");

        Pump(4300);
        service.Engine.RequestOutro();   // bar 3 is playing: outro in bar 4, final hit at 8.0 s
        Pump(4500);

        var gaps = beats.Zip(beats.Skip(1), (a, b) => b - a).ToList();
        double drift = beats.Count > 1 ? beats[^1] - beats[0] - 500.0 * (beats.Count - 1) : double.NaN;
        Console.WriteLine($"beats={beats.Count} (expected 16), stopped event={stopped}, first beat at {beats.FirstOrDefault():F0} ms");
        if (gaps.Count > 0)
            Console.WriteLine($"beat gap min={gaps.Min():F0} max={gaps.Max():F0} avg={gaps.Average():F1} ms (expected 500), drift {drift:F1} ms");

        Pump(2500);
        bool closed = outputField.GetValue(service) == null;
        Console.WriteLine($"output closed after idle: {closed}");

        bool ok = beats.Count == 16 && stopped && closed && Math.Abs(drift) < 40 && gaps.Min() > 440 && gaps.Max() < 560;
        Console.WriteLine(ok ? "PASS" : "FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>How well does each output configuration keep up on the default device? (played seconds per second)</summary>
    private static void DeviceRateDiag()
    {
        using (var devices = new MMDeviceEnumerator())
        {
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Console.WriteLine($"default render device: {device.FriendlyName}; mix format {device.AudioClient.MixFormat.SampleRate} Hz");
        }

        var configs = new (string Name, Func<IWavePlayer> Make, bool To16)[]
        {
            ("WaveOutEvent 80 ms / 2 buffers", () => new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2 }, true),
            ("WaveOutEvent 100 ms / 3 buffers", () => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 }, true),
            ("WasapiOut shared, event, 50 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 50), false),
            ("WasapiOut shared, event, 100 ms", () => new WasapiOut(AudioClientShareMode.Shared, true, 100), false),
        };

        foreach (var (name, make, to16) in configs)
        {
            try
            {
                var engine = new DrumMachineEngine(new DrumKit()) { MasterVolume = 0f, Bpm = 120 };
                using var output = make();
                output.Init(engine, to16);
                var position = (IWavePosition)output;
                double Played() => (double)position.GetPosition() / position.OutputWaveFormat.AverageBytesPerSecond;

                var clock = Stopwatch.StartNew();
                output.Play();
                engine.Start();
                Thread.Sleep(1000);                       // exclude start-up

                double t0 = clock.Elapsed.TotalSeconds, p0 = Played();
                long r0 = engine.FramesRendered;
                double minBuffered = double.MaxValue, maxBuffered = 0;
                for (int i = 0; i < 80; i++)
                {
                    Thread.Sleep(100);
                    double buffered = (double)engine.FramesRendered / engine.WaveFormat.SampleRate - Played();
                    minBuffered = Math.Min(minBuffered, buffered);
                    maxBuffered = Math.Max(maxBuffered, buffered);
                }
                double t1 = clock.Elapsed.TotalSeconds, p1 = Played();
                long r1 = engine.FramesRendered;

                // Below ~0.999 the device is starving (under-runs): audible gaps and a dragging tempo.
                Console.WriteLine($"{name,-34} played {(p1 - p0) / (t1 - t0):F4} s/s, rendered {(r1 - r0) / (t1 - t0):F0} frames/s, " +
                                  $"buffered {minBuffered * 1000:F0}..{maxBuffered * 1000:F0} ms");
                output.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name}: FAILED {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
