using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class QuickNotesViewModel : BaseViewModel
    {
        private const int AutoSaveDelayMs = 800;

        private readonly QuickNotesService _notesService;
        private readonly DispatcherTimer _autoSaveTimer;

        // True while the editor is being loaded from a note (ignore edit notifications)
        private bool _loading;
        // True while the notes list is rebuilt (ignore transient selection changes)
        private bool _refreshing;
        // True while the previously selected note is being deleted (skip write-back)
        private bool _suppressSave;

        [ObservableProperty]
        private ObservableCollection<QuickNote> _notes = new();

        [ObservableProperty]
        private QuickNote? _selectedNote;

        [ObservableProperty]
        private string _currentContent = string.Empty;

        [ObservableProperty]
        private string _currentTitle = string.Empty;

        public QuickNotesViewModel(
            QuickNotesService notesService,
            ILogger<QuickNotesViewModel>? logger = null) : base(logger)
        {
            _notesService = notesService;

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMs) };
            _autoSaveTimer.Tick += OnAutoSaveTick;

            RefreshNotes();

            // Select first note if available
            if (Notes.Count > 0)
            {
                SelectedNote = Notes[0];
            }

            Logger?.LogInformation("QuickNotesViewModel initialized with {Count} notes", Notes.Count);
        }

        partial void OnSelectedNoteChanging(QuickNote? oldValue, QuickNote? newValue)
        {
            if (_refreshing || _suppressSave || oldValue == null || ReferenceEquals(oldValue, newValue))
                return;

            // Persist edits to the note we are leaving before the editor is overwritten
            _autoSaveTimer.Stop();
            PersistNote(oldValue);
        }

        partial void OnSelectedNoteChanged(QuickNote? value)
        {
            // Transient selection changes during a list rebuild must not touch the editor
            if (_refreshing) return;

            LoadEditor(value);
        }

        partial void OnCurrentTitleChanged(string value) => ScheduleAutoSave();

        partial void OnCurrentContentChanged(string value) => ScheduleAutoSave();

        private void LoadEditor(QuickNote? note)
        {
            _loading = true;
            try
            {
                CurrentTitle = note?.Title ?? string.Empty;
                CurrentContent = note?.Content ?? string.Empty;
            }
            finally
            {
                _loading = false;
            }
        }

        private void ScheduleAutoSave()
        {
            if (IsDisposed || _loading || _refreshing || SelectedNote == null) return;

            // Debounce: restart the timer on every edit
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void OnAutoSaveTick(object? sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            if (IsDisposed) return;

            var note = SelectedNote;
            if (note != null)
            {
                PersistNote(note);
                Logger?.LogDebug("Autosaved note: {Title}", note.Title);
            }
        }

        /// <summary>
        /// Writes the editor text into the note and persists it (only if something changed).
        /// </summary>
        private void PersistNote(QuickNote note)
        {
            var title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Untitled" : CurrentTitle;
            if (note.Title == title && note.Content == CurrentContent) return;

            note.Title = title;
            note.Content = CurrentContent;
            _notesService.UpdateNote(note);
        }

        /// <summary>
        /// Rebuilds the list while preserving the selection (by Id) and leaving the editor untouched.
        /// </summary>
        private void RefreshNotes()
        {
            var selectedId = SelectedNote?.Id;
            _refreshing = true;
            try
            {
                var notesList = _notesService.GetNotes();
                Notes.Clear();
                foreach (var note in notesList.OrderByDescending(n => n.ModifiedAt))
                {
                    Notes.Add(note);
                }

                SelectedNote = selectedId == null
                    ? null
                    : Notes.FirstOrDefault(n => n.Id == selectedId);
            }
            finally
            {
                _refreshing = false;
            }
        }

        [RelayCommand]
        private void CreateNote()
        {
            if (IsDisposed) return;

            // Save the current note first so no edits are lost
            _autoSaveTimer.Stop();
            if (SelectedNote != null) PersistNote(SelectedNote);

            var note = _notesService.CreateNote();
            var noteId = note.Id;
            RefreshNotes();

            // Find and select the newly created note
            var newNote = Notes.FirstOrDefault(n => n.Id == noteId);
            if (newNote != null)
            {
                SelectedNote = newNote;
                Logger?.LogDebug("Created and selected new note: {Id}", noteId);
                ToastService.Instance.Success("New note created");
            }
        }

        [RelayCommand]
        private void SelectNote(QuickNote? note)
        {
            if (IsDisposed || note == null) return;

            // The previous note is saved by OnSelectedNoteChanging
            SelectedNote = note;
            Logger?.LogDebug("Selected note: {Title}", note.Title);
        }

        [RelayCommand]
        private void SaveCurrentNote()
        {
            if (IsDisposed) return;
            _autoSaveTimer.Stop();

            // If no note is selected but we have content, create a new note
            if (SelectedNote == null)
            {
                if (string.IsNullOrWhiteSpace(CurrentTitle) && string.IsNullOrWhiteSpace(CurrentContent))
                {
                    return; // Nothing to save
                }

                // Create a new note with the current content
                var title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Untitled" : CurrentTitle;
                var newNote = _notesService.CreateNote(title);
                newNote.Content = CurrentContent;
                _notesService.UpdateNote(newNote);

                RefreshNotes();
                SelectedNote = Notes.FirstOrDefault(n => n.Id == newNote.Id);
                Logger?.LogDebug("Created and saved new note: {Title}", title);
                ToastService.Instance.Success($"Note created and saved: {title}");
                return;
            }

            // Always persist on an explicit save (refreshes ModifiedAt)
            SelectedNote.Title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Untitled" : CurrentTitle;
            SelectedNote.Content = CurrentContent;
            _notesService.UpdateNote(SelectedNote);

            // RefreshNotes preserves the selection and the editor contents
            RefreshNotes();

            Logger?.LogDebug("Saved note: {Title}", CurrentTitle);
            ToastService.Instance.Success($"Note saved: {CurrentTitle}");
        }

        [RelayCommand]
        private void DeleteNote(QuickNote? noteToDelete = null)
        {
            if (IsDisposed) return;

            var note = noteToDelete ?? SelectedNote;
            if (note == null) return;

            var result = System.Windows.Forms.MessageBox.Show(
                $"Delete note '{note.Title}'?",
                "Confirm Delete",
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                var noteId = note.Id;
                var noteTitle = note.Title;
                var deletingSelected = SelectedNote?.Id == noteId;

                if (deletingSelected)
                {
                    // Don't write the editor back into a note that no longer exists
                    _autoSaveTimer.Stop();
                    _suppressSave = true;
                }

                try
                {
                    _notesService.DeleteNote(noteId);
                    RefreshNotes();

                    if (deletingSelected)
                    {
                        SelectedNote = Notes.FirstOrDefault();
                        // SelectedNote may already be null (no change notification), so reload explicitly
                        LoadEditor(SelectedNote);
                    }
                }
                finally
                {
                    _suppressSave = false;
                }

                ToastService.Instance.Success($"Note deleted: {noteTitle}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Tick -= OnAutoSaveTick;

                // Save any pending changes (PersistNote is a no-op when nothing changed,
                // and names an empty title "Untitled" — don't drop content typed under it).
                if (SelectedNote != null)
                {
                    PersistNote(SelectedNote);
                }
                Logger?.LogInformation("QuickNotesViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
