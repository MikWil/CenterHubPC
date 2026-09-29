using System.Collections.Generic;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>
    /// A routable audio source in the flow board. Each maps to one Voicemeeter
    /// Banana strip. Hardware sources (mic/guitar/line-in) are physical strips;
    /// virtual "app" sources are the Voicemeeter virtual inputs that Windows apps
    /// can be pointed at (VAIO, AUX, and VAIO3 on Potato).
    /// </summary>
    public enum AudioSourceKind
    {
        Microphone,   // physical strip 0
        Guitar,       // physical strip 1
        LineIn,       // physical strip 2 (optional/spare)
        AppVaio,      // virtual strip 3 — "Voicemeeter Input" (Windows default → desktop + Discord voices)
        AppAux,       // virtual strip 4 — "Voicemeeter Aux Input" (one app peeled off)
        AppVaio3,     // virtual strip 5 — Potato only
        AppCable      // physical strip 2 fed by VB-Audio Virtual Cable — extra app slot on Banana
    }

    /// <summary>
    /// Static description of a source: its strip index, the Windows playback device
    /// an app must target to feed it (for the "Set in Windows" deep-link), and
    /// whether it is a virtual app input vs. a hardware input.
    /// </summary>
    public sealed class AudioSource
    {
        public AudioSourceKind Kind { get; init; }
        public int StripIndex { get; init; }
        public string DefaultName { get; init; } = "";
        public bool IsVirtualApp { get; init; }

        /// <summary>
        /// False for the Desktop/Discord sink: routing it to "Others" (B1) would send
        /// Discord's incoming voices straight back to Discord — an echo/feedback loop.
        /// </summary>
        public bool CanSendToOthers { get; init; } = true;

        /// <summary>Windows playback device name an app targets to feed this input (virtual sources only).</summary>
        public string? WindowsPlaybackName { get; init; }

        public string Icon { get; init; } = "";
    }

    /// <summary>Per-source routing state within a preset.</summary>
    public sealed class AudioSourceRoute
    {
        public AudioSourceKind Kind { get; set; }

        /// <summary>User-facing label (editable), e.g. "Spotify" for the AUX slot.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Route to A1 — what you hear on your monitor/headphones.</summary>
        public bool ToYou { get; set; }

        /// <summary>Route to B1 — what others hear (the Discord send). Never set for the VAIO desktop slot (feedback guard).</summary>
        public bool ToOthers { get; set; }

        public float GainDb { get; set; }
        public bool Muted { get; set; }
    }

    /// <summary>A saved routing configuration for the flow board.</summary>
    public sealed class AudioRoutingPreset
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Preset";
        public string Icon { get; set; } = "ti-adjustments";
        public List<AudioSourceRoute> Routes { get; set; } = new();
    }
}
