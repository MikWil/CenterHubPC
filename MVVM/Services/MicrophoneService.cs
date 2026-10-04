using System;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Mute state of the microphone Windows uses for calls (the default *communications* capture
    /// device — what Discord and Teams record from when set to Default). One place for the hotkey,
    /// the status strip, the command palette and the tray menu. DI singleton.
    /// </summary>
    public sealed class MicrophoneService : IDisposable
    {
        private readonly ILogger<MicrophoneService>? _logger;
        private readonly object _gate = new();
        private MMDevice? _watched;
        private string? _watchedId;

        public MicrophoneService(ILogger<MicrophoneService>? logger = null) => _logger = logger;

        /// <summary>Raised (on an audio thread) when the watched microphone's mute state changes.</summary>
        public event Action<bool>? MuteChanged;

        /// <summary>True/false for the current default communications mic; null when there is none.</summary>
        public bool? IsMuted
        {
            get
            {
                try
                {
                    lock (_gate)
                    {
                        var mic = WatchDefault();
                        return mic?.AudioEndpointVolume.Mute;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not read the microphone mute state");
                    return null;
                }
            }
        }

        /// <summary>Name of the current default communications mic (null when there is none).</summary>
        public string? DeviceName
        {
            get
            {
                try { lock (_gate) return WatchDefault()?.FriendlyName; }
                catch { return null; }
            }
        }

        /// <summary>Flip the mute state. Returns the new state, or null when there is no microphone.</summary>
        public bool? ToggleMute()
        {
            try
            {
                lock (_gate)
                {
                    var mic = WatchDefault();
                    if (mic is null) return null;
                    bool muted = !mic.AudioEndpointVolume.Mute;
                    mic.AudioEndpointVolume.Mute = muted;
                    return muted;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not toggle the microphone");
                return null;
            }
        }

        /// <summary>Set the mute state. Returns false when there is no microphone.</summary>
        public bool SetMuted(bool muted)
        {
            try
            {
                lock (_gate)
                {
                    var mic = WatchDefault();
                    if (mic is null) return false;
                    mic.AudioEndpointVolume.Mute = muted;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not set the microphone mute state");
                return false;
            }
        }

        /// <summary>Keeps a notification subscription on the current default mic (re-attaches when it changes). Caller holds the lock.</summary>
        private MMDevice? WatchDefault()
        {
            using var en = new MMDeviceEnumerator();
            if (!en.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
            {
                Unwatch();
                return null;
            }

            var current = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            if (_watched != null && string.Equals(current.ID, _watchedId, StringComparison.OrdinalIgnoreCase))
            {
                current.Dispose();
                return _watched;
            }

            Unwatch();
            _watched = current;
            _watchedId = current.ID;
            _watched.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            return _watched;
        }

        private void Unwatch()
        {
            if (_watched is null) return;
            try { _watched.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            try { _watched.Dispose(); } catch { }
            _watched = null;
            _watchedId = null;
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            try { MuteChanged?.Invoke(data.Muted); }
            catch (Exception ex) { _logger?.LogDebug(ex, "A microphone listener threw"); }
        }

        public void Dispose()
        {
            lock (_gate) Unwatch();
        }
    }
}
