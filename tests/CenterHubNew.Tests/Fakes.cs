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

    /// <summary>What Banana "really" has on A1: null = can't be read, "" = no device.</summary>
    public string? MonitorDevice;
    /// <summary>False simulates Banana failing to open the requested headphones.</summary>
    public bool MonitorDeviceSticks = true;
    public int MonitorDeviceSets;
    public readonly Dictionary<int, string> InputDevices = new();
    public int InputDeviceSets;
    public bool StartSucceeds = true;
    public bool RestartSucceeds = true;
    public string? Error;

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
    public string? LastError => Error;

    public VoicemeeterStatus RefreshStatus() => StatusValue;
    public Task<bool> EnsureRunningAsync(CancellationToken ct = default) => Task.FromResult(StartSucceeds);
    public bool Connect() => true;
    public void Disconnect() { }
    public void Reconnect() { }
    public bool RestartAudioEngine() => true;
    public Task<bool> RestartApplicationAsync(CancellationToken ct = default) => Task.FromResult(RestartSucceeds);

    public bool ShutdownSucceeds = true;
    public int Shutdowns;
    public Task<bool> ShutdownAsync(CancellationToken ct = default)
    {
        Shutdowns++;
        if (ShutdownSucceeds) StatusValue = VoicemeeterStatus.Stopped;
        return Task.FromResult(ShutdownSucceeds);
    }
    public void OpenUi() { }

    public bool SetHardwareInput(int stripIndex, string deviceName)
    {
        InputDeviceSets++;
        InputDevices[stripIndex] = deviceName;
        return true;
    }

    /// <summary>How the headphones were last opened: true = shared (MME), false = exclusive (WDM).</summary>
    public bool? MonitorShared;

    public bool SetMonitorDevice(string deviceName, bool shared)
    {
        MonitorDeviceSets++;
        if (MonitorDeviceSticks) { MonitorDevice = deviceName; MonitorShared = shared; }
        return true;
    }

    public string? GetMonitorDeviceName() => MonitorDevice;
    public string? GetHardwareInputName(int stripIndex) => InputDevices.TryGetValue(stripIndex, out var name) ? name : "";
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

    /// <summary>Whether some app holds the headphones exclusively (null = can't tell).</summary>
    public bool? Locked;
    public bool? IsPlaybackDeviceLocked(AudioEndpointRef device) => Locked;

    public event Action? DefaultPlaybackChanged;
    /// <summary>Simulates the user (or Windows) changing the main output device.</summary>
    public void ChangeDefaultPlayback(string name)
    {
        DefaultPlay = new AudioEndpointRef { Name = name };
        DefaultPlaybackChanged?.Invoke();
    }

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
    public Task<bool> SetDefaultPlaybackAsync(AudioEndpointRef device, bool communicationsToo)
    {
        if (!Playback.Any(d => d.Name == device.Name)) return Task.FromResult(false);
        DefaultPlay = device;
        return Task.FromResult(true);
    }

    public Task<bool> SetDefaultRecordingAsync(AudioEndpointRef device, bool communicationsToo)
    {
        if (!Recording.Any(d => d.Name == device.Name)) return Task.FromResult(false);
        DefaultRec = device;
        return Task.FromResult(true);
    }
    public Task<bool> SetDefaultPlaybackByNameAsync(string nameSubstring, bool communicationsToo)
    {
        var match = Playback.FirstOrDefault(d => d.Name.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase));
        if (match is null) return Task.FromResult(false);
        DefaultPlay = new AudioEndpointRef { Id = match.Id, Name = match.Name };
        return Task.FromResult(true);
    }
    public Task<bool> SetDefaultRecordingByNameAsync(string nameSubstring, bool communicationsToo)
        => Task.FromResult(Recording.Any(d => d.Name.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase)));
    public Task RestoreSnapshotAsync(AudioDeviceSnapshot snapshot) => Task.CompletedTask;
}

/// <summary>
/// Stand-in for the undocumented per-app call. <see cref="MovesMainOutputTo"/> reproduces what
/// Windows build 26200 does: report success while moving Windows' MAIN output to the slot.
/// </summary>
internal sealed class FakePerApp : PerAppAudioService
{
    private readonly FakeAudio _audio;
    public string? MovesMainOutputTo;
    public int Calls;

    public FakePerApp(FakeAudio audio) => _audio = audio;

    public override int? ResolveLivePid(int preferredPid, string processName) => preferredPid;

    public override bool SetAppRenderDevice(int processId, string mmDeviceId)
    {
        Calls++;
        if (MovesMainOutputTo is not null) _audio.DefaultPlay = new AudioEndpointRef { Id = mmDeviceId, Name = MovesMainOutputTo };
        return true;
    }
}

/// <summary>Routing service wired to fakes and a throwaway settings folder.</summary>
internal sealed class RoutingHarness : IDisposable
{
    public readonly string Folder = Path.Combine(Path.GetTempPath(), "centerhub-routing-" + Guid.NewGuid().ToString("N"));
    public readonly FakeVoicemeeter Vm = new();
    public readonly FakeAudio Audio = new();
    public readonly VoicemeeterSettingsService Store;
    public readonly FakePerApp PerApp;
    public readonly AudioRoutingService Routing;

    public RoutingHarness()
    {
        Store = new VoicemeeterSettingsService(null, Folder);
        PerApp = new FakePerApp(Audio);
        Routing = new AudioRoutingService(Vm, Audio, Store, PerApp)
        {
            DeviceSettleDelay = TimeSpan.Zero,   // no real hardware to wait for
            WindowsBuild = 22631,                // a build where the per-app call is expected to work
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
    }
}
