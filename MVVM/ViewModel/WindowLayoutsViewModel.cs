using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class WindowLayoutsViewModel : BaseViewModel
    {
        private readonly WindowLayoutService _service;

        [ObservableProperty] private ObservableCollection<WindowLayoutItem> _layouts = new();

        public bool HasLayouts   => Layouts.Count > 0;
        public bool HasNoLayouts => Layouts.Count == 0;

        // Capture-name dialog state
        [ObservableProperty] private bool   _isNameDialogOpen;
        [ObservableProperty] private string _newLayoutName = "";

        // Rename dialog state
        [ObservableProperty] private bool   _isRenameDialogOpen;
        [ObservableProperty] private string _renameInput = "";
        private WindowLayoutItem? _renameTarget;

        public WindowLayoutsViewModel(
            WindowLayoutService service,
            ILogger<WindowLayoutsViewModel>? logger = null) : base(logger)
        {
            _service = service;
            Layouts.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasLayouts));
                OnPropertyChanged(nameof(HasNoLayouts));
            };
            RefreshFromStore();
            Logger?.LogInformation("WindowLayoutsViewModel initialized — {Count} stored", Layouts.Count);
        }

        private void RefreshFromStore()
        {
            Layouts.Clear();
            foreach (var l in _service.Layouts)
                Layouts.Add(new WindowLayoutItem { Layout = l });
        }

        // ───────── Capture flow ─────────

        [RelayCommand]
        private void BeginCapture()
        {
            // Pre-fill with a sensible default name
            NewLayoutName = $"Layout {DateTime.Now:HH:mm}";
            IsNameDialogOpen = true;
        }

        [RelayCommand]
        private void CancelCapture()
        {
            IsNameDialogOpen = false;
            NewLayoutName = "";
        }

        [RelayCommand]
        private void ConfirmCapture()
        {
            var name = string.IsNullOrWhiteSpace(NewLayoutName)
                ? $"Layout {DateTime.Now:MMM d, HH:mm}"
                : NewLayoutName.Trim();

            var layout = _service.AddLayout(name);
            IsNameDialogOpen = false;
            NewLayoutName = "";
            RefreshFromStore();
            ToastService.Instance.Success($"Saved '{layout.Name}' — {layout.Windows.Count} windows");
        }

        // ───────── Apply / delete / recapture ─────────

        [RelayCommand]
        private void Apply(WindowLayoutItem? item)
        {
            if (item is null) return;
            var n = _service.ApplyLayout(item.Layout);
            if (n > 0)
                ToastService.Instance.Success($"Applied '{item.Name}' — {n} window{(n == 1 ? "" : "s")} placed");
            else
                ToastService.Instance.Warning($"'{item.Name}' — nothing to apply (no matching windows are open)");
        }

        [RelayCommand]
        private void Delete(WindowLayoutItem? item)
        {
            if (item is null) return;
            _service.DeleteLayout(item.Layout);
            Layouts.Remove(item);
            ToastService.Instance.Info($"Deleted '{item.Name}'");
        }

        [RelayCommand]
        private void Recapture(WindowLayoutItem? item)
        {
            if (item is null) return;
            _service.RecaptureLayout(item.Layout);
            // Re-wrap so the bound display values refresh
            var idx = Layouts.IndexOf(item);
            if (idx >= 0)
            {
                Layouts[idx] = new WindowLayoutItem { Layout = item.Layout };
            }
            ToastService.Instance.Success($"Re-captured '{item.Name}' — {item.Layout.Windows.Count} windows");
        }

        // ───────── Rename ─────────

        [RelayCommand]
        private void BeginRename(WindowLayoutItem? item)
        {
            if (item is null) return;
            _renameTarget = item;
            RenameInput = item.Name;
            IsRenameDialogOpen = true;
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenameDialogOpen = false;
            _renameTarget = null;
            RenameInput = "";
        }

        [RelayCommand]
        private void ConfirmRename()
        {
            if (_renameTarget is null) return;
            _service.RenameLayout(_renameTarget.Layout, RenameInput);
            var idx = Layouts.IndexOf(_renameTarget);
            if (idx >= 0)
            {
                Layouts[idx] = new WindowLayoutItem { Layout = _renameTarget.Layout };
            }
            ToastService.Instance.Success($"Renamed to '{_renameTarget.Name}'");
            IsRenameDialogOpen = false;
            _renameTarget = null;
            RenameInput = "";
        }

        // ───────── Hotkey entry points (called from App.axaml.cs) ─────────

        public void ApplyByIndex(int zeroBasedIndex)
        {
            var item = Layouts.ElementAtOrDefault(zeroBasedIndex);
            if (item is null)
            {
                ToastService.Instance.Warning($"No layout at slot {zeroBasedIndex + 1}");
                return;
            }
            Apply(item);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
