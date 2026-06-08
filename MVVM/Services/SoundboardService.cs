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
            PruneMissingDefaults();
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

                _waveOut.Init(ApplyTrim(_audioReader, sound));
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

                    _monitorWaveOut.Init(ApplyTrim(_monitorAudioReader, sound));
                    _monitorWaveOut.Play();
                }

                _logger?.LogDebug("Playing sound: {Name} on device {Device}", sound.Name, deviceNumber);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to play sound: {Name}", sound.Name);
            }
        }

        /// <summary>
        /// Wrap a reader so only the user-chosen slice plays: skip <c>StartSeconds</c>
        /// from the front and take <c>LengthSeconds</c> (0 = to the end). Lets users
        /// trim a clip in-app instead of editing the file externally.
        /// </summary>
        private static ISampleProvider ApplyTrim(AudioFileReader reader, SoundboardItem sound)
        {
            ISampleProvider provider = reader;
            if (sound.StartSeconds <= 0 && sound.LengthSeconds <= 0)
                return provider;

            var offset = new OffsetSampleProvider(provider);
            if (sound.StartSeconds > 0)
                offset.SkipOver = TimeSpan.FromSeconds((double)sound.StartSeconds);
            if (sound.LengthSeconds > 0)
                offset.Take = TimeSpan.FromSeconds((double)sound.LengthSeconds);
            return offset;
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
                existing.StartSeconds = sound.StartSeconds;
                existing.LengthSeconds = sound.LengthSeconds;
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

        /// <summary>
        /// The soundboard ships with no bundled audio. Earlier versions seeded a set
        /// of built-in entries pointing at files that were never installed, leaving
        /// dead buttons. Remove any such managed-default entry whose file is missing so
        /// users start clean and add their own clips.
        /// </summary>
        private void PruneMissingDefaults()
        {
            int removed = _sounds.RemoveAll(s =>
                s.IsDefault && (string.IsNullOrEmpty(s.FilePath) || !File.Exists(s.FilePath)));

            if (removed > 0)
            {
                _logger?.LogInformation("Removed {Count} built-in soundboard entries with no audio file", removed);
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

