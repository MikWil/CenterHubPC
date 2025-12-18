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

