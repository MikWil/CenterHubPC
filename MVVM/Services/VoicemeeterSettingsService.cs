using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>The full persisted Voicemeeter state: user settings + last audio snapshot + routing presets.</summary>
    public sealed class VoicemeeterState
    {
        public VoicemeeterSettings Settings { get; set; } = new();

        /// <summary>The audio snapshot from the most recent Enable. Its SessionActive flag drives crash recovery.</summary>
        public AudioDeviceSnapshot? Snapshot { get; set; }

        /// <summary>Saved flow-board routing presets.</summary>
        public System.Collections.Generic.List<AudioRoutingPreset> Presets { get; set; } = new();

        /// <summary>Id of the preset currently applied (for restoring board selection).</summary>
        public string? ActivePresetId { get; set; }
    }

    /// <summary>
    /// Persists Voicemeeter settings and the pre-switch audio snapshot to
    /// %AppData%\CenterHub\voicemeeter.json (same convention as sound-profiles.json).
    /// </summary>
    public sealed class VoicemeeterSettingsService
    {
        private readonly ILogger<VoicemeeterSettingsService>? _logger;
        private readonly string _filePath;
        private readonly object _gate = new();

        public VoicemeeterSettingsService(ILogger<VoicemeeterSettingsService>? logger = null)
            : this(logger, storageFolder: null) { }

        /// <param name="storageFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        public VoicemeeterSettingsService(ILogger<VoicemeeterSettingsService>? logger, string? storageFolder)
        {
            _logger = logger;
            var folder = storageFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "voicemeeter.json");
        }

        public VoicemeeterState Load()
        {
            lock (_gate)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        var json = File.ReadAllText(_filePath);
                        var state = JsonConvert.DeserializeObject<VoicemeeterState>(json);
                        if (state != null)
                        {
                            state.Settings ??= new VoicemeeterSettings();
                            return state;
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    _logger?.LogError(ex, "voicemeeter.json is corrupt; quarantining file");
                    AtomicFile.QuarantineCorrupt(_filePath);
                }
                catch (Exception ex) { _logger?.LogError(ex, "Error loading voicemeeter.json"); }

                return new VoicemeeterState();
            }
        }

        public void Save(VoicemeeterState state)
        {
            lock (_gate)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                    AtomicFile.WriteAllText(_filePath, json);
                }
                catch (Exception ex) { _logger?.LogError(ex, "Error saving voicemeeter.json"); }
            }
        }

        public void SaveSettings(VoicemeeterSettings settings)
        {
            var state = Load();
            state.Settings = settings;
            Save(state);
        }

        public void SaveSnapshot(AudioDeviceSnapshot? snapshot)
        {
            var state = Load();
            state.Snapshot = snapshot;
            Save(state);
        }

        /// <summary>Mark whether a Voicemeeter session is currently active (for crash recovery).</summary>
        public void SetSessionActive(bool active)
        {
            var state = Load();
            if (state.Snapshot != null)
            {
                state.Snapshot.SessionActive = active;
                Save(state);
            }
        }

        public void SavePresets(System.Collections.Generic.List<AudioRoutingPreset> presets, string? activeId)
        {
            var state = Load();
            state.Presets = presets;
            state.ActivePresetId = activeId;
            Save(state);
        }

        public void SetActivePreset(string? id)
        {
            var state = Load();
            state.ActivePresetId = id;
            Save(state);
        }
    }
}
