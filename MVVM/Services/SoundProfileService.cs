using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CenterHubNew.MVVM.Services
{
    public class SoundProfileService
    {
        private readonly ILogger<SoundProfileService>? _logger;
        private readonly string _profilesFilePath;
        private const int MaxProfiles = 3;

        public SoundProfileService(ILogger<SoundProfileService>? logger = null)
        {
            _logger = logger;
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "CenterHub");
            Directory.CreateDirectory(appFolder);
            _profilesFilePath = Path.Combine(appFolder, "sound-profiles.json");
            _logger?.LogInformation("SoundProfileService initialized. Profiles file: {FilePath}", _profilesFilePath);
        }

        public List<SoundProfile> LoadProfiles()
        {
            try
            {
                if (File.Exists(_profilesFilePath))
                {
                    var json = File.ReadAllText(_profilesFilePath);
                    var profiles = JsonConvert.DeserializeObject<List<SoundProfile>>(json);
                    if (profiles != null && profiles.Count > 0)
                    {
                        // Ensure we only return up to MaxProfiles
                        var result = profiles.Take(MaxProfiles).ToList();
                        // Ensure we have exactly MaxProfiles
                        while (result.Count < MaxProfiles)
                        {
                            result.Add(new SoundProfile($"Profile {result.Count + 1}", null, null, 1.0f));
                        }
                        _logger?.LogInformation("Loaded {Count} sound profiles from file", result.Count);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading sound profiles from file");
            }

            // Return default profiles if file doesn't exist or error occurred
            var defaultProfiles = new List<SoundProfile>();
            for (int i = 1; i <= MaxProfiles; i++)
            {
                defaultProfiles.Add(new SoundProfile($"Profile {i}", null, null, 1.0f));
            }
            _logger?.LogInformation("Created {Count} default sound profiles", defaultProfiles.Count);
            return defaultProfiles;
        }

        public void SaveProfiles(List<SoundProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger?.LogWarning("Cannot save empty profiles list");
                    return;
                }

                // Ensure we only save up to MaxProfiles
                var profilesToSave = profiles.Take(MaxProfiles).ToList();
                var json = JsonConvert.SerializeObject(profilesToSave, Formatting.Indented);
                File.WriteAllText(_profilesFilePath, json);
                _logger?.LogInformation("Saved {Count} sound profiles to file", profilesToSave.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving sound profiles to file");
                throw;
            }
        }

        public void SaveProfile(int index, SoundProfile profile)
        {
            if (index < 0 || index >= MaxProfiles)
            {
                _logger?.LogWarning("Invalid profile index: {Index}. Must be between 0 and {Max}", index, MaxProfiles - 1);
                return;
            }

            var profiles = LoadProfiles();
            if (profiles.Count <= index)
            {
                while (profiles.Count <= index)
                {
                    profiles.Add(new SoundProfile($"Profile {profiles.Count + 1}", null, null, 1.0f));
                }
            }

            profiles[index] = profile;
            SaveProfiles(profiles);
        }
    }
}

