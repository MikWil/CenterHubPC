using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioSwitcher.AudioApi.CoreAudio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using CenterHubNew.MVVM.Models;
using AsDeviceType = AudioSwitcher.AudioApi.DeviceType;
using AsDeviceState = AudioSwitcher.AudioApi.DeviceState;

namespace CenterHubNew.MVVM.Services
{
    /// <inheritdoc cref="IAudioDeviceService"/>
    public sealed class AudioDeviceService : IAudioDeviceService
    {
        private readonly ILogger<AudioDeviceService>? _logger;

        // Windows hands every caller in the process the SAME device-enumerator COM object, and
        // .NET keeps one wrapper per COM object. This service uses two libraries on it: NAudio
        // (typed wrapper) and AudioSwitcher (generic wrapper). If AudioSwitcher gets there first,
        // its generic wrapper is the one .NET remembers and every later NAudio enumeration throws
        // InvalidCastException for the rest of the session — device lists come back empty and the
        // setup check claims Voicemeeter's devices are missing. Holding one NAudio enumerator for
        // the service's lifetime keeps the typed wrapper in place, which both libraries can use.
        private static MMDeviceEnumerator? _comAnchor;
        private static readonly object _comAnchorGate = new();

        private readonly DefaultDeviceListener _listener;

        public event Action? DefaultPlaybackChanged;

        public AudioDeviceService(ILogger<AudioDeviceService>? logger = null)
        {
            _logger = logger;
            EnsureComAnchor(logger);

            // Windows calls this back on its own threads; hop to the pool before telling anyone,
            // because calling back into the audio APIs from inside the notification can deadlock.
            _listener = new DefaultDeviceListener(() => Task.Run(() =>
            {
                try { DefaultPlaybackChanged?.Invoke(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "A default-device listener threw"); }
            }));
            try
            {
                lock (_comAnchorGate) _comAnchor?.RegisterEndpointNotificationCallback(_listener);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Could not watch for default-device changes"); }
        }

        /// <summary>Forwards "the default playback device changed" from Windows; everything else is ignored.</summary>
        private sealed class DefaultDeviceListener : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
        {
            private readonly Action _onRenderDefaultChanged;
            public DefaultDeviceListener(Action onRenderDefaultChanged) => _onRenderDefaultChanged = onRenderDefaultChanged;

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render && role == Role.Multimedia) _onRenderDefaultChanged();
            }

            public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
            public void OnDeviceAdded(string pwstrDeviceId) { }
            public void OnDeviceRemoved(string deviceId) { }
            public void OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey key) { }
        }

        private static void EnsureComAnchor(ILogger? logger)
        {
            lock (_comAnchorGate)
            {
                if (_comAnchor != null) return;
                try { _comAnchor = new MMDeviceEnumerator(); }
                catch (Exception ex) { logger?.LogWarning(ex, "Could not create the audio device enumerator"); }
            }
        }

        private static CoreAudioController? _sharedController;

        /// <summary>
        /// The one AudioSwitcher controller for the whole app. A controller enumerates every device
        /// and subscribes to Windows' device notifications when it is created and is never released,
        /// so creating one per switch leaked them and made each switch slower than the last
        /// (1.6 s growing to 5.7 s over ten preset changes). It tracks device changes by itself.
        /// </summary>
        internal static CoreAudioController SharedController
        {
            get
            {
                lock (_comAnchorGate)
                {
                    if (_sharedController != null) return _sharedController;
                }
                EnsureComAnchor(null);   // the typed NAudio wrapper must exist first (see above)
                lock (_comAnchorGate)
                {
                    return _sharedController ??= new CoreAudioController();
                }
            }
        }

        /// <summary>Drop the shared controller after a failure so the next switch starts from a fresh one.</summary>
        internal static void ResetSharedController()
        {
            CoreAudioController? old;
            lock (_comAnchorGate) { old = _sharedController; _sharedController = null; }
            try { old?.Dispose(); } catch { /* best effort */ }
        }

        // ─────────────────── Enumeration (NAudio) ───────────────────

        public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => Enumerate(DataFlow.Render);
        public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => Enumerate(DataFlow.Capture);

        private IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow)
        {
            var result = new List<AudioDeviceInfo>();
            try
            {
                using var en = new MMDeviceEnumerator();
                foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    try { result.Add(new AudioDeviceInfo { Id = d.ID, Name = d.FriendlyName, IsCapture = flow == DataFlow.Capture }); }
                    finally { d.Dispose(); }
                }
            }
            catch (Exception ex) { _logger?.LogError(ex, "Error enumerating {Flow} devices", flow); }
            return result;
        }

        // ─────────────────── Current defaults (NAudio) ───────────────────

        public AudioEndpointRef? GetDefaultPlayback() => GetDefault(DataFlow.Render, Role.Multimedia);
        public AudioEndpointRef? GetDefaultCommunicationsPlayback() => GetDefault(DataFlow.Render, Role.Communications);
        public AudioEndpointRef? GetDefaultRecording() => GetDefault(DataFlow.Capture, Role.Multimedia);
        public AudioEndpointRef? GetDefaultCommunicationsRecording() => GetDefault(DataFlow.Capture, Role.Communications);

        private AudioEndpointRef? GetDefault(DataFlow flow, Role role)
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                if (!en.HasDefaultAudioEndpoint(flow, role)) return null;
                using var d = en.GetDefaultAudioEndpoint(flow, role);
                return new AudioEndpointRef { Id = d.ID, Name = d.FriendlyName };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read default {Flow}/{Role}", flow, role);
                return null;
            }
        }

        private const uint AudclntDeviceInUse = 0x8889000A;   // AUDCLNT_E_DEVICE_IN_USE

        public bool? IsPlaybackDeviceLocked(AudioEndpointRef device)
        {
            if (device is null || !device.HasValue) return null;
            try
            {
                using var en = new MMDeviceEnumerator();
                MMDevice? match = null;
                foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    bool isMatch = match is null &&
                        ((!string.IsNullOrEmpty(device.Id) && string.Equals(d.ID, device.Id, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrEmpty(device.Name) && string.Equals(d.FriendlyName, device.Name, StringComparison.OrdinalIgnoreCase)));
                    if (isMatch) match = d; else d.Dispose();
                }
                if (match is null) return null;

                using (match)
                {
                    // Asking for a shared stream (never started, so nothing is heard) fails with
                    // "device in use" exactly when another app has the endpoint exclusively.
                    var client = match.AudioClient;
                    try
                    {
                        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.None,
                            1_000_000 /* 100 ms, in 100-ns units */, 0, client.MixFormat, Guid.Empty);
                        return false;
                    }
                    catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == AudclntDeviceInUse)
                    {
                        return true;
                    }
                    finally { client.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not probe exclusive use of {Device}", device);
                return null;
            }
        }

        public AudioDeviceSnapshot CaptureSnapshot() => new()
        {
            DefaultPlayback = GetDefaultPlayback(),
            DefaultCommunicationsPlayback = GetDefaultCommunicationsPlayback(),
            DefaultRecording = GetDefaultRecording(),
            DefaultCommunicationsRecording = GetDefaultCommunicationsRecording(),
            CapturedUtc = DateTime.UtcNow,
        };

        // ─────────────────── Switching (AudioSwitcher) ───────────────────

        public Task<bool> SetDefaultPlaybackAsync(AudioEndpointRef device, bool communicationsToo)
            => SetDefaultAsync(AsDeviceType.Playback, device, setMultimedia: true, setCommunications: communicationsToo);

        public Task<bool> SetDefaultRecordingAsync(AudioEndpointRef device, bool communicationsToo)
            => SetDefaultAsync(AsDeviceType.Capture, device, setMultimedia: true, setCommunications: communicationsToo);

        public async Task<bool> SetDefaultPlaybackByNameAsync(string nameSubstring, bool communicationsToo)
            => await SetDefaultByNameAsync(AsDeviceType.Playback, nameSubstring, communicationsToo).ConfigureAwait(false);

        public async Task<bool> SetDefaultRecordingByNameAsync(string nameSubstring, bool communicationsToo)
            => await SetDefaultByNameAsync(AsDeviceType.Capture, nameSubstring, communicationsToo).ConfigureAwait(false);

        private async Task<bool> SetDefaultAsync(AsDeviceType type, AudioEndpointRef? device, bool setMultimedia, bool setCommunications)
        {
            if (device is null || !device.HasValue) return false;
            try
            {
                var controller = SharedController;
                var devices = await controller.GetDevicesAsync(type, AsDeviceState.Active).ConfigureAwait(false);
                var match = MatchDevice(devices, device);
                if (match is null)
                {
                    _logger?.LogWarning("No active {Type} device matched {Device}", type, device);
                    return false;
                }

                if (setMultimedia) await match.SetAsDefaultAsync().ConfigureAwait(false);
                if (setCommunications) await match.SetAsDefaultCommunicationsAsync().ConfigureAwait(false);
                _logger?.LogInformation("Set {Type} default -> {Name} (comms={Comms})", type, match.FullName, setCommunications);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to set {Type} default to {Device}", type, device);
                ResetSharedController();
                return false;
            }
        }

        private async Task<bool> SetDefaultByNameAsync(AsDeviceType type, string nameSubstring, bool communicationsToo)
        {
            if (string.IsNullOrWhiteSpace(nameSubstring)) return false;
            try
            {
                var controller = SharedController;
                var devices = await controller.GetDevicesAsync(type, AsDeviceState.Active).ConfigureAwait(false);
                var match = devices.FirstOrDefault(d =>
                    (d.FullName?.IndexOf(nameSubstring, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d.Name?.IndexOf(nameSubstring, StringComparison.OrdinalIgnoreCase) >= 0));
                if (match is null)
                {
                    _logger?.LogWarning("No active {Type} device name contains '{Name}'", type, nameSubstring);
                    return false;
                }

                await match.SetAsDefaultAsync().ConfigureAwait(false);
                if (communicationsToo) await match.SetAsDefaultCommunicationsAsync().ConfigureAwait(false);
                _logger?.LogInformation("Set {Type} default -> {Name} (by name '{Sub}')", type, match.FullName, nameSubstring);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to set {Type} default by name '{Name}'", type, nameSubstring);
                ResetSharedController();
                return false;
            }
        }

        public async Task RestoreSnapshotAsync(AudioDeviceSnapshot snapshot)
        {
            if (snapshot is null) return;

            // Playback: multimedia + communications may be different endpoints.
            await SetDefaultAsync(AsDeviceType.Playback, snapshot.DefaultPlayback, setMultimedia: true, setCommunications: false).ConfigureAwait(false);
            await SetDefaultAsync(AsDeviceType.Playback, snapshot.DefaultCommunicationsPlayback, setMultimedia: false, setCommunications: true).ConfigureAwait(false);

            // Recording: multimedia + communications.
            await SetDefaultAsync(AsDeviceType.Capture, snapshot.DefaultRecording, setMultimedia: true, setCommunications: false).ConfigureAwait(false);
            await SetDefaultAsync(AsDeviceType.Capture, snapshot.DefaultCommunicationsRecording, setMultimedia: false, setCommunications: true).ConfigureAwait(false);

            _logger?.LogInformation("Restored audio snapshot captured {Utc:o}", snapshot.CapturedUtc);
        }

        // ─────────────────── Matching ───────────────────

        /// <summary>Match by endpoint ID first (AudioSwitcher exposes it as RealId), then full name, then friendly name.</summary>
        private static CoreAudioDevice? MatchDevice(IEnumerable<CoreAudioDevice> devices, AudioEndpointRef target)
        {
            var list = devices.ToList();

            if (!string.IsNullOrEmpty(target.Id))
            {
                var byId = list.FirstOrDefault(d =>
                    string.Equals(d.RealId, target.Id, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }

            if (!string.IsNullOrEmpty(target.Name))
            {
                return list.FirstOrDefault(d => string.Equals(d.FullName, target.Name, StringComparison.OrdinalIgnoreCase))
                    ?? list.FirstOrDefault(d => string.Equals(d.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                    ?? list.FirstOrDefault(d => d.FullName?.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return null;
        }
    }
}
