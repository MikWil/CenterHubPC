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

        public AudioDeviceService(ILogger<AudioDeviceService>? logger = null)
        {
            _logger = logger;
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
                var controller = new CoreAudioController();
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
                return false;
            }
        }

        private async Task<bool> SetDefaultByNameAsync(AsDeviceType type, string nameSubstring, bool communicationsToo)
        {
            if (string.IsNullOrWhiteSpace(nameSubstring)) return false;
            try
            {
                var controller = new CoreAudioController();
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
