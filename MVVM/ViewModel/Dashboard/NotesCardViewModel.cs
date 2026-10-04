using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>The most recently edited note: its title and a three-line preview.</summary>
    public sealed partial class NotesCardViewModel : DashboardCardViewModel
    {
        private readonly QuickNotesService _notes;
        private DispatcherTimer? _timer;

        [ObservableProperty] private bool _hasNote;
        [ObservableProperty] private string _noteTitle = "";
        [ObservableProperty] private string _notePreview = "";

        public NotesCardViewModel(DashboardCardInfo info, QuickNotesService notes, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            _notes = notes;
            Refresh();
        }

        /// <summary>The first three non-empty lines of a note, trimmed.</summary>
        public static string Preview(string? content, int lines = 3)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            var kept = new List<string>(lines);
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                kept.Add(trimmed);
                if (kept.Count == lines) break;
            }
            return string.Join("\n", kept);
        }

        protected override void OnActiveChanged(bool active)
        {
            if (active)
            {
                Refresh();
                _timer ??= CreateTimer();
                _timer.Start();
            }
            else
            {
                _timer?.Stop();
            }
        }

        // The notes service has no change event: look again every few seconds while the page is open.
        private DispatcherTimer CreateTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                if (!IsDisposed) Refresh();
            };
            return timer;
        }

        private void Refresh()
        {
            if (IsDisposed) return;
            try
            {
                var latest = _notes.GetNotes().OrderByDescending(n => n.ModifiedAt).FirstOrDefault();
                HasNote = latest != null;
                NoteTitle = latest == null ? "" : (string.IsNullOrWhiteSpace(latest.Title) ? "Untitled" : latest.Title);
                NotePreview = latest == null ? "" : Preview(latest.Content);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Notes card: could not read the notes");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _timer?.Stop();
                _timer = null;
            }
            base.Dispose(disposing);
        }
    }
}
