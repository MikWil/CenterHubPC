using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;
using Xunit;

namespace CenterHubNew.Tests;

public class SetupWizardTests : IDisposable
{
    private readonly RoutingHarness _h = new();
    private readonly UiSettingsService _ui;

    public SetupWizardTests() => _ui = new UiSettingsService(null, _h.Folder);

    public void Dispose() => _h.Dispose();

    /// <summary>A wizard on fakes whose UI marshalling runs inline; devices are loaded.</summary>
    private async Task<SetupWizardViewModel> MakeAsync()
    {
        var vm = new SetupWizardViewModel(_h.Routing, _h.Audio, _h.Store, _ui, null, a => a());
        await vm.ReloadAsync();
        return vm;
    }

    /// <summary>Windows currently plays to the USB headphones and records from the USB mic.</summary>
    private void UseUsbDefaults()
    {
        _h.Audio.DefaultPlay = new AudioEndpointRef { Id = "p-head", Name = "Headphones (USB)" };
        _h.Audio.DefaultRec = new AudioEndpointRef { Id = "r-mic", Name = "Microphone (USB)" };
    }

    // ─────────────────── Devices ───────────────────

    [Fact]
    public async Task Device_lists_never_offer_Voicemeeters_own_endpoints()
    {
        _h.Audio.Playback.Add(new() { Id = "p-aux", Name = "Voicemeeter Aux Input (VB-Audio Voicemeeter AUX)" });
        _h.Audio.Recording.Add(new() { Id = "r-b2", Name = "Voicemeeter Out B2 (VB-Audio Voicemeeter VAIO)", IsCapture = true });

        var vm = await MakeAsync();

        Assert.Equal(new[] { "Headphones (USB)" }, vm.HeadphoneDevices.Select(d => d.Name));
        Assert.Equal(new[] { "Microphone (USB)" }, vm.MicrophoneDevices.Select(d => d.Name));
        Assert.Equal(new[] { "Microphone (USB)" }, vm.GuitarDevices.Select(d => d.Name));
    }

    [Fact]
    public async Task Saved_settings_are_preselected_over_the_windows_defaults()
    {
        _h.Audio.Playback.Add(new() { Id = "p-spk", Name = "Speakers (Realtek)" });
        _h.Audio.Recording.Add(new() { Id = "r-gtr", Name = "Katana (USB)", IsCapture = true });
        UseUsbDefaults();
        _h.Store.SaveSettings(new VoicemeeterSettings
        {
            MonitorDeviceId = "p-spk", MonitorDeviceName = "Speakers (Realtek)",
            MicrophoneDeviceName = "Microphone (USB)",
            GuitarDeviceId = "r-gtr", GuitarDeviceName = "Katana (USB)",
            ShareMonitorDevice = false,
        });

        var vm = await MakeAsync();

        Assert.Equal("Speakers (Realtek)", vm.SelectedHeadphones?.Name);
        Assert.Equal("Microphone (USB)", vm.SelectedMicrophone?.Name);
        Assert.Equal("Katana (USB)", vm.SelectedGuitar?.Name);
        Assert.False(vm.ShareHeadset);
    }

    [Fact]
    public async Task Without_saved_settings_the_windows_defaults_are_preselected()
    {
        UseUsbDefaults();

        var vm = await MakeAsync();

        Assert.Equal("Headphones (USB)", vm.SelectedHeadphones?.Name);
        Assert.Equal("Microphone (USB)", vm.SelectedMicrophone?.Name);
        Assert.Null(vm.SelectedGuitar);      // there is no "default guitar"
        Assert.True(vm.ShareHeadset);        // shared headphones are the default
    }

    [Fact]
    public async Task A_voicemeeter_default_is_not_preselected()
    {
        _h.Audio.DefaultPlay = new AudioEndpointRef { Id = "p-vaio", Name = "Voicemeeter Input (VB-Audio Voicemeeter VAIO)" };
        _h.Audio.DefaultRec = new AudioEndpointRef { Id = "r-b1", Name = "Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)" };

        var vm = await MakeAsync();

        Assert.Null(vm.SelectedHeadphones);
        Assert.Null(vm.SelectedMicrophone);
    }

    [Fact]
    public async Task Next_is_blocked_on_the_device_step_until_headphones_and_mic_are_chosen()
    {
        var vm = await MakeAsync();
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentStep);

        Assert.False(vm.CanGoNext);
        Assert.True(vm.NeedsDeviceChoice);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentStep);

        vm.SelectedHeadphones = vm.HeadphoneDevices.First();
        Assert.False(vm.CanGoNext);          // the microphone is still missing
        vm.SelectedMicrophone = vm.MicrophoneDevices.First();
        Assert.True(vm.CanGoNext);

        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.CurrentStep);
    }

    [Fact]
    public async Task Leaving_the_device_step_saves_the_devices_and_keeps_the_other_settings()
    {
        UseUsbDefaults();
        _h.Store.SaveSettings(new VoicemeeterSettings
        {
            MicGainDb = 6f,
            GuitarGainDb = -3f,
            MonitorMicrophone = true,
            PreferredSendBus = VoicemeeterBBus.B2,
            AutoRestoreOnStartup = true,
        });
        _h.Store.SavePresets(_h.Routing.BuildDefaultPresets(), null);

        var vm = await MakeAsync();
        await vm.NextCommand.ExecuteAsync(null);              // 1 → 2
        vm.ShareHeadset = false;
        await vm.NextCommand.ExecuteAsync(null);              // 2 → 3 (saves)

        var state = _h.Store.Load();
        var s = state.Settings;
        Assert.Equal("p-head", s.MonitorDeviceId);
        Assert.Equal("Headphones (USB)", s.MonitorDeviceName);
        Assert.Equal("r-mic", s.MicrophoneDeviceId);
        Assert.Equal("Microphone (USB)", s.MicrophoneDeviceName);
        Assert.Null(s.GuitarDeviceName);
        Assert.False(s.ShareMonitorDevice);
        // …and nothing else was touched.
        Assert.Equal(6f, s.MicGainDb);
        Assert.Equal(-3f, s.GuitarGainDb);
        Assert.True(s.MonitorMicrophone);
        Assert.Equal(VoicemeeterBBus.B2, s.PreferredSendBus);
        Assert.True(s.AutoRestoreOnStartup);
        Assert.NotEmpty(state.Presets);
    }

    // ─────────────────── Navigation / completion ───────────────────

    [Fact]
    public async Task Steps_are_bounded_and_the_last_one_says_Finish()
    {
        UseUsbDefaults();
        var vm = await MakeAsync();

        Assert.Equal(1, vm.CurrentStep);
        Assert.False(vm.CanGoBack);
        vm.BackCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStep);
        Assert.Equal("Step 1 of 5", vm.StepLabel);
        Assert.Equal("Next", vm.NextLabel);

        for (int i = 2; i <= SetupWizardViewModel.StepCount; i++)
        {
            await vm.NextCommand.ExecuteAsync(null);
            Assert.Equal(i, vm.CurrentStep);
        }

        Assert.True(vm.IsLastStep);
        Assert.Equal("Finish", vm.NextLabel);
        Assert.Equal("Step 5 of 5", vm.StepLabel);

        vm.BackCommand.Execute(null);
        Assert.Equal(4, vm.CurrentStep);
        Assert.False(vm.IsLastStep);
    }

    [Fact]
    public async Task Without_Voicemeeter_the_first_step_cannot_advance()
    {
        _h.Vm.Installed = false;
        var vm = await MakeAsync();

        Assert.True(vm.VoicemeeterMissing);
        Assert.False(vm.CanGoNext);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentStep);
    }

    [Fact]
    public async Task Finish_marks_the_setup_completed_and_closes()
    {
        UseUsbDefaults();
        var vm = await MakeAsync();
        int closed = 0;
        vm.CloseRequested += () => closed++;

        for (int i = 1; i < SetupWizardViewModel.StepCount; i++) await vm.NextCommand.ExecuteAsync(null);
        Assert.False(_ui.Current.AudioSetupCompleted);
        await vm.NextCommand.ExecuteAsync(null);              // "Finish"

        Assert.True(_ui.Current.AudioSetupCompleted);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Skip_marks_the_setup_completed_and_closes()
    {
        var vm = await MakeAsync();
        int closed = 0;
        vm.CloseRequested += () => closed++;

        vm.SkipCommand.Execute(null);

        Assert.True(_ui.Current.AudioSetupCompleted);
        Assert.Equal(1, closed);
        Assert.True(new UiSettingsService(null, _h.Folder).Current.AudioSetupCompleted);   // persisted
    }

    [Fact]
    public async Task Finishing_without_routing_when_Voicemeeter_is_missing_completes_too()
    {
        _h.Vm.Installed = false;
        var vm = await MakeAsync();
        int closed = 0;
        vm.CloseRequested += () => closed++;

        vm.FinishWithoutRoutingCommand.Execute(null);

        Assert.True(_ui.Current.AudioSetupCompleted);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task The_wizard_ignores_backdrop_clicks_and_starts_over_each_time_it_is_shown()
    {
        UseUsbDefaults();
        var vm = await MakeAsync();
        Assert.False(vm.CloseOnBackdropClick);

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.CurrentStep);

        vm.OnShown();
        await vm.LoadTask;

        Assert.Equal(1, vm.CurrentStep);
        Assert.Equal("Headphones (USB)", vm.SelectedHeadphones?.Name);
        vm.OnClosed();   // nothing playing: must not throw
    }

    // ─────────────────── Voicemeeter / routing ───────────────────

    [Fact]
    public async Task Starting_Voicemeeter_reports_success_or_the_reason_it_failed()
    {
        _h.Vm.StatusValue = VoicemeeterStatus.Stopped;
        var vm = await MakeAsync();
        Assert.True(vm.VoicemeeterNeedsStart);

        _h.Vm.StartSucceeds = false;
        _h.Vm.Error = "Voicemeeter didn't answer in time.";
        await vm.StartVoicemeeterCommand.ExecuteAsync(null);
        Assert.False(vm.VoicemeeterRunning);
        Assert.Equal("Voicemeeter didn't answer in time.", vm.VoicemeeterMessage);

        _h.Vm.StartSucceeds = true;
        await vm.StartVoicemeeterCommand.ExecuteAsync(null);
        Assert.True(vm.VoicemeeterRunning);
        Assert.True(vm.VoicemeeterReady);
    }

    [Fact]
    public async Task Applying_routing_shows_the_result_and_a_live_summary()
    {
        UseUsbDefaults();
        var vm = await MakeAsync();
        Assert.NotEmpty(vm.PresetChips);
        Assert.True(vm.PresetChips.Count(c => c.IsActive) == 1);

        await vm.ApplyRoutingCommand.ExecuteAsync(null);

        Assert.True(vm.RoutingSucceeded);
        Assert.Contains("Applied", vm.RoutingResultText);
        Assert.NotEmpty(vm.CheckRows);
        Assert.All(vm.CheckRows, r => Assert.Contains(r.Icon, new[] { "✓", "!", "✕" }));
    }

    [Fact]
    public async Task A_failed_apply_shows_the_specific_failure_text()
    {
        UseUsbDefaults();
        var vm = await MakeAsync();
        _h.Vm.StartSucceeds = false;
        _h.Vm.Error = "Banana crashed on start.";

        await vm.ApplyRoutingCommand.ExecuteAsync(null);

        Assert.False(vm.RoutingSucceeded);
        Assert.Equal("Banana crashed on start.", vm.RoutingResultText);
    }

    [Fact]
    public async Task Choosing_a_preset_changes_which_chip_is_active_and_what_gets_applied()
    {
        var vm = await MakeAsync();
        var gaming = vm.PresetChips.First(c => c.Name == "Gaming");

        vm.SelectPresetCommand.Execute(gaming);

        Assert.Equal("Gaming", vm.SelectedPreset?.Name);
        Assert.Equal("Gaming", vm.PresetChips.Single(c => c.IsActive).Name);
    }

    [Fact]
    public async Task Not_hearing_the_test_sound_lists_at_most_the_three_worst_checks()
    {
        _h.Vm.StatusValue = VoicemeeterStatus.Stopped;      // engine warning + routing warning + unset devices…
        var vm = await MakeAsync();

        await vm.DidNotHearCommand.ExecuteAsync(null);

        Assert.True(vm.TestNotHeard);
        Assert.InRange(vm.ProblemRows.Count, 1, 3);
        Assert.All(vm.ProblemRows, r => Assert.NotEqual("✓", r.Icon));
        Assert.Contains(vm.ProblemRows, r => r.HasFix);
    }

    [Fact]
    public async Task Hearing_the_test_sound_clears_the_problem_list()
    {
        var vm = await MakeAsync();
        await vm.DidNotHearCommand.ExecuteAsync(null);

        vm.HeardItCommand.Execute(null);

        Assert.True(vm.TestHeard);
        Assert.False(vm.TestNotHeard);
        Assert.Empty(vm.ProblemRows);
    }

    // ─────────────────── The chime (pure) ───────────────────

    [Fact]
    public void The_chime_is_about_six_tenths_of_a_second_and_peaks_at_minus_12_dBFS()
    {
        var chime = SetupWizardViewModel.GenerateChime(48000);

        Assert.Equal(28800, chime.Length);
        double peak = chime.Max(s => Math.Abs((double)s));
        Assert.Equal((double)SetupWizardViewModel.ChimePeak, peak, 4);
        Assert.Equal(-12.0, 20 * Math.Log10(peak), 1);
    }

    [Fact]
    public void The_chime_fades_in_and_out_without_clicking()
    {
        var chime = SetupWizardViewModel.GenerateChime(48000);
        double peak = chime.Max(s => Math.Abs((double)s));

        Assert.Equal(0f, chime[0]);
        Assert.Equal(0f, chime[^1]);
        Assert.True(chime.Take(48).Max(s => Math.Abs((double)s)) < 0.25 * peak, "the first millisecond is still fading in");
        Assert.True(chime.TakeLast(48).Max(s => Math.Abs((double)s)) < 0.05 * peak, "the last millisecond is faded out");
        Assert.Contains(chime, s => Math.Abs((double)s) > 0.5 * peak);     // and it is not silent in between
    }

    [Fact]
    public void The_chime_is_deterministic_and_scales_with_the_sample_rate()
    {
        Assert.Equal(SetupWizardViewModel.GenerateChime(48000), SetupWizardViewModel.GenerateChime(48000));
        Assert.Equal(26460, SetupWizardViewModel.GenerateChime(44100).Length);
    }
}
