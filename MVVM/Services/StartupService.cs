using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// "Start with Windows": a per-user Run-key value (HKCU\Software\Microsoft\Windows\CurrentVersion\Run,
    /// value "CenterHub") pointing at the running exe. No admin rights needed. DI singleton.
    /// </summary>
    public sealed class StartupService
    {
        /// <summary>Name of the Run-key value.</summary>
        public const string ValueName = "CenterHub";

        /// <summary>The real Run key (relative to HKEY_CURRENT_USER).</summary>
        public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly ILogger<StartupService>? _logger;
        private readonly string _keyPath;
        private readonly string? _exePath;

        public StartupService(ILogger<StartupService>? logger = null) : this(logger, null, null) { }

        /// <param name="registryKeyPath">Override for tests (a key under HKCU, e.g. Software\CenterHubTests\Run-…); defaults to the real Run key.</param>
        /// <param name="exePath">Override for tests; defaults to <see cref="Environment.ProcessPath"/>.</param>
        public StartupService(ILogger<StartupService>? logger, string? registryKeyPath, string? exePath)
        {
            _logger = logger;
            _keyPath = string.IsNullOrWhiteSpace(registryKeyPath) ? DefaultKeyPath : registryKeyPath;
            _exePath = string.IsNullOrWhiteSpace(exePath) ? Environment.ProcessPath : exePath;
        }

        /// <summary>
        /// True only when the value exists and points at the current exe. A value that points elsewhere
        /// (an old install location) counts as off, and enabling overwrites it.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(_exePath)) return false;

                    using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
                    if (key?.GetValue(ValueName) is not string data) return false;

                    var target = ExtractPath(data);
                    if (string.IsNullOrEmpty(target)) return false;

                    return string.Equals(
                        Path.GetFullPath(target), Path.GetFullPath(_exePath), StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not read the startup entry");
                    return false;
                }
            }
        }

        /// <summary>Add (or overwrite) / remove the startup entry. Returns false when the registry could not be written.</summary>
        public bool SetEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (string.IsNullOrEmpty(_exePath)) return false;
                    using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
                    if (key is null) return false;
                    key.SetValue(ValueName, "\"" + _exePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
                    key?.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not {Action} the startup entry", enabled ? "write" : "remove");
                return false;
            }
        }

        /// <summary>The exe path inside a Run value: the quoted part when quoted, otherwise the whole text.</summary>
        internal static string? ExtractPath(string data)
        {
            var text = data.Trim();
            if (text.Length == 0) return null;
            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : null;
            }
            return text;
        }
    }
}
