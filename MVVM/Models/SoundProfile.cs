using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Models
{
    public partial class SoundProfile : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string? communicationDevice;

        [ObservableProperty]
        private string? audioDevice;

        [ObservableProperty]
        private float volume = 1.0f;

        /// <summary>
        /// When true, applying this profile also enables Voicemeeter "Guitar + Discord"
        /// mode; when false, applying it disables/restores an active Voicemeeter session.
        /// Absent in older saved profiles (deserializes to false — normal behavior).
        /// </summary>
        [ObservableProperty]
        private bool voicemeeterEnabled;

        /// <summary>Reserved for future named Voicemeeter presets; null = the single default preset.</summary>
        [ObservableProperty]
        private string? voicemeeterProfileId;

        public SoundProfile()
        {
        }

        public SoundProfile(string name, string? communicationDevice, string? audioDevice, float volume)
        {
            Name = name;
            CommunicationDevice = communicationDevice;
            AudioDevice = audioDevice;
            Volume = volume;
        }
    }
}

