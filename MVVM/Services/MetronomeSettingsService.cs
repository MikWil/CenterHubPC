using System;
using System.Collections.Generic;
using System.IO;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Persists the Metronome page settings to %AppData%\CenterHub\metronome.json
    /// (same convention as voicemeeter.json). Enums are stored as strings so the file
    /// stays readable and survives enum reordering.
    /// </summary>
    public sealed class MetronomeSettingsService
    {
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Converters = { new TolerantStringEnumConverter() },
        };

        private readonly ILogger<MetronomeSettingsService>? _logger;
        private readonly string _filePath;
        private readonly object _gate = new();

        public MetronomeSettingsService(ILogger<MetronomeSettingsService>? logger = null)
            : this(logger, storageFolder: null) { }

        /// <param name="storageFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        public MetronomeSettingsService(ILogger<MetronomeSettingsService>? logger, string? storageFolder)
        {
            _logger = logger;
            var folder = storageFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "metronome.json");
        }

        /// <summary>Loads the settings; never throws. Returns defaults when missing or unreadable.</summary>
        public MetronomeSettings Load()
        {
            lock (_gate)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        var json = File.ReadAllText(_filePath);
                        var settings = JsonConvert.DeserializeObject<MetronomeSettings>(json, JsonSettings);
                        if (settings != null)
                        {
                            Sanitize(settings);
                            return settings;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "metronome.json is corrupt; quarantining file");
                    AtomicFile.QuarantineCorrupt(_filePath);
                }
                // Not quarantined: a transient lock must not hide the user's file.
                catch (Exception ex) { _logger?.LogError(ex, "Error loading metronome.json"); }

                return new MetronomeSettings();
            }
        }

        /// <summary>Saves the settings atomically; never throws.</summary>
        public void Save(MetronomeSettings settings)
        {
            lock (_gate)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(settings, Formatting.Indented, JsonSettings);
                    AtomicFile.WriteAllText(_filePath, json);
                }
                catch (Exception ex) { _logger?.LogError(ex, "Error saving metronome.json"); }
            }
        }

        // A hand-edited or older file may hold out-of-range values; keep the page safe.
        private static void Sanitize(MetronomeSettings s)
        {
            s.Bpm = Math.Clamp(s.Bpm, 30, 280);
            s.Volume = double.IsNaN(s.Volume) ? 0.75 : Math.Clamp(s.Volume, 0.0, 1.0);
            s.BeatsPerMeasure = Math.Clamp(s.BeatsPerMeasure, 1, 12);
            s.AutoFillBars = Math.Clamp(s.AutoFillBars, 0, 32);
            s.TrainerStepBpm = Math.Clamp(s.TrainerStepBpm, 1, 50);
            s.TrainerEveryBars = Math.Clamp(s.TrainerEveryBars, 1, 64);
            s.TrainerTargetBpm = Math.Clamp(s.TrainerTargetBpm, 30, 280);
            s.GapPlayBars = Math.Clamp(s.GapPlayBars, 1, 16);
            s.GapMuteBars = Math.Clamp(s.GapMuteBars, 1, 16);
            s.Accents ??= new List<BeatAccent>();

            for (int i = 0; i < s.Accents.Count; i++)
                if (!Enum.IsDefined(s.Accents[i])) s.Accents[i] = BeatAccent.Normal;

            if (!Enum.IsDefined(s.ClickSound)) s.ClickSound = MetronomeSound.Clock;
            if (!Enum.IsDefined(s.Subdivision)) s.Subdivision = ClickSubdivision.None;
            if (!Enum.IsDefined(s.Kit)) s.Kit = DrumKitKind.Rock;

            SanitizeSetlists(s);
        }

        private static void SanitizeSetlists(MetronomeSettings s)
        {
            s.Setlists ??= new List<Setlist>();
            s.Setlists.RemoveAll(l => l is null);

            var seen = new HashSet<string>();
            foreach (var list in s.Setlists)
            {
                if (string.IsNullOrWhiteSpace(list.Id) || !seen.Add(list.Id))
                {
                    list.Id = Guid.NewGuid().ToString("N");
                    seen.Add(list.Id);
                }
                list.Name ??= "";
                list.Songs ??= new List<SetlistSong>();
                list.Songs.RemoveAll(song => song is null);
                foreach (var song in list.Songs) SetlistHelper.Sanitize(song);
            }

            var active = s.ActiveSetlistId is null ? null : s.Setlists.Find(l => l.Id == s.ActiveSetlistId);
            if (active is null)
            {
                s.ActiveSetlistId = null;
                s.ActiveSongIndex = 0;
            }
            else
            {
                s.ActiveSongIndex = Math.Clamp(s.ActiveSongIndex, 0, Math.Max(0, active.Songs.Count - 1));
            }
        }
    }
}
