using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;
using Xunit;

namespace CenterHubNew.Tests;

public class AudioRoutingTests
{
    private const int VaioStrip = 3;

    [Fact]
    public async Task ApplyPreset_never_sends_desktop_and_discord_back_to_discord()
    {
        using var h = new RoutingHarness();
        var preset = new AudioRoutingPreset
        {
            Name = "Stale",
            Routes =
            {
                // A stale/edited preset asking for the echo loop must be refused.
                new AudioSourceRoute { Kind = AudioSourceKind.AppVaio, ToYou = true, ToOthers = true },
                new AudioSourceRoute { Kind = AudioSourceKind.Microphone, ToYou = false, ToOthers = true },
            }
        };

        var result = await h.Routing.ApplyPresetAsync(preset);

        Assert.True(result.Success, result.Message);
        Assert.Contains((VaioStrip, VoicemeeterBus.B1, false), h.Vm.Routes);
        Assert.DoesNotContain((VaioStrip, VoicemeeterBus.B1, true), h.Vm.Routes);
        Assert.Contains((0, VoicemeeterBus.B1, true), h.Vm.Routes);   // mic still reaches Discord
    }

    [Fact]
    public void LiveSet_also_blocks_the_echo_route()
    {
        using var h = new RoutingHarness();

        h.Routing.LiveSet(new AudioSourceRoute { Kind = AudioSourceKind.AppVaio, ToYou = true, ToOthers = true });

        Assert.DoesNotContain((VaioStrip, VoicemeeterBus.B1, true), h.Vm.Routes);
    }

    [Fact]
    public void Default_presets_keep_desktop_and_discord_you_only()
    {
        using var h = new RoutingHarness();

        var desktopRoutes = h.Routing.BuildDefaultPresets()
            .SelectMany(p => p.Routes)
            .Where(r => r.Kind == AudioSourceKind.AppVaio);

        Assert.All(desktopRoutes, r => Assert.False(r.ToOthers));
    }

    [Fact]
    public void Board_row_ignores_a_stale_others_flag_on_the_desktop_sink()
    {
        var source = new AudioSource { Kind = AudioSourceKind.AppVaio, DefaultName = "Desktop & Discord", CanSendToOthers = false };
        var staleRoute = new AudioSourceRoute { Kind = AudioSourceKind.AppVaio, ToYou = true, ToOthers = true, DisplayName = "Slot" };

        var row = new RoutingSourceRow(source, staleRoute, onChanged: null);

        Assert.False(row.ToOthers);
        Assert.True(row.ToYou);
        Assert.Equal("Desktop & Discord", row.DisplayName); // fixed sources keep their proper label
    }

    [Fact]
    public void Setup_check_reports_missing_voicemeeter_as_a_single_error()
    {
        using var h = new RoutingHarness();
        h.Vm.Installed = false;

        var checks = h.Routing.RunChecks();

        var only = Assert.Single(checks);
        Assert.Equal(AudioCheckStatus.Error, only.Status);
        Assert.Equal(AudioCheckFix.InstallVoicemeeter, only.Fix);
    }

    [Fact]
    public void Setup_check_flags_missing_virtual_driver()
    {
        using var h = new RoutingHarness();
        h.Audio.Playback.RemoveAll(d => d.Name.Contains("Voicemeeter"));

        var checks = h.Routing.RunChecks();

        Assert.Contains(checks, c => c.Title == "Virtual audio driver" && c.Status == AudioCheckStatus.Error);
    }

    [Fact]
    public void Setup_check_on_a_healthy_rig_has_no_errors_and_offers_restart()
    {
        using var h = new RoutingHarness();
        h.Audio.DefaultPlay = new AudioEndpointRef { Id = "p-vaio", Name = "Voicemeeter Input (VB-Audio Voicemeeter VAIO)" };
        h.Audio.DefaultRec = new AudioEndpointRef { Id = "r-b1", Name = "Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)" };
        h.Store.SaveSettings(new VoicemeeterSettings
        {
            MicrophoneDeviceName = "Microphone (USB)",
            GuitarDeviceName = "Microphone (USB)",
            MonitorDeviceName = "Headphones (USB)",
        });

        var checks = h.Routing.RunChecks();

        Assert.DoesNotContain(checks, c => c.Status == AudioCheckStatus.Error);
        Assert.Contains(checks, c => c.Title == "Windows audio routing" && c.Status == AudioCheckStatus.Ok);
        Assert.Contains(checks, c => c.Fix == AudioCheckFix.RestartVoicemeeter);
    }

    // ── Banana's real device state: assigning a device is a request, not a fact ──

    private static AudioRoutingPreset AnyPreset() => new()
    {
        Name = "Any",
        Routes = { new AudioSourceRoute { Kind = AudioSourceKind.Microphone, ToOthers = true } },
    };

    private static RoutingHarness HarnessWithHeadphones()
    {
        var h = new RoutingHarness();
        h.Store.SaveSettings(new VoicemeeterSettings
        {
            MicrophoneDeviceName = "Microphone (USB)",
            MonitorDeviceName = "Högtalare (USB headset)",   // non-ASCII, like a Swedish Windows
        });
        return h;
    }

    [Fact]
    public async Task ApplyPreset_assigns_the_headphones_and_confirms_banana_opened_them()
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = "";   // Banana starts with no A1 device

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(result.Success, result.Message);
        Assert.Equal("Högtalare (USB headset)", h.Vm.MonitorDevice);
        Assert.Equal(1, h.Vm.MonitorDeviceSets);
    }

    [Fact]
    public async Task ApplyPreset_does_not_reassign_devices_banana_already_has()
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = "Högtalare (USB headset)";
        h.Vm.InputDevices[0] = "Microphone (USB)";

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, h.Vm.MonitorDeviceSets);   // re-assigning restarts Banana's engine: a dropout per preset switch
        Assert.Equal(0, h.Vm.InputDeviceSets);
    }

    [Fact]
    public async Task ApplyPreset_reports_when_banana_cannot_open_the_headphones()
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = "";
        h.Vm.MonitorDeviceSticks = false;   // e.g. headset off, or held by another app

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.False(result.Success);
        Assert.Contains("couldn't open your headphones", result.Message);
        Assert.Equal(3, h.Vm.MonitorDeviceSets);   // first assignment + two retries
    }

    [Fact]
    public async Task ApplyPreset_skips_the_read_back_when_banana_cannot_be_read()
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = null;
        h.Vm.MonitorDeviceSticks = false;

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(result.Success, result.Message);   // unknown is not reported as broken
    }

    [Fact]
    public async Task Start_and_restart_failures_say_why()
    {
        using var h = new RoutingHarness();
        h.Vm.Error = "Couldn't close Voicemeeter — it is running as administrator.";

        h.Vm.RestartSucceeds = false;
        var restart = await h.Routing.RestartVoicemeeterAsync(AnyPreset());
        Assert.False(restart.Success);
        Assert.Equal(h.Vm.Error, restart.Message);

        h.Vm.StartSucceeds = false;
        var apply = await h.Routing.ApplyPresetAsync(AnyPreset());
        Assert.False(apply.Success);
        Assert.Equal(h.Vm.Error, apply.Message);
    }

    [Theory]
    [InlineData("", AudioCheckStatus.Error)]                                                  // no device on A1
    [InlineData("Voicemeeter In 2 (VB-Audio Voicemeeter VAIO)", AudioCheckStatus.Error)]      // output fed back into itself
    [InlineData("Speakers (Monitor)", AudioCheckStatus.Warning)]                              // some other device
    [InlineData("Högtalare (USB headset)", AudioCheckStatus.Ok)]
    public void Setup_check_reads_the_headphone_output_banana_really_has(string a1, AudioCheckStatus expected)
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = a1;

        var check = Assert.Single(h.Routing.RunChecks(), c => c.Title == "Headphone output");

        Assert.Equal(expected, check.Status);
    }

    // ── Windows' main output must never be an app slot: every app would play into it, and a
    //    slot sent to Others would put a browser tab in Discord without anyone assigning it ──

    private const string AuxInput = "Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)";
    private const string DesktopInput = "Voicemeeter Input (VB-Audio Voicemeeter VAIO)";

    [Theory]
    [InlineData(AuxInput, true)]
    [InlineData("Voicemeeter VAIO3 Input (VB-Audio Voicemeeter VAIO)", true)]
    [InlineData("Voicemeeter In 2 (VB-Audio Voicemeeter VAIO)", true)]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", true)]
    [InlineData(DesktopInput, false)]                       // the Desktop & Discord row: where it belongs
    [InlineData("Högtalare (PRO X 2 LIGHTSPEED)", false)]   // a real device: the user's choice (direct mode)
    [InlineData(null, false)]
    public void App_slot_inputs_are_recognised(string? device, bool expected)
        => Assert.Equal(expected, AudioRoutingService.IsSlotInput(device));

    [Fact]
    public async Task Main_output_on_an_app_slot_is_moved_back_to_the_desktop_input()
    {
        using var h = new RoutingHarness();
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = AuxInput };
        string? reported = null;
        h.Routing.DesktopOutputRestored += slot => reported = slot;

        bool moved = await h.Routing.KeepDesktopOutputAsync();

        Assert.True(moved);
        Assert.Equal(DesktopInput, h.Audio.DefaultPlay?.Name);
        Assert.Equal(AuxInput, reported);
    }

    [Theory]
    [InlineData(DesktopInput)]            // already right
    [InlineData("Headphones (USB)")]      // the user deliberately bypasses Banana
    public async Task Main_output_is_left_alone_when_it_is_not_on_a_slot(string device)
    {
        using var h = new RoutingHarness();
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = device };

        Assert.False(await h.Routing.KeepDesktopOutputAsync());
        Assert.Equal(device, h.Audio.DefaultPlay?.Name);
    }

    [Fact]
    public async Task Main_output_is_not_touched_while_banana_is_off()
    {
        using var h = new RoutingHarness();
        h.Vm.StatusValue = VoicemeeterStatus.Stopped;
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = AuxInput };

        Assert.False(await h.Routing.KeepDesktopOutputAsync());
        Assert.Equal(AuxInput, h.Audio.DefaultPlay?.Name);
    }

    [Fact]
    public async Task Changing_the_main_output_to_a_slot_is_corrected_automatically()
    {
        using var h = new RoutingHarness();
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = DesktopInput };
        var restored = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Routing.DesktopOutputRestored += slot => restored.TrySetResult(slot);

        h.Audio.ChangeDefaultPlayback(AuxInput);   // e.g. picked as the main Output device in Windows

        var done = await Task.WhenAny(restored.Task, Task.Delay(5000));
        Assert.Same(restored.Task, done);
        Assert.Equal(DesktopInput, h.Audio.DefaultPlay?.Name);
    }

    [Fact]
    public void Setup_check_calls_out_a_main_output_that_sits_on_a_slot()
    {
        using var h = new RoutingHarness();
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = AuxInput };
        h.Audio.DefaultRec = new AudioEndpointRef { Name = "Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)" };

        var check = Assert.Single(h.Routing.RunChecks(), c => c.Title == "Windows audio routing");

        Assert.Equal(AudioCheckStatus.Error, check.Status);
        Assert.Equal(AudioCheckFix.ReapplyRouting, check.Fix);
    }

    // ── "Send an app here": the undocumented per-app call must be checked, not trusted ──

    private static RoutingHarness HarnessWithAuxSlot()
    {
        var h = new RoutingHarness();
        h.Audio.Playback.Add(new AudioDeviceInfo { Id = "p-aux", Name = AuxInput });
        h.Audio.DefaultPlay = new AudioEndpointRef { Id = "p-vaio", Name = DesktopInput };
        return h;
    }

    private static readonly AudioAppInfo Chrome = new() { ProcessId = 4242, DisplayName = "chrome" };

    [Fact]
    public void Sending_an_app_to_a_slot_works_when_windows_does_what_it_says()
    {
        using var h = HarnessWithAuxSlot();

        Assert.True(h.Routing.AssignAppToSlot(Chrome, AudioSourceKind.AppAux));
        Assert.Equal(DesktopInput, h.Audio.DefaultPlay?.Name);   // main output untouched
        Assert.True(h.Routing.CanAssignAppsInApp);
    }

    [Fact]
    public void Sending_an_app_to_a_slot_is_undone_and_disabled_when_it_moves_the_main_output()
    {
        using var h = HarnessWithAuxSlot();
        h.PerApp.MovesMainOutputTo = AuxInput;   // Windows build 26200: "success", but the WHOLE PC now plays into the slot

        bool ok = h.Routing.AssignAppToSlot(Chrome, AudioSourceKind.AppAux);

        Assert.False(ok);
        Assert.Equal(DesktopInput, h.Audio.DefaultPlay?.Name);   // put back: nothing leaks to Others
        Assert.False(h.Routing.CanAssignAppsInApp);

        // …and it is not attempted again — not in this session, not after a restart on the same build.
        Assert.False(h.Routing.AssignAppToSlot(Chrome, AudioSourceKind.AppAux));
        Assert.Equal(1, h.PerApp.Calls);
        var afterRestart = new AudioRoutingService(h.Vm, h.Audio, new VoicemeeterSettingsService(null, h.Folder), h.PerApp) { WindowsBuild = 22631 };
        Assert.False(afterRestart.CanAssignAppsInApp);
    }

    [Fact]
    public void Sending_an_app_to_a_slot_is_never_tried_on_builds_known_to_move_the_main_output()
    {
        using var h = HarnessWithAuxSlot();
        var onInsiderBuild = new AudioRoutingService(h.Vm, h.Audio, h.Store, h.PerApp) { WindowsBuild = 26200 };

        Assert.False(onInsiderBuild.CanAssignAppsInApp);
        Assert.False(onInsiderBuild.AssignAppToSlot(Chrome, AudioSourceKind.AppAux));
        Assert.Equal(0, h.PerApp.Calls);
        Assert.Equal(DesktopInput, h.Audio.DefaultPlay?.Name);
    }

    // ── Sharing the headphones: Banana's exclusive (WDM) output locks them for every other app ──

    [Fact]
    public async Task Headphones_are_shared_with_other_apps_by_default()
    {
        using var h = HarnessWithHeadphones();
        h.Vm.MonitorDevice = "";

        await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(new VoicemeeterSettings().ShareMonitorDevice);
        Assert.True(h.Vm.MonitorShared);
    }

    [Fact]
    public async Task Exclusive_headphones_are_used_when_sharing_is_switched_off()
    {
        using var h = HarnessWithHeadphones();
        var settings = h.Store.Load().Settings;
        settings.ShareMonitorDevice = false;
        h.Store.SaveSettings(settings);
        h.Vm.MonitorDevice = "";

        await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.False(h.Vm.MonitorShared);
    }

    [Theory]
    [InlineData(true, true, 1)]     // want shared, but Banana has it locked  -> reopen it shared
    [InlineData(true, false, 0)]    // want shared, already shared            -> leave it (no dropout)
    [InlineData(false, false, 1)]   // want exclusive, but it is shared       -> reopen it exclusive
    [InlineData(false, true, 0)]    // want exclusive, already exclusive      -> leave it
    public async Task Headphones_are_reopened_only_when_the_sharing_mode_is_wrong(bool share, bool locked, int expectedSets)
    {
        using var h = HarnessWithHeadphones();
        var settings = h.Store.Load().Settings;
        settings.ShareMonitorDevice = share;
        h.Store.SaveSettings(settings);
        h.Vm.MonitorDevice = "Högtalare (USB headset)";   // right device already on A1
        h.Audio.Locked = locked;

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(result.Success, result.Message);
        Assert.Equal(expectedSets, h.Vm.MonitorDeviceSets);
        if (expectedSets > 0) Assert.Equal(share, h.Vm.MonitorShared);
    }

    // ── Direct mode: Banana out of the picture (it locks the headset while it runs) ──

    private static RoutingHarness HarnessWithRealDevices()
    {
        var h = new RoutingHarness();
        h.Store.SaveSettings(new VoicemeeterSettings
        {
            MicrophoneDeviceName = "Microphone (USB)",
            MonitorDeviceName = "Headphones (USB)",
        });
        h.Audio.DefaultPlay = new AudioEndpointRef { Name = "Voicemeeter Input (VB-Audio Voicemeeter VAIO)" };
        h.Audio.DefaultRec = new AudioEndpointRef { Name = "Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)" };
        return h;
    }

    [Fact]
    public async Task Direct_mode_closes_banana_and_hands_the_headset_and_mic_to_windows()
    {
        using var h = HarnessWithRealDevices();

        var result = await h.Routing.BypassAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, h.Vm.Shutdowns);
        Assert.Equal("Headphones (USB)", h.Audio.DefaultPlay?.Name);
        Assert.Equal("Microphone (USB)", h.Audio.DefaultRec?.Name);
        Assert.True(h.Routing.IsDirectMode());
    }

    [Fact]
    public async Task Direct_mode_needs_headphones_to_switch_to()
    {
        using var h = new RoutingHarness();   // nothing chosen in Setup

        var result = await h.Routing.BypassAsync();

        Assert.False(result.Success);
        Assert.Equal(0, h.Vm.Shutdowns);      // Banana is left alone
    }

    [Fact]
    public async Task Direct_mode_keeps_the_routing_when_banana_will_not_close()
    {
        using var h = HarnessWithRealDevices();
        h.Vm.ShutdownSucceeds = false;
        h.Vm.Error = "Couldn't close Voicemeeter — it is running as administrator.";

        var result = await h.Routing.BypassAsync();

        Assert.False(result.Success);
        Assert.Equal(h.Vm.Error, result.Message);
        Assert.StartsWith("Voicemeeter Input", h.Audio.DefaultPlay?.Name);   // still routed through Banana
        Assert.False(h.Routing.IsDirectMode());
    }

    [Fact]
    public async Task Applying_a_preset_leaves_direct_mode()
    {
        using var h = HarnessWithRealDevices();
        await h.Routing.BypassAsync();
        h.Vm.StatusValue = VoicemeeterStatus.Running;   // what EnsureRunningAsync does for real

        var result = await h.Routing.ApplyPresetAsync(AnyPreset());

        Assert.True(result.Success, result.Message);
        Assert.False(h.Routing.IsDirectMode());
    }

    [Theory]
    [InlineData("Voicemeeter Input (VB-Audio Voicemeeter VAIO)", true)]
    [InlineData("Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)", true)]
    [InlineData("Högtalare (PRO X 2 LIGHTSPEED)", false)]
    [InlineData("PRIMARY (KATANA3)", false)]
    [InlineData(null, false)]
    public void Voicemeeter_virtual_devices_are_recognised(string? name, bool expected)
        => Assert.Equal(expected, AudioRoutingService.IsVoicemeeterDevice(name));

    [Fact]
    public void Setup_check_reports_a_disconnected_guitar()
    {
        using var h = new RoutingHarness();
        h.Store.SaveSettings(new VoicemeeterSettings { GuitarDeviceName = "BOSS KATANA" });

        var checks = h.Routing.RunChecks();

        Assert.Contains(checks, c => c.Title == "Guitar (Katana)"
                                     && c.Status == AudioCheckStatus.Warning
                                     && c.Fix == AudioCheckFix.ConfigureDevices);
    }
}
