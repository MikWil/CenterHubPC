using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>One line of the clipboard card: the trimmed text and a command that copies the entry back.</summary>
    public sealed class ClipboardEntryViewModel
    {
        public ClipboardEntryViewModel(ClipboardItem item, Action<ClipboardItem> copy)
        {
            Text = ClipboardCardViewModel.OneLine(item.Content);
            CopyCommand = new RelayCommand(() => copy(item));
        }

        public string Text { get; }
        public ICommand CopyCommand { get; }
    }

    /// <summary>The last three clipboard entries; clicking one copies it to the clipboard again.</summary>
    public sealed partial class ClipboardCardViewModel : DashboardCardViewModel
    {
        public const int EntryCount = 3;
        private const int MaxLength = 120;

        private readonly ClipboardViewModel _clipboard;
        private bool _rebuildQueued;

        [ObservableProperty] private bool _hasEntries;

        public ClipboardCardViewModel(DashboardCardInfo info, ClipboardViewModel clipboard, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            _clipboard = clipboard;
            _clipboard.PropertyChanged += OnClipboardChanged;
            _clipboard.ClipboardHistory.CollectionChanged += OnHistoryChanged;
            Rebuild();
        }

        public ObservableCollection<ClipboardEntryViewModel> Entries { get; } = new();

        /// <summary>A clipboard text as one trimmed line (line breaks become spaces), cut at 120 characters.</summary>
        public static string OneLine(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            var line = string.Join(" ", content.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Where(p => p.Length > 0));
            return line.Length > MaxLength ? line.Substring(0, MaxLength) + "…" : line;
        }

        private void OnClipboardChanged(object? sender, PropertyChangedEventArgs e)
        {
            // The history list is replaceable (an ObservableProperty): not done today, but stay safe.
            if (e.PropertyName == nameof(ClipboardViewModel.ClipboardHistory))
                QueueRebuild();
        }

        // The history is refilled with Clear + Add per item: coalesce into one rebuild.
        private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

        private void QueueRebuild()
        {
            if (IsDisposed || _rebuildQueued) return;
            _rebuildQueued = true;
            try { Dispatcher.UIThread.Post(() => { _rebuildQueued = false; Rebuild(); }, DispatcherPriority.Background); }
            catch (InvalidOperationException) { _rebuildQueued = false; }
        }

        protected override void OnActiveChanged(bool active)
        {
            if (active) Rebuild();
        }

        private void Rebuild()
        {
            if (IsDisposed) return;
            Entries.Clear();
            foreach (var item in _clipboard.ClipboardHistory.Where(i => !string.IsNullOrWhiteSpace(i.Content)).Take(EntryCount))
                Entries.Add(new ClipboardEntryViewModel(item, Copy));
            HasEntries = Entries.Count > 0;
        }

        // The same method the Clipboard page uses (it also toasts "Copied to clipboard").
        private void Copy(ClipboardItem item)
        {
            if (IsDisposed) return;
            _clipboard.CopyToClipboardCommand.Execute(item);
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _clipboard.PropertyChanged -= OnClipboardChanged;
                _clipboard.ClipboardHistory.CollectionChanged -= OnHistoryChanged;
            }
            base.Dispose(disposing);
        }
    }
}
