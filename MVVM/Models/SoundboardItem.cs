using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace CenterHubNew.MVVM.Models
{
    public partial class SoundboardItem : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "New Sound";

        [ObservableProperty]
        private string _filePath = string.Empty;

        [ObservableProperty]
        private float _volume = 1.0f;

        [ObservableProperty]
        private string? _hotkey;

        [ObservableProperty]
        private string _color = "#7DD3FC"; // Default accent color

        [ObservableProperty]
        private bool _isDefault = false;

        /// <summary>Playback start offset in seconds (trim from the front). 0 = beginning.</summary>
        [ObservableProperty]
        private decimal _startSeconds = 0m;

        /// <summary>How long to play, in seconds. 0 = play to the end of the clip.</summary>
        [ObservableProperty]
        private decimal _lengthSeconds = 0m;

        public SoundboardItem() { }

        public SoundboardItem(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }
    }
}

