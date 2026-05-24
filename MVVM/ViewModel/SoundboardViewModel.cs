using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class SoundboardViewModel : BaseViewModel
    {
        private readonly SoundboardService _soundboardService;

        [ObservableProperty]
        private ObservableCollection<SoundboardItem> _sounds = new();

        [ObservableProperty]
        private ObservableCollection<string> _outputDevices = new();

        [ObservableProperty]
        private string? _selectedOutputDevice;

        [ObservableProperty]
        private string? _selectedMonitorDevice;

        [ObservableProperty]
        private SoundboardItem? _selectedSound;

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private bool _monitorEnabled = true;

        [ObservableProperty]
        private float _monitorVolume = 0.5f;

        [ObservableProperty]
        private float _outputVolume = 1.0f;

        public SoundboardViewModel(
            SoundboardService soundboardService,
            ILogger<SoundboardViewModel>? logger = null) : base(logger)
        {
            _soundboardService = soundboardService;
            LoadSounds();
            LoadOutputDevices();

            Logger?.LogInformation("SoundboardViewModel initialized with {Count} sounds", Sounds.Count);
        }

        private void LoadSounds()
        {
            Sounds.Clear();
            foreach (var sound in _soundboardService.GetSounds())
            {
                Sounds.Add(sound);
            }
        }

        private void LoadOutputDevices()
        {
            OutputDevices.Clear();
            foreach (var device in _soundboardService.GetOutputDevices())
            {
                OutputDevices.Add(device);
            }
            
            // Restore saved settings or use defaults
            if (!string.IsNullOrEmpty(_soundboardService.SelectedOutputDevice) && 
                OutputDevices.Contains(_soundboardService.SelectedOutputDevice))
            {
                SelectedOutputDevice = _soundboardService.SelectedOutputDevice;
            }
            else if (OutputDevices.Count > 0)
            {
                SelectedOutputDevice = OutputDevices[0];
            }

            if (!string.IsNullOrEmpty(_soundboardService.SelectedMonitorDevice) && 
                OutputDevices.Contains(_soundboardService.SelectedMonitorDevice))
            {
                SelectedMonitorDevice = _soundboardService.SelectedMonitorDevice;
            }
            else if (OutputDevices.Count > 0)
            {
                SelectedMonitorDevice = OutputDevices[0];
            }

            // Restore volume settings
            MonitorEnabled = _soundboardService.MonitorEnabled;
            MonitorVolume = _soundboardService.MonitorVolume;
            OutputVolume = _soundboardService.OutputVolume;
        }

        partial void OnMonitorEnabledChanged(bool value)
        {
            _soundboardService.MonitorEnabled = value;
        }

        partial void OnMonitorVolumeChanged(float value)
        {
            _soundboardService.MonitorVolume = value;
        }

        partial void OnSelectedMonitorDeviceChanged(string? value)
        {
            if (value != null)
            {
                _soundboardService.MonitorDeviceIndex = _soundboardService.GetDeviceIndex(value);
                _soundboardService.SelectedMonitorDevice = value;
            }
        }

        partial void OnSelectedOutputDeviceChanged(string? value)
        {
            if (value != null)
            {
                _soundboardService.SelectedOutputDevice = value;
            }
        }

        partial void OnOutputVolumeChanged(float value)
        {
            _soundboardService.OutputVolume = value;
        }

        [RelayCommand]
        private void PlaySound(SoundboardItem? sound)
        {
            if (sound == null) return;

            var deviceIndex = SelectedOutputDevice != null 
                ? _soundboardService.GetDeviceIndex(SelectedOutputDevice) 
                : 0;

            _soundboardService.PlaySound(sound, deviceIndex);
            IsPlaying = true;
        }

        [RelayCommand]
        private void StopSound()
        {
            _soundboardService.Stop();
            IsPlaying = false;
        }

        [RelayCommand]
        private void AddSound()
        {
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Audio Files (*.mp3;*.wav;*.ogg;*.flac)|*.mp3;*.wav;*.ogg;*.flac|All Files (*.*)|*.*",
                Title = "Select Sound File",
                Multiselect = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                foreach (var filePath in dialog.FileNames)
                {
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    var sound = _soundboardService.AddSound(name, filePath);
                    Sounds.Add(sound);
                }
            }
        }

        [RelayCommand]
        private void RemoveSound(SoundboardItem? sound)
        {
            if (sound == null) return;

            var result = System.Windows.Forms.MessageBox.Show(
                $"Remove sound '{sound.Name}'?",
                "Confirm Remove",
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                _soundboardService.RemoveSound(sound.Id);
                Sounds.Remove(sound);
            }
        }

        [RelayCommand]
        private void EditSound(SoundboardItem? sound)
        {
            if (sound == null) return;
            SelectedSound = sound;
        }

        [RelayCommand]
        private void SaveSoundEdit()
        {
            if (SelectedSound == null) return;
            _soundboardService.UpdateSound(SelectedSound);
            SelectedSound = null;
        }

        [RelayCommand]
        private void CancelSoundEdit()
        {
            SelectedSound = null;
            LoadSounds(); // Reload to discard changes
        }

        [RelayCommand]
        private void BrowseSoundFile()
        {
            if (SelectedSound == null) return;

            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Audio Files (*.mp3;*.wav;*.ogg;*.flac)|*.mp3;*.wav;*.ogg;*.flac|All Files (*.*)|*.*",
                Title = "Select Sound File"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedSound.FilePath = dialog.FileName;
            }
        }

        [RelayCommand]
        private void RefreshDevices()
        {
            LoadOutputDevices();
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _soundboardService.Stop();
                Logger?.LogInformation("SoundboardViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

