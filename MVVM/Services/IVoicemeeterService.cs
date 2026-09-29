using System.Threading;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.Services
{
    public enum VoicemeeterStatus
    {
        NotInstalled,
        Stopped,
        Running
    }

    /// <summary>Voicemeeter edition, as reported by the engine once running.</summary>
    public enum VoicemeeterKind
    {
        Unknown = 0,
        Standard = 1,
        Banana = 2,
        Potato = 3
    }

    /// <summary>Routing/send buses on a Voicemeeter Banana strip.</summary>
    public enum VoicemeeterBus { A1, A2, A3, B1, B2 }

    /// <summary>
    /// High-level access to Voicemeeter Banana via the Remote API. All raw
    /// parameter strings ("Strip[0].B1", "Bus[0].device.wdm", …) are hidden behind
    /// the strongly-typed methods here. Safe to use when Voicemeeter is not
    /// installed — every call is a no-op returning false and <see cref="Status"/>
    /// reports <see cref="VoicemeeterStatus.NotInstalled"/>.
    /// </summary>
    public interface IVoicemeeterService : System.IDisposable
    {
        // ── Banana channel map (indices used by the routing methods) ──
        int MicStripIndex { get; }       // physical strip 0 — microphone
        int GuitarStripIndex { get; }    // physical strip 1 — guitar
        int LineInStripIndex { get; }    // physical strip 2 — spare line-in
        int VaioStripIndex { get; }      // virtual strip 3 — "Voicemeeter Input" (desktop audio)
        int AuxStripIndex { get; }       // virtual strip 4 — "Voicemeeter Aux Input"
        int Vaio3StripIndex { get; }     // virtual strip 5 — Potato only
        int MonitorBusIndex { get; }     // A1 hardware output bus (headphones/speakers)

        bool IsInstalled { get; }
        VoicemeeterStatus Status { get; }
        VoicemeeterKind Kind { get; }
        bool IsConnected { get; }

        /// <summary>Re-detect install / running state (and edition when running).</summary>
        VoicemeeterStatus RefreshStatus();

        /// <summary>Start Banana if needed, connect the Remote API, and wait until the engine is ready.</summary>
        Task<bool> EnsureRunningAsync(CancellationToken ct = default);

        /// <summary>Log in to a Voicemeeter that is already running. False if not installed/running.</summary>
        bool Connect();

        void Disconnect();

        /// <summary>Drop and re-establish the Remote API connection (recovers a stale session).</summary>
        void Reconnect();

        /// <summary>Restart the Voicemeeter audio engine (clears glitches after idle/device changes).</summary>
        bool RestartAudioEngine();

        /// <summary>Bring up the Voicemeeter Banana window.</summary>
        void OpenUi();

        // ── strongly-typed parameters ──
        bool SetHardwareInput(int stripIndex, string deviceName);
        bool SetMonitorDevice(string deviceName);
        bool SetStripGain(int stripIndex, float gainDb);
        bool SetStripMute(int stripIndex, bool mute);
        bool SetRoute(int stripIndex, VoicemeeterBus bus, bool on);
        bool SetRouteToB1(int stripIndex, bool on);

        /// <summary>Toggle whether a strip is also sent to the A1 monitor bus.</summary>
        bool SetMonitorRouting(int stripIndex, bool toA1);
    }
}
