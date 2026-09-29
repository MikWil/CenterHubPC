using System;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>
    /// A single Windows audio endpoint captured by stable endpoint ID (with the
    /// friendly name kept only as a human-readable fallback / for logging).
    /// </summary>
    public sealed class AudioEndpointRef
    {
        /// <summary>Stable MMDevice endpoint ID, e.g. "{0.0.0.00000000}.{guid}".</summary>
        public string? Id { get; set; }

        /// <summary>Friendly name at capture time — display / last-resort match only.</summary>
        public string? Name { get; set; }

        public bool HasValue => !string.IsNullOrEmpty(Id) || !string.IsNullOrEmpty(Name);

        public override string ToString() => Name ?? Id ?? "(none)";
    }

    /// <summary>
    /// Snapshot of the four Windows default audio endpoints taken immediately
    /// before CenterHub switches audio for Voicemeeter mode. Persisted so the
    /// exact previous configuration can be restored — including after a crash.
    /// </summary>
    public sealed class AudioDeviceSnapshot
    {
        /// <summary>Default multimedia playback (render, Multimedia/Console role).</summary>
        public AudioEndpointRef? DefaultPlayback { get; set; }

        /// <summary>Default communications playback (render, Communications role).</summary>
        public AudioEndpointRef? DefaultCommunicationsPlayback { get; set; }

        /// <summary>Default recording (capture, Multimedia/Console role).</summary>
        public AudioEndpointRef? DefaultRecording { get; set; }

        /// <summary>Default communications recording (capture, Communications role).</summary>
        public AudioEndpointRef? DefaultCommunicationsRecording { get; set; }

        /// <summary>When the snapshot was captured (UTC).</summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True while Voicemeeter mode is active. Persisted with the snapshot so a
        /// restart after a crash can detect an incomplete session and offer to restore.
        /// </summary>
        public bool SessionActive { get; set; }

        /// <summary>True if at least one endpoint was captured (something is restorable).</summary>
        public bool HasAnything() =>
            (DefaultPlayback?.HasValue ?? false) ||
            (DefaultCommunicationsPlayback?.HasValue ?? false) ||
            (DefaultRecording?.HasValue ?? false) ||
            (DefaultCommunicationsRecording?.HasValue ?? false);
    }
}
