namespace CenterHubNew.MVVM.Models
{
    /// <summary>
    /// Which Voicemeeter "send" bus a source is routed to for Discord/monitoring.
    /// Banana exposes B1 and B2 as virtual output (recording) buses.
    /// </summary>
    public enum VoicemeeterBBus
    {
        B1 = 0,
        B2 = 1
    }

    /// <summary>
    /// User-configurable settings for the "Guitar + Discord" Voicemeeter mode.
    /// Persisted to %AppData%\CenterHub\voicemeeter.json alongside the last
    /// <see cref="AudioDeviceSnapshot"/>.
    ///
    /// Devices are stored by stable endpoint ID first, with the display name kept
    /// as a fallback for when an ID can no longer be resolved (device reconnected
    /// with a new instance id, driver reinstall, etc.).
    /// </summary>
    public sealed class VoicemeeterSettings
    {
        // ── Microphone (a physical Voicemeeter hardware input strip) ──
        public string? MicrophoneDeviceId { get; set; }
        public string? MicrophoneDeviceName { get; set; }

        // ── Guitar / Boss Katana (a physical Voicemeeter hardware input strip) ──
        public string? GuitarDeviceId { get; set; }
        public string? GuitarDeviceName { get; set; }

        // ── Monitor output (the A1 hardware output bus — headphones/speakers) ──
        public string? MonitorDeviceId { get; set; }
        public string? MonitorDeviceName { get; set; }

        /// <summary>
        /// True (default): Banana opens the headphones in shared mode (its MME driver), so other
        /// apps — a game or Teams pointed straight at the headset — can play to them too.
        /// False: exclusive mode (WDM) — the lowest monitoring delay, but only Banana can use them.
        /// </summary>
        public bool ShareMonitorDevice { get; set; } = true;

        // ── Gains (dB, matching Voicemeeter's Strip[].Gain range ~ -60..+12) ──
        public float MicGainDb { get; set; }
        public float GuitarGainDb { get; set; }

        // ── Local monitoring: also send the source to A1 so the user hears it ──
        public bool MonitorMicrophone { get; set; }
        public bool MonitorGuitar { get; set; }

        /// <summary>Which virtual bus carries voice to Discord (default B1).</summary>
        public VoicemeeterBBus PreferredSendBus { get; set; } = VoicemeeterBBus.B1;

        /// <summary>When true, a crash-interrupted session is restored on startup without prompting.</summary>
        public bool AutoRestoreOnStartup { get; set; }
    }
}
