using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class ConverterToolsViewModel : BaseViewModel
    {
        // ── Base64 ──
        [ObservableProperty]
        private string _base64Input = string.Empty;

        [ObservableProperty]
        private string _base64Output = string.Empty;

        // ── URL Encode/Decode ──
        [ObservableProperty]
        private string _urlInput = string.Empty;

        [ObservableProperty]
        private string _urlOutput = string.Empty;

        // ── Hash Generator ──
        [ObservableProperty]
        private string _hashInput = string.Empty;

        [ObservableProperty]
        private string _hashMd5 = string.Empty;

        [ObservableProperty]
        private string _hashSha256 = string.Empty;

        [ObservableProperty]
        private string _hashSha512 = string.Empty;

        // ── GUID Generator ──
        [ObservableProperty]
        private string _generatedGuid = string.Empty;

        // ── Epoch / Unix Timestamp ──
        [ObservableProperty]
        private string _epochInput = string.Empty;

        [ObservableProperty]
        private string _epochOutput = string.Empty;

        public ConverterToolsViewModel(
            ILogger<ConverterToolsViewModel>? logger = null) : base(logger)
        {
            GenerateNewGuid();
            Logger?.LogInformation("ConverterToolsViewModel initialized");
        }

        // ═══════════════════════════════════════════
        // BASE64
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void Base64Encode()
        {
            if (string.IsNullOrEmpty(Base64Input)) return;
            try
            {
                Base64Output = Convert.ToBase64String(Encoding.UTF8.GetBytes(Base64Input));
                ToastService.Instance.Success("Encoded to Base64");
            }
            catch (Exception ex)
            {
                Base64Output = string.Empty;
                ToastService.Instance.Error($"Encode failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void Base64Decode()
        {
            if (string.IsNullOrEmpty(Base64Input)) return;
            try
            {
                Base64Output = Encoding.UTF8.GetString(Convert.FromBase64String(Base64Input));
                ToastService.Instance.Success("Decoded from Base64");
            }
            catch (Exception ex)
            {
                Base64Output = string.Empty;
                ToastService.Instance.Error($"Decode failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // URL ENCODE / DECODE
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void UrlEncode()
        {
            if (string.IsNullOrEmpty(UrlInput)) return;
            try
            {
                UrlOutput = Uri.EscapeDataString(UrlInput);
                ToastService.Instance.Success("URL encoded");
            }
            catch (Exception ex)
            {
                UrlOutput = string.Empty;
                ToastService.Instance.Error($"Encode failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void UrlDecode()
        {
            if (string.IsNullOrEmpty(UrlInput)) return;
            try
            {
                UrlOutput = Uri.UnescapeDataString(UrlInput);
                ToastService.Instance.Success("URL decoded");
            }
            catch (Exception ex)
            {
                UrlOutput = string.Empty;
                ToastService.Instance.Error($"Decode failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // HASH GENERATOR
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void GenerateHashes()
        {
            if (string.IsNullOrEmpty(HashInput))
            {
                HashMd5 = string.Empty;
                HashSha256 = string.Empty;
                HashSha512 = string.Empty;
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(HashInput);

            HashMd5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            HashSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            HashSha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

            ToastService.Instance.Success("Hashes generated");
        }

        // ═══════════════════════════════════════════
        // GUID GENERATOR
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void GenerateNewGuid()
        {
            GeneratedGuid = Guid.NewGuid().ToString();
        }

        [RelayCommand]
        private void GenerateNewGuidUpperCase()
        {
            GeneratedGuid = Guid.NewGuid().ToString().ToUpperInvariant();
        }

        [RelayCommand]
        private void GenerateNewGuidNoDashes()
        {
            GeneratedGuid = Guid.NewGuid().ToString("N");
        }

        // ═══════════════════════════════════════════
        // EPOCH / UNIX TIMESTAMP
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void EpochToDate()
        {
            if (string.IsNullOrWhiteSpace(EpochInput)) return;
            try
            {
                if (long.TryParse(EpochInput.Trim(), out long epoch))
                {
                    // Auto-detect seconds vs milliseconds
                    var dto = epoch > 9999999999L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                    EpochOutput = dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz");
                    ToastService.Instance.Success("Timestamp converted");
                }
                else
                {
                    EpochOutput = "Invalid number";
                    ToastService.Instance.Error("Invalid epoch value");
                }
            }
            catch (Exception ex)
            {
                EpochOutput = $"Error: {ex.Message}";
                ToastService.Instance.Error($"Conversion failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void DateToEpoch()
        {
            EpochInput = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            EpochOutput = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " (now)";
            ToastService.Instance.Success("Current epoch generated");
        }

        // ═══════════════════════════════════════════
        // COPY HELPERS
        // ═══════════════════════════════════════════

        [RelayCommand]
        private void CopyBase64Output()
        {
            CopyToClipboard(Base64Output, "Base64 result");
        }

        [RelayCommand]
        private void CopyUrlOutput()
        {
            CopyToClipboard(UrlOutput, "URL result");
        }

        [RelayCommand]
        private void CopyHashMd5()
        {
            CopyToClipboard(HashMd5, "MD5 hash");
        }

        [RelayCommand]
        private void CopyHashSha256()
        {
            CopyToClipboard(HashSha256, "SHA-256 hash");
        }

        [RelayCommand]
        private void CopyHashSha512()
        {
            CopyToClipboard(HashSha512, "SHA-512 hash");
        }

        [RelayCommand]
        private void CopyGuid()
        {
            CopyToClipboard(GeneratedGuid, "GUID");
        }

        [RelayCommand]
        private void CopyEpochOutput()
        {
            CopyToClipboard(EpochOutput, "Epoch result");
        }

        private void CopyToClipboard(string text, string label)
        {
            if (string.IsNullOrEmpty(text))
            {
                ToastService.Instance.Error("Nothing to copy");
                return;
            }
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
                ToastService.Instance.Success($"{label} copied");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to copy to clipboard");
                ToastService.Instance.Error($"Copy failed: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("ConverterToolsViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
