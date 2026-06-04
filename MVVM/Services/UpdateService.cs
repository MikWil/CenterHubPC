using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    public sealed class UpdateInfo
    {
        public string Version        { get; init; } = "";   // "5.2.0"
        public string TagName        { get; init; } = "";   // "v5.2.0"
        public string ReleaseName    { get; init; } = "";   // "CenterHub v5.2.0"
        public string ReleaseUrl     { get; init; } = "";   // GH release page
        public string Body           { get; init; } = "";   // raw markdown body
        public string MsiDownloadUrl { get; init; } = "";   // browser_download_url
        public long   MsiSizeBytes   { get; init; }
    }

    /// <summary>Persisted across runs at %LOCALAPPDATA%\CenterHub\update-cache.json.</summary>
    public sealed class UpdateCheckCache
    {
        [JsonPropertyName("lastChecked")] public DateTime LastCheckedUtc { get; set; }
        [JsonPropertyName("latestTag")]   public string?  LatestTagName  { get; set; }
        [JsonPropertyName("skippedTag")]  public string?  SkippedVersion { get; set; }
    }

    /// <summary>
    /// Checks the GitHub Releases API for newer versions and orchestrates the
    /// download + handoff to msiexec. Cached for 6 hours to stay well below
    /// the anonymous 60-req/hour rate limit.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        private const string GitHubRepo = "MikWil/WilkonCenterHub";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

        private readonly ILogger<UpdateService>? _logger;
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private UpdateCheckCache _cache = new();

        public UpdateInfo? AvailableUpdate { get; private set; }

        /// <summary>Fires whenever the available update changes (found or dismissed).</summary>
        public event Action<UpdateInfo?>? UpdateChanged;

        public UpdateService(ILogger<UpdateService>? logger = null)
        {
            _logger = logger;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CenterHub", CurrentVersion));
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CenterHub");
            Directory.CreateDirectory(dir);
            _cachePath = Path.Combine(dir, "update-cache.json");
            LoadCache();
        }

        public string CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        // ─────────────────── Persistence ───────────────────

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var json = File.ReadAllText(_cachePath);
                var c = JsonSerializer.Deserialize<UpdateCheckCache>(json);
                if (c != null) _cache = c;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load update cache");
            }
        }

        private void SaveCache()
        {
            try
            {
                File.WriteAllText(_cachePath,
                    JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save update cache");
            }
        }

        // ─────────────────── Check ───────────────────

        /// <summary>
        /// Hit the GitHub Releases API and return an UpdateInfo if a newer
        /// version is available (and isn't on the skip list). Cached for 6h
        /// unless forceRefresh is set.
        /// </summary>
        public async Task<UpdateInfo?> CheckAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            // Cache hit?
            if (!forceRefresh &&
                _cache.LastCheckedUtc != default &&
                _cache.LastCheckedUtc + CacheTtl > DateTime.UtcNow)
            {
                _logger?.LogDebug("Update check skipped (cache valid until {Until})",
                    _cache.LastCheckedUtc + CacheTtl);
                if (AvailableUpdate != null) return AvailableUpdate;
            }

            try
            {
                var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("GitHub releases API returned {Status}", (int)resp.StatusCode);
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tag      = root.GetPropertyOrEmpty("tag_name");
                var name     = root.GetPropertyOrEmpty("name");
                var body     = root.GetPropertyOrEmpty("body");
                var htmlUrl  = root.GetPropertyOrEmpty("html_url");

                var verStr = tag.TrimStart('v', 'V');
                if (!Version.TryParse(verStr, out var latestVer))
                {
                    _logger?.LogWarning("Could not parse latest version '{Tag}'", tag);
                    return null;
                }
                if (!Version.TryParse(CurrentVersion, out var currentVer))
                {
                    _logger?.LogWarning("Could not parse current version '{Cur}'", CurrentVersion);
                    return null;
                }

                _cache.LastCheckedUtc = DateTime.UtcNow;
                _cache.LatestTagName = tag;
                SaveCache();

                // Already up-to-date?
                if (latestVer <= currentVer)
                {
                    _logger?.LogInformation("Up to date — current {Cur}, latest {Latest}",
                        CurrentVersion, verStr);
                    SetAvailable(null);
                    return null;
                }

                // User-skipped this version?
                if (string.Equals(_cache.SkippedVersion, tag, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("Newer version {Tag} found but user has skipped it", tag);
                    SetAvailable(null);
                    return null;
                }

                // Find the .msi asset (prefer that over zips)
                string msiUrl = "";
                long   msiSize = 0;
                if (root.TryGetProperty("assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var assetName = a.GetPropertyOrEmpty("name");
                        if (assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            msiUrl  = a.GetPropertyOrEmpty("browser_download_url");
                            msiSize = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number
                                ? sz.GetInt64() : 0;
                            break;
                        }
                    }
                }

                var info = new UpdateInfo
                {
                    Version        = verStr,
                    TagName        = tag,
                    ReleaseName    = string.IsNullOrEmpty(name) ? tag : name,
                    ReleaseUrl     = htmlUrl,
                    Body           = body,
                    MsiDownloadUrl = msiUrl,
                    MsiSizeBytes   = msiSize,
                };
                _logger?.LogInformation("Update available: {Tag} ({Size} bytes)", tag, msiSize);
                SetAvailable(info);
                return info;
            }
            catch (TaskCanceledException) { return null; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Update check failed");
                return null;
            }
        }

        private void SetAvailable(UpdateInfo? info)
        {
            AvailableUpdate = info;
            try { UpdateChanged?.Invoke(info); } catch { }
        }

        // ─────────────────── Skip / dismiss ───────────────────

        /// <summary>Mark the currently-available version as skipped (won't notify again until newer).</summary>
        public void SkipCurrent()
        {
            if (AvailableUpdate is null) return;
            _cache.SkippedVersion = AvailableUpdate.TagName;
            SaveCache();
            _logger?.LogInformation("User skipped update {Tag}", AvailableUpdate.TagName);
            SetAvailable(null);
        }

        /// <summary>Hide the banner this session without persisting (re-checks tomorrow).</summary>
        public void DismissForSession() => SetAvailable(null);

        /// <summary>Clear the skip list so the user gets prompted again.</summary>
        public void ResetSkip()
        {
            _cache.SkippedVersion = null;
            SaveCache();
        }

        // ─────────────────── Download + install ───────────────────

        /// <summary>
        /// Download the MSI to %TEMP%\CenterHubUpdate\ and return the local path,
        /// or null on failure. Reports bytes-downloaded / total-bytes via progress.
        /// </summary>
        public async Task<string?> DownloadAsync(IProgress<(long downloaded, long total)>? progress = null,
                                                 CancellationToken ct = default)
        {
            if (AvailableUpdate is null || string.IsNullOrEmpty(AvailableUpdate.MsiDownloadUrl))
                return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "CenterHubUpdate");
            Directory.CreateDirectory(tempDir);
            var dest = Path.Combine(tempDir, $"CenterHub-{AvailableUpdate.Version}.msi");

            try
            {
                using var resp = await _http.GetAsync(AvailableUpdate.MsiDownloadUrl,
                                                       HttpCompletionOption.ResponseHeadersRead, ct)
                                            .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("MSI download returned {Status}", (int)resp.StatusCode);
                    return null;
                }

                var total = resp.Content.Headers.ContentLength ?? AvailableUpdate.MsiSizeBytes;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(dest);

                var buf = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report((downloaded, total));
                }

                _logger?.LogInformation("Downloaded MSI to {Path} ({Size} bytes)", dest, downloaded);
                return dest;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MSI download failed");
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Hand the MSI off to msiexec. We use /passive so the user sees a
        /// progress dialog but no prompts; MajorUpgrade replaces the old
        /// install automatically.
        /// </summary>
        public bool LaunchInstaller(string msiPath)
        {
            try
            {
                if (!File.Exists(msiPath)) return false;
                var psi = new ProcessStartInfo
                {
                    FileName  = "msiexec.exe",
                    Arguments = $"/i \"{msiPath}\" /passive /norestart",
                    UseShellExecute = true,
                };
                Process.Start(psi);
                _logger?.LogInformation("Launched MSI installer: {Path}", msiPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to launch installer");
                return false;
            }
        }

        public void Dispose()
        {
            try { _http.Dispose(); } catch { }
        }
    }

    internal static class JsonElementExt
    {
        /// <summary>Safe string getter for optional JSON properties.</summary>
        public static string GetPropertyOrEmpty(this JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? "";
            return "";
        }
    }
}
