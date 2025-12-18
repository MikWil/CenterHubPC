using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CenterHubNew.MVVM.Services
{
    public class SoundboardService : IDisposable
    {
        private readonly string _configFilePath;
        private readonly string _settingsFilePath;
        private readonly string _defaultSoundsFolder;
        private readonly ILogger<SoundboardService>? _logger;
        private List<SoundboardItem> _sounds = new();
        private SoundboardSettings _settings = new();
        private WaveOutEvent? _waveOut;
        private WaveOutEvent? _monitorWaveOut;
        private AudioFileReader? _audioReader;
        private AudioFileReader? _monitorAudioReader;
        private bool _disposed;
        
        public bool MonitorEnabled 
        { 
            get => _settings.MonitorEnabled; 
            set { _settings.MonitorEnabled = value; SaveSettings(); }
        }
        public float MonitorVolume 
        { 
            get => _settings.MonitorVolume; 
            set { _settings.MonitorVolume = value; SaveSettings(); }
        }
        public float OutputVolume 
        { 
            get => _settings.OutputVolume; 
            set { _settings.OutputVolume = value; SaveSettings(); }
        }
        public int MonitorDeviceIndex { get; set; } = 0;
        
        public string? SelectedOutputDevice 
        { 
            get => _settings.SelectedOutputDevice; 
            set { _settings.SelectedOutputDevice = value; SaveSettings(); }
        }
        public string? SelectedMonitorDevice 
        { 
            get => _settings.SelectedMonitorDevice; 
            set { _settings.SelectedMonitorDevice = value; SaveSettings(); }
        }

        public SoundboardService(ILogger<SoundboardService>? logger = null)
        {
            _logger = logger;
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            _configFilePath = Path.Combine(appFolder, "soundboard.json");
            _settingsFilePath = Path.Combine(appFolder, "soundboard-settings.json");
            _defaultSoundsFolder = Path.Combine(appFolder, "sounds");
            if (!Directory.Exists(_defaultSoundsFolder))
            {
                Directory.CreateDirectory(_defaultSoundsFolder);
            }
            LoadSettings();
            LoadSounds();
            EnsureDefaultSounds();
        }

        public List<SoundboardItem> GetSounds() => _sounds.ToList();

        public List<string> GetOutputDevices()
        {
            var devices = new List<string>();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                devices.Add(caps.ProductName);
            }
            return devices;
        }

        public int GetDeviceIndex(string deviceName)
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                if (caps.ProductName == deviceName)
                    return i;
            }
            return 0; // Default device
        }

        public void PlaySound(SoundboardItem sound, int deviceNumber = 0)
        {
            if (string.IsNullOrEmpty(sound.FilePath) || !File.Exists(sound.FilePath))
            {
                _logger?.LogWarning("Sound file not found: {Path}", sound.FilePath);
                return;
            }

            try
            {
                Stop();

                // Main output (for Discord/Voicemeeter)
                _audioReader = new AudioFileReader(sound.FilePath)
                {
                    Volume = sound.Volume * OutputVolume
                };

                _waveOut = new WaveOutEvent
                {
                    DeviceNumber = deviceNumber
                };

                _waveOut.Init(_audioReader);
                _waveOut.Play();

                // Monitor output (for you to hear)
                if (MonitorEnabled && MonitorDeviceIndex != deviceNumber)
                {
                    _monitorAudioReader = new AudioFileReader(sound.FilePath)
                    {
                        Volume = sound.Volume * MonitorVolume
                    };

                    _monitorWaveOut = new WaveOutEvent
                    {
                        DeviceNumber = MonitorDeviceIndex
                    };

                    _monitorWaveOut.Init(_monitorAudioReader);
                    _monitorWaveOut.Play();
                }

                _logger?.LogDebug("Playing sound: {Name} on device {Device}", sound.Name, deviceNumber);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to play sound: {Name}", sound.Name);
            }
        }

        public void Stop()
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;

                _audioReader?.Dispose();
                _audioReader = null;

                _monitorWaveOut?.Stop();
                _monitorWaveOut?.Dispose();
                _monitorWaveOut = null;

                _monitorAudioReader?.Dispose();
                _monitorAudioReader = null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping sound");
            }
        }

        public SoundboardItem AddSound(string name, string filePath)
        {
            var sound = new SoundboardItem(name, filePath);
            _sounds.Add(sound);
            SaveSounds();
            _logger?.LogInformation("Added sound: {Name}", name);
            return sound;
        }

        public void UpdateSound(SoundboardItem sound)
        {
            var existing = _sounds.FirstOrDefault(s => s.Id == sound.Id);
            if (existing != null)
            {
                existing.Name = sound.Name;
                existing.FilePath = sound.FilePath;
                existing.Volume = sound.Volume;
                existing.Hotkey = sound.Hotkey;
                existing.Color = sound.Color;
                SaveSounds();
            }
        }

        public void RemoveSound(string soundId)
        {
            var sound = _sounds.FirstOrDefault(s => s.Id == soundId);
            if (sound != null)
            {
                _sounds.Remove(sound);
                SaveSounds();
                _logger?.LogInformation("Removed sound: {Name}", sound.Name);
            }
        }

        public void SaveSounds()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_sounds, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save soundboard");
            }
        }

        private void LoadSounds()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    _sounds = JsonConvert.DeserializeObject<List<SoundboardItem>>(json) ?? new List<SoundboardItem>();
                    _logger?.LogInformation("Loaded {Count} sounds", _sounds.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load soundboard");
                _sounds = new List<SoundboardItem>();
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonConvert.DeserializeObject<SoundboardSettings>(json) ?? new SoundboardSettings();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load soundboard settings");
                _settings = new SoundboardSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save soundboard settings");
            }
        }

        private class SoundboardSettings
        {
            public string? SelectedOutputDevice { get; set; }
            public string? SelectedMonitorDevice { get; set; }
            public bool MonitorEnabled { get; set; } = true;
            public float MonitorVolume { get; set; } = 0.5f;
            public float OutputVolume { get; set; } = 1.0f;
        }

        private void EnsureDefaultSounds()
        {
            // Default sounds - always present
            var defaultSounds = new[]
            {
                ("Airhorn", "airhorn.mp3"),
                ("Sad Trombone", "sad-trombone.mp3"),
                ("Rimshot", "rimshot.mp3"),
                ("Applause", "applause.mp3"),
                ("Crickets", "crickets.mp3"),
                ("Dramatic", "dramatic.mp3"),
                ("Fart", "fart.mp3"),
                ("Laugh Track", "laugh.mp3")
            };

            bool changed = false;
            foreach (var (name, fileName) in defaultSounds)
            {
                var filePath = Path.Combine(_defaultSoundsFolder, fileName);
                // Check if this default sound already exists
                if (!_sounds.Any(s => s.Name == name && s.FilePath == filePath))
                {
                    var sound = new SoundboardItem(name, filePath) { IsDefault = true };
                    _sounds.Insert(0, sound); // Add defaults at the beginning
                    changed = true;
                }
            }
            
            if (changed)
            {
                SaveSounds();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}

