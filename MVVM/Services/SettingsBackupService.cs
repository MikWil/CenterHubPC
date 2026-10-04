using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>Outcome of <see cref="SettingsBackupService.Restore"/>.</summary>
    /// <param name="Success">False when the backup was refused or failed.</param>
    /// <param name="Message">What happened, in words fit for a toast.</param>
    /// <param name="FilesRestored">Number of settings files written (0 when refused).</param>
    /// <param name="SafetyBackupPath">The "before restore" zip holding the previous settings, when one was made.</param>
    public sealed record RestoreResult(bool Success, string Message, int FilesRestored, string? SafetyBackupPath = null);

    /// <summary>
    /// Backs up and restores the app's settings: every <c>*.json</c> directly in %AppData%\CenterHub
    /// (not subfolders, not other file types) in one zip. A restore is all-or-nothing validated first,
    /// saves the current files to <c>backups\before-restore-*.zip</c>, then writes atomically.
    /// DI singleton.
    /// </summary>
    public sealed class SettingsBackupService
    {
        /// <summary>Small metadata entry added to every backup (app version, date). Never restored.</summary>
        public const string InfoEntryName = "backup-info.json";

        private const long MaxEntryBytes = 64L * 1024 * 1024;

        private readonly ILogger<SettingsBackupService>? _logger;

        public SettingsBackupService(ILogger<SettingsBackupService>? logger = null) : this(logger, null) { }

        /// <param name="dataFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        public SettingsBackupService(ILogger<SettingsBackupService>? logger, string? dataFolder)
        {
            _logger = logger;
            DataFolder = dataFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
        }

        /// <summary>The folder holding the settings files.</summary>
        public string DataFolder { get; }

        /// <summary>
        /// Zip every top-level <c>*.json</c> in the data folder, plus <see cref="InfoEntryName"/>, into
        /// <paramref name="zipPath"/> (replaced if it exists). Returns the number of settings files saved.
        /// Throws on IO errors; the target is only replaced once the new zip is complete.
        /// </summary>
        public int CreateBackup(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("No backup path given.", nameof(zipPath));

            var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var files = ListSettingsFiles();
            var tmp = zipPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                        using var target = entry.Open();
                        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        source.CopyTo(target);
                    }

                    var info = new JObject
                    {
                        ["app"] = "CenterHub",
                        ["version"] = AppVersion(),
                        ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["files"] = new JArray(files.Select(f => (object)Path.GetFileName(f)).ToArray()),
                    };
                    var infoEntry = zip.CreateEntry(InfoEntryName, CompressionLevel.Optimal);
                    using var infoStream = infoEntry.Open();
                    using var writer = new StreamWriter(infoStream, new UTF8Encoding(false));
                    writer.Write(info.ToString(Formatting.Indented));
                }
                File.Move(tmp, zipPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            _logger?.LogInformation("Backed up {Count} settings files to {Path}", files.Count, zipPath);
            return files.Count;
        }

        /// <summary>
        /// Restore the settings in <paramref name="zipPath"/>. Refuses (changing nothing) unless every entry
        /// is a plain <c>*.json</c> file name holding valid JSON. Saves the current files first. Files that
        /// are not in the backup are never touched.
        /// </summary>
        public RestoreResult Restore(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                return Refuse("That backup file doesn't exist.");

            var pending = new List<(string Name, string Text)>();
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                if (zip.Entries.Count == 0)
                    return Refuse("The backup is empty.");

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName;
                    if (!IsPlainJsonFileName(name))
                        return Refuse($"The backup contains an entry that isn't a settings file (\"{Printable(name)}\"). Nothing was changed.");
                    if (!seen.Add(name))
                        return Refuse($"The backup lists \"{name}\" twice. Nothing was changed.");
                    if (entry.Length > MaxEntryBytes)
                        return Refuse($"\"{name}\" is too large to be a settings file. Nothing was changed.");

                    string text;
                    using (var reader = new StreamReader(entry.Open(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
                        text = reader.ReadToEnd();

                    try { JToken.Parse(text); }
                    catch (JsonException)
                    {
                        return Refuse($"\"{name}\" in the backup isn't valid JSON. Nothing was changed.");
                    }

                    if (!string.Equals(name, InfoEntryName, StringComparison.OrdinalIgnoreCase))
                        pending.Add((name, text));
                }
            }
            catch (InvalidDataException)
            {
                return Refuse("That isn't a valid backup file (not a zip archive).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not read the backup {Path}", zipPath);
                return Refuse($"Couldn't read the backup: {ex.Message}");
            }

            if (pending.Count == 0)
                return Refuse("The backup contains no settings files.");

            // Keep what the user has now, so a restore can always be undone by hand.
            string safety;
            try
            {
                var backups = Path.Combine(DataFolder, "backups");
                Directory.CreateDirectory(backups);
                safety = Path.Combine(backups, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                CreateBackup(safety);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Could not save the current settings before a restore");
                return Refuse($"Couldn't save your current settings first, so nothing was changed: {ex.Message}");
            }

            int written = 0;
            try
            {
                Directory.CreateDirectory(DataFolder);
                foreach (var (name, text) in pending)
                {
                    AtomicFile.WriteAllText(Path.Combine(DataFolder, name), text);
                    written++;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Restore failed after {Written} of {Total} files", written, pending.Count);
                return new RestoreResult(false,
                    $"The restore stopped after {written} of {pending.Count} files: {ex.Message}. Your previous settings are saved in {safety}.",
                    written, safety);
            }

            _logger?.LogInformation("Restored {Count} settings files from {Path}", written, zipPath);
            return new RestoreResult(true, $"Restored {written} settings file{(written == 1 ? "" : "s")}.", written, safety);
        }

        private RestoreResult Refuse(string message)
        {
            _logger?.LogInformation("Restore refused: {Message}", message);
            return new RestoreResult(false, message, 0);
        }

        /// <summary>The top-level *.json files of the data folder.</summary>
        private List<string> ListSettingsFiles()
        {
            if (!Directory.Exists(DataFolder)) return new List<string>();
            return Directory.EnumerateFiles(DataFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(Path.GetFileName(f), InfoEntryName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>A bare file name ending in .json: no folders, no drive, no "..", no invalid characters.</summary>
        internal static bool IsPlainJsonFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..")) return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)) return false;
            return name.Length > ".json".Length && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static string Printable(string name) => name.Length <= 60 ? name : name.Substring(0, 57) + "...";

        private static string AppVersion()
        {
            var info = typeof(SettingsBackupService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                return typeof(SettingsBackupService).Assembly.GetName().Version?.ToString() ?? "unknown";
            var plus = info.IndexOf('+');
            return plus > 0 ? info.Substring(0, plus) : info;
        }
    }
}
