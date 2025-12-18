using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic; // Added missing import

namespace CenterHubNew.MVVM.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigurationService>? _logger;
        private readonly string _configFilePath;
        private readonly Dictionary<string, object> _settings;

        public ConfigurationService(ILogger<ConfigurationService>? logger = null)
        {
            _logger = logger;
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            _settings = new Dictionary<string, object>();

            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            _configuration = builder.Build();
            Load();
        }

        public T? GetValue<T>(string key, T? defaultValue = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Configuration key is null or empty");
                return defaultValue;
            }

            try
            {
                // First try to get from in-memory settings
                if (_settings.TryGetValue(key, out var value))
                {
                    if (value is T typedValue)
                    {
                        _logger?.LogDebug("Configuration value retrieved from memory: {Key} = {Value}", key, typedValue);
                        return typedValue;
                    }
                }

                // Fallback to configuration file
                var configValue = _configuration.GetValue<T>(key);
                if (configValue != null)
                {
                    _logger?.LogDebug("Configuration value retrieved from file: {Key} = {Value}", key, configValue);
                    return configValue;
                }

                _logger?.LogDebug("Configuration key not found, returning default: {Key}", key);
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving configuration value for key: {Key}", key);
                return defaultValue;
            }
        }

        public void SetValue<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Cannot set configuration with null or empty key");
                return;
            }

            try
            {
                _settings[key] = value!;
                _logger?.LogDebug("Configuration value set: {Key} = {Value}", key, value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting configuration value for key: {Key}", key);
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                _logger?.LogInformation("Configuration saved to file: {FilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving configuration to file: {FilePath}", _configFilePath);
            }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var loadedSettings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (loadedSettings != null)
                    {
                        _settings.Clear();
                        foreach (var setting in loadedSettings)
                        {
                            _settings[setting.Key] = setting.Value;
                        }
                        _logger?.LogInformation("Configuration loaded from file: {FilePath}", _configFilePath);
                    }
                }
                else
                {
                    _logger?.LogInformation("Configuration file not found, using defaults: {FilePath}", _configFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading configuration from file: {FilePath}", _configFilePath);
            }
        }
    }
}
