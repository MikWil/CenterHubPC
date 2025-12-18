using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CenterHubNew.MVVM.Services
{
    public class QuickNotesService
    {
        private readonly string _notesFilePath;
        private readonly ILogger<QuickNotesService>? _logger;
        private List<QuickNote> _notes = new();

        public QuickNotesService(ILogger<QuickNotesService>? logger = null)
        {
            _logger = logger;
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            _notesFilePath = Path.Combine(appFolder, "quick-notes.json");
            LoadNotes();
        }

        public List<QuickNote> GetNotes() => _notes.ToList();

        public QuickNote CreateNote(string title = "New Note")
        {
            var note = new QuickNote(title);
            _notes.Insert(0, note);
            SaveNotes();
            _logger?.LogInformation("Created new note: {Title}", title);
            return note;
        }

        public void UpdateNote(QuickNote note)
        {
            var existing = _notes.FirstOrDefault(n => n.Id == note.Id);
            if (existing != null)
            {
                existing.Title = note.Title;
                existing.Content = note.Content;
                existing.ModifiedAt = DateTime.Now;
                SaveNotes();
            }
        }

        public void DeleteNote(string noteId)
        {
            var note = _notes.FirstOrDefault(n => n.Id == noteId);
            if (note != null)
            {
                _notes.Remove(note);
                SaveNotes();
                _logger?.LogInformation("Deleted note: {Title}", note.Title);
            }
        }

        public void SaveNotes()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_notes, Formatting.Indented);
                File.WriteAllText(_notesFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save notes");
            }
        }

        private void LoadNotes()
        {
            try
            {
                if (File.Exists(_notesFilePath))
                {
                    var json = File.ReadAllText(_notesFilePath);
                    _notes = JsonConvert.DeserializeObject<List<QuickNote>>(json) ?? new List<QuickNote>();
                    _logger?.LogInformation("Loaded {Count} notes", _notes.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load notes");
                _notes = new List<QuickNote>();
            }
        }
    }
}

