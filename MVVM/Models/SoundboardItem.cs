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

        public SoundboardItem() { }

        public SoundboardItem(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }
    }
}

