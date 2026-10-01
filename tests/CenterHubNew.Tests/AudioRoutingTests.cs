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
