using System.Diagnostics;
using System.Runtime.InteropServices;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Exercises CenterHub's real Voicemeeter start / restart / recovery code against the real Banana:
/// a normal restart, a Banana that was closed behind CenterHub's back, one that crashed, and one
/// that is hung. It works on a COPY of the user's voicemeeter.json and finishes with Banana
/// running and the active preset applied.
/// </summary>
internal static class VoicemeeterChecks
{
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr handle);

    private static readonly string[] EngineNames =
        { "voicemeeter", "voicemeeter_x64", "voicemeeterpro", "voicemeeterpro_x64", "voicemeeter8", "voicemeeter8x64" };

    private static Process[] Engines() => EngineNames.SelectMany(Process.GetProcessesByName).ToArray();

    public static Task<int> RunAsync() => RunAsync(quick: false);

    /// <param name="quick">Only the apply + two normal restarts (three audio drops instead of ten).</param>
    public static async Task<int> RunAsync(bool quick)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Work on a copy so the user's real settings file is never written.
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub", "voicemeeter.json");
        string temp = Path.Combine(Path.GetTempPath(), "CenterHubVm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        if (File.Exists(real)) File.Copy(real, Path.Combine(temp, "voicemeeter.json"));

        using var logs = LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; })
            .SetMinimumLevel(LogLevel.Information));

        using var vm = new VoicemeeterService(logs.CreateLogger<VoicemeeterService>());
        var audio = new AudioDeviceService(logs.CreateLogger<AudioDeviceService>());
        var store = new VoicemeeterSettingsService(null, temp);
        var routing = new AudioRoutingService(vm, audio, store, new PerAppAudioService(), logs.CreateLogger<AudioRoutingService>());

        var presets = routing.LoadPresets();
        var preset = presets.FirstOrDefault(p => p.Id == routing.ActivePresetId) ?? presets[0];
        var wanted = store.Load().Settings;
        Console.WriteLine($"preset '{preset.Name}', headphones '{wanted.MonitorDeviceName}'");

        int failures = 0;
        var clock = new Stopwatch();

        async Task Step(string name, Func<Task<VoicemeeterModeResult>> action)
        {
            Console.WriteLine($"\n=== {name} ===");
            clock.Restart();
            VoicemeeterModeResult result;
            try
            {
                // A step that never returns is a failure, not a reason to leave Banana down.
                var work = action();
                result = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(75))) == work
                    ? await work
                    : VoicemeeterModeResult.Fail("TIMED OUT after 75 s");
            }
            catch (Exception ex) { result = VoicemeeterModeResult.Fail($"EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
            long ms = clock.ElapsedMilliseconds;

            await Task.Delay(800);
            string? a1 = vm.GetMonitorDeviceName();
            string? play = audio.GetDefaultPlayback()?.Name;
            string? rec = audio.GetDefaultRecording()?.Name;
            bool ok = result.Success
                      && vm.RefreshStatus() == VoicemeeterStatus.Running
                      && Engines().Length == 1
                      && (string.IsNullOrEmpty(wanted.MonitorDeviceName) || string.Equals(a1, wanted.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                      && play?.StartsWith("Voicemeeter Input", StringComparison.OrdinalIgnoreCase) == true
                      && rec?.StartsWith("Voicemeeter Out B1", StringComparison.OrdinalIgnoreCase) == true;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} in {ms} ms: \"{result.Message}\"");
            Console.WriteLine($"     engine={vm.Status} processes={Engines().Length} A1='{a1}' default out='{play}' default mic='{rec}'");
        }

        // 1. Whatever state Banana is in now: bring it up and apply the preset.
        await Step("apply preset (starts Banana if needed)", () => routing.ApplyPresetAsync(preset));

        // 2. The button: restart a healthy Banana, three times.
        for (int i = 1; i <= (quick ? 2 : 3); i++)
            await Step($"Restart Voicemeeter #{i}", () => routing.RestartVoicemeeterAsync(preset));

        if (quick)
        {
            try { Directory.Delete(temp, true); } catch { }
            Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        // 3. Banana closed behind CenterHub's back (user quit it / it crashed). The keep-alive
        //    notices within a second; the next apply has to start it again. This is the sequence
        //    that left v6.0.0 stuck on "Could not start Voicemeeter Banana".
        Console.WriteLine("\n--- killing Banana behind the service's back, waiting for the keep-alive to notice ---");
        foreach (var p in Engines()) { p.Kill(); p.WaitForExit(3000); }
        await Task.Delay(2500);
        await Step("apply preset after Banana died", () => routing.ApplyPresetAsync(preset));

        // 4. …and the same, but asking for a restart instead.
        Console.WriteLine("\n--- killing Banana again ---");
        foreach (var p in Engines()) { p.Kill(); p.WaitForExit(3000); }
        await Task.Delay(2500);
        await Step("Restart Voicemeeter after Banana died", () => routing.RestartVoicemeeterAsync(preset));

        // 5. A hung Banana: suspended, so it ignores the polite shutdown and must be terminated.
        Console.WriteLine("\n--- suspending Banana to simulate a hang ---");
        foreach (var p in Engines()) NtSuspendProcess(p.Handle);
        await Task.Delay(1500);
        await Step("Restart Voicemeeter while it is hung", () => routing.RestartVoicemeeterAsync(preset));

        // 6. Two requests at once (button double-click, hotkey + button).
        await Step("two restarts at the same time", async () =>
        {
            var both = await Task.WhenAll(routing.RestartVoicemeeterAsync(preset), routing.RestartVoicemeeterAsync(preset));
            return both.All(r => r.Success) ? both[0] : both.First(r => !r.Success);
        });

        // Leave it the way the user wants it: one clean, politely restarted Banana with the preset on.
        await Step("final clean restart", () => routing.RestartVoicemeeterAsync(preset));

        // Whatever happened above, never exit with Banana down.
        if (Engines().Length == 0)
        {
            Console.WriteLine("\nBanana is not running — starting it directly");
            const string dir = @"C:\Program Files (x86)\VB\Voicemeeter";
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "voicemeeterpro.exe")) { UseShellExecute = true, WorkingDirectory = dir });
            failures++;
        }

        try { Directory.Delete(temp, true); } catch { }
        Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }
}
