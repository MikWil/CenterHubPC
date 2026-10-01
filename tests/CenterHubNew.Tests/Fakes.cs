using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.Tests;

/// <summary>In-memory Voicemeeter that records every routing call.</summary>
internal sealed class FakeVoicemeeter : IVoicemeeterService
{
    public bool Installed = true;
    public VoicemeeterStatus StatusValue = VoicemeeterStatus.Running;
    public VoicemeeterKind KindValue = VoicemeeterKind.Banana;
    public readonly List<(int Strip, VoicemeeterBus Bus, bool On)> Routes = new();

    public int MicStripIndex => 0;
    public int GuitarStripIndex => 1;
    public int LineInStripIndex => 2;
    public int VaioStripIndex => 3;
    public int AuxStripIndex => 4;
    public int Vaio3StripIndex => 5;
    public int MonitorBusIndex => 0;

    public bool IsInstalled => Installed;
    public VoicemeeterStatus Status => StatusValue;
    public VoicemeeterKind Kind => KindValue;
    public bool IsConnected => true;

    public VoicemeeterStatus RefreshStatus() => StatusValue;
    public Task<bool> EnsureRunningAsync(CancellationToken ct = default) => Task.FromResult(true);
    public bool Connect() => true;
    public void Disconnect() { }
    public void Reconnect() { }
    public bool RestartAudioEngine() => true;
    public Task<bool> RestartApplicationAsync(CancellationToken ct = default) => Task.FromResult(true);
    public void OpenUi() { }

    public bool SetHardwareInput(int stripIndex, string deviceName) => true;
    public bool SetMonitorDevice(string deviceName) => true;
    public bool SetStripGain(int stripIndex, float gainDb) => true;
    public bool SetStripMute(int stripIndex, bool mute) => true;
    public bool SetRoute(int stripIndex, VoicemeeterBus bus, bool on) { Routes.Add((stripIndex, bus, on)); return true; }
    public bool SetRouteToB1(int stripIndex, bool on) => SetRoute(stripIndex, VoicemeeterBus.B1, on);
    public bool SetMonitorRouting(int stripIndex, bool toA1) => SetRoute(stripIndex, VoicemeeterBus.A1, toA1);
    public void Dispose() { }
}

/// <summary>Windows audio stand-in with Voicemeeter's virtual devices present by default.</summary>
internal sealed class FakeAudio : IAudioDeviceService
{
    public List<AudioDeviceInfo> Playback = new()
    {
        new() { Id = "p-vaio", Name = "Voicemeeter Input (VB-Audio Voicemeeter VAIO)" },
        new() { Id = "p-head", Name = "Headphones (USB)" },
    };
    public List<AudioDeviceInfo> Recording = new()
    {
        new() { Id = "r-b1", Name = "Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)", IsCapture = true },
        new() { Id = "r-mic", Name = "Microphone (USB)", IsCapture = true },
    };
    public AudioEndpointRef? DefaultPlay;
    public AudioEndpointRef? DefaultRec;

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => Playback;
    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => Recording;
    public AudioEndpointRef? GetDefaultPlayback() => DefaultPlay;
    public AudioEndpointRef? GetDefaultCommunicationsPlayback() => DefaultPlay;
    public AudioEndpointRef? GetDefaultRecording() => DefaultRec;
    public AudioEndpointRef? GetDefaultCommunicationsRecording() => DefaultRec;
    public AudioDeviceSnapshot CaptureSnapshot() => new()
    {
        DefaultPlayback = DefaultPlay, DefaultCommunicationsPlayback = DefaultPlay,
        DefaultRecording = DefaultRec, DefaultCommunicationsRecording = DefaultRec,
    };
    public Task<bool> SetDefaultPlaybackAsync(AudioEndpointRef device, bool communicationsToo) => Task.FromResult(true);
    public Task<bool> SetDefaultRecordingAsync(AudioEndpointRef device, bool communicationsToo) => Task.FromResult(true);
    public Task<bool> SetDefaultPlaybackByNameAsync(string nameSubstring, bool communicationsToo)
        => Task.FromResult(Playback.Any(d => d.Name.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase)));
    public Task<bool> SetDefaultRecordingByNameAsync(string nameSubstring, bool communicationsToo)
        => Task.FromResult(Recording.Any(d => d.Name.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase)));
    public Task RestoreSnapshotAsync(AudioDeviceSnapshot snapshot) => Task.CompletedTask;
}

/// <summary>Routing service wired to fakes and a throwaway settings folder.</summary>
internal sealed class RoutingHarness : IDisposable
{
    public readonly string Folder = Path.Combine(Path.GetTempPath(), "centerhub-routing-" + Guid.NewGuid().ToString("N"));
    public readonly FakeVoicemeeter Vm = new();
    public readonly FakeAudio Audio = new();
    public readonly VoicemeeterSettingsService Store;
    public readonly AudioRoutingService Routing;

    public RoutingHarness()
    {
        Store = new VoicemeeterSettingsService(null, Folder);
        Routing = new AudioRoutingService(Vm, Audio, Store, new PerAppAudioService());
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
    }
}
