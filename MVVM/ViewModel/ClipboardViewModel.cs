using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class ClipboardViewModel : BaseViewModel
    {
        private readonly ClipboardService _clipboardService;
        private readonly DispatcherTimer _clipboardTimer;
        private string _lastClipboardContent = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ClipboardItem> _clipboardHistory = new();

        [ObservableProperty]
        private ClipboardItem? _selectedItem;

        [ObservableProperty]
        private bool _isMonitoring = true;

        public ClipboardViewModel(
            ClipboardService clipboardService,
            ILogger<ClipboardViewModel>? logger = null) : base(logger)
        {
            _clipboardService = clipboardService;

            // Load existing history
            RefreshHistory();

            // Set up clipboard monitoring timer
            _clipboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();

            Logger?.LogInformation("ClipboardViewModel initialized");
        }

        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsMonitoring || IsDisposed)
                return;

            try
            {
                if (Clipboard.ContainsText())
                {
                    var currentContent = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(currentContent) && currentContent != _lastClipboardContent)
                    {
                        _lastClipboardContent = currentContent;
                        _clipboardService.AddItem(currentContent);
                        RefreshHistory();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to read clipboard");
            }
        }

        private void RefreshHistory()
        {
            var items = _clipboardService.GetHistory();
            ClipboardHistory.Clear();
            foreach (var item in items.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.Timestamp))
            {
                ClipboardHistory.Add(item);
            }
        }

        [RelayCommand]
        private void CopyToClipboard(ClipboardItem? item)
        {
            if (item == null) return;

            try
            {
                _lastClipboardContent = item.Content; // Prevent re-adding
                Clipboard.SetText(item.Content);
                Logger?.LogDebug("Copied item to clipboard");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to copy to clipboard");
            }
        }

        [RelayCommand]
        private void TogglePin(ClipboardItem? item)
        {
            if (item == null) return;
            _clipboardService.TogglePin(item);
            RefreshHistory();
        }

        [RelayCommand]
        private void DeleteItem(ClipboardItem? item)
        {
            if (item == null) return;
            _clipboardService.RemoveItem(item);
            RefreshHistory();
        }

        [RelayCommand]
        private void ClearHistory()
        {
            var result = MessageBox.Show(
                "Clear all clipboard history? Pinned items will be kept.",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _clipboardService.ClearHistory(keepPinned: true);
                RefreshHistory();
            }
        }

        [RelayCommand]
        private void ToggleMonitoring()
        {
            IsMonitoring = !IsMonitoring;
            Logger?.LogInformation("Clipboard monitoring: {Status}", IsMonitoring ? "enabled" : "disabled");
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _clipboardTimer.Stop();
                Logger?.LogInformation("ClipboardViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

