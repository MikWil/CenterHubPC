using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class QuickNotesViewModel : BaseViewModel
    {
        private readonly QuickNotesService _notesService;

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
            RefreshNotes();

            // Select first note if available
            if (Notes.Count > 0)
            {
                SelectedNote = Notes[0];
            }

            Logger?.LogInformation("QuickNotesViewModel initialized with {Count} notes", Notes.Count);
        }

        partial void OnSelectedNoteChanged(QuickNote? value)
        {
            if (value != null)
            {
                CurrentTitle = value.Title;
                CurrentContent = value.Content;
            }
            else
            {
                CurrentTitle = string.Empty;
                CurrentContent = string.Empty;
            }
        }

        private void RefreshNotes()
        {
            var notesList = _notesService.GetNotes();
            Notes.Clear();
            foreach (var note in notesList.OrderByDescending(n => n.ModifiedAt))
            {
                Notes.Add(note);
            }
        }

        [RelayCommand]
        private void CreateNote()
        {
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
            if (note == null) return;
            
            // Save current note before switching (auto-save)
            if (SelectedNote != null && SelectedNote.Id != note.Id)
            {
                SelectedNote.Title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Untitled" : CurrentTitle;
                SelectedNote.Content = CurrentContent;
                _notesService.UpdateNote(SelectedNote);
            }
            
            SelectedNote = note;
            Logger?.LogDebug("Selected note: {Title}", note.Title);
        }

        [RelayCommand]
        private void SaveCurrentNote()
        {
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

            SelectedNote.Title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Untitled" : CurrentTitle;
            SelectedNote.Content = CurrentContent;
            _notesService.UpdateNote(SelectedNote);
            
            var selectedId = SelectedNote.Id;
            RefreshNotes();
            
            // Re-select the note
            SelectedNote = Notes.FirstOrDefault(n => n.Id == selectedId);
            
            Logger?.LogDebug("Saved note: {Title}", CurrentTitle);
            ToastService.Instance.Success($"Note saved: {CurrentTitle}");
        }

        [RelayCommand]
        private void DeleteNote(QuickNote? noteToDelete = null)
        {
            var note = noteToDelete ?? SelectedNote;
            if (note == null) return;

            var result = MessageBox.Show(
                $"Delete note '{note.Title}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var noteId = note.Id;
                var noteTitle = note.Title;
                _notesService.DeleteNote(noteId);
                RefreshNotes();
                SelectedNote = Notes.FirstOrDefault();
                ToastService.Instance.Success($"Note deleted: {noteTitle}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                // Save any pending changes
                if (SelectedNote != null && !string.IsNullOrEmpty(CurrentTitle))
                {
                    SelectedNote.Title = CurrentTitle;
                    SelectedNote.Content = CurrentContent;
                    _notesService.UpdateNote(SelectedNote);
                }
                Logger?.LogInformation("QuickNotesViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

