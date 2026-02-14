using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Windows;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class JsonStringifyViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string _inputJson = string.Empty;

        [ObservableProperty]
        private string _outputJson = string.Empty;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public JsonStringifyViewModel(
            ILogger<JsonStringifyViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("JsonStringifyViewModel initialized");
        }

        [RelayCommand]
        private void Stringify()
        {
            HasError = false;
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(InputJson))
            {
                HasError = true;
                ErrorMessage = "Input is empty";
                OutputJson = string.Empty;
                return;
            }

            try
            {
                // First validate that the input is valid JSON by parsing it
                using var doc = JsonDocument.Parse(InputJson);

                // Serialize the JSON string itself as a JSON string value (i.e. stringify it)
                // This produces a quoted, escaped string like: "{\"key\":\"value\"}"
                var minified = JsonSerializer.Serialize(doc.RootElement);
                OutputJson = JsonSerializer.Serialize(minified);

                Logger?.LogDebug("JSON stringified successfully");
                ToastService.Instance.Success("JSON stringified successfully");
            }
            catch (JsonException ex)
            {
                HasError = true;
                ErrorMessage = $"Invalid JSON: {ex.Message}";
                OutputJson = string.Empty;
                Logger?.LogWarning(ex, "Invalid JSON input");
                ToastService.Instance.Error("Invalid JSON input");
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error: {ex.Message}";
                OutputJson = string.Empty;
                Logger?.LogError(ex, "Failed to stringify JSON");
                ToastService.Instance.Error($"Failed to stringify: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CopyOutput()
        {
            if (string.IsNullOrEmpty(OutputJson))
            {
                ToastService.Instance.Error("Nothing to copy");
                return;
            }

            try
            {
                Clipboard.SetText(OutputJson);
                ToastService.Instance.Success("Stringified JSON copied to clipboard");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to copy to clipboard");
                ToastService.Instance.Error($"Failed to copy: {ex.Message}");
            }
        }

        [RelayCommand]
        private void Clear()
        {
            InputJson = string.Empty;
            OutputJson = string.Empty;
            HasError = false;
            ErrorMessage = string.Empty;
        }

        [RelayCommand]
        private void PasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    InputJson = Clipboard.GetText();
                    ToastService.Instance.Success("Pasted from clipboard");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to paste from clipboard");
                ToastService.Instance.Error($"Failed to paste: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("JsonStringifyViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
