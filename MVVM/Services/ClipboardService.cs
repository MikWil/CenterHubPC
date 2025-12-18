using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CenterHubNew.MVVM.Services
{
    public class ClipboardService
    {
        private readonly string _historyFilePath;
        private readonly ILogger<ClipboardService>? _logger;
        private readonly int _maxHistoryItems = 50;
        private List<ClipboardItem> _history = new();

        public ClipboardService(ILogger<ClipboardService>? logger = null)
        {
            _logger = logger;
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            _historyFilePath = Path.Combine(appFolder, "clipboard-history.json");
            LoadHistory();
        }

        public List<ClipboardItem> GetHistory() => _history.ToList();

        public void AddItem(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            // Don't add duplicates of the most recent item
            if (_history.Count > 0 && _history[0].Content == content)
                return;

            var item = new ClipboardItem(content);
            _history.Insert(0, item);

            // Keep only max items (excluding pinned)
            var unpinned = _history.Where(x => !x.IsPinned).ToList();
            if (unpinned.Count > _maxHistoryItems)
            {
                var toRemove = unpinned.Skip(_maxHistoryItems).ToList();
                foreach (var r in toRemove)
                {
                    _history.Remove(r);
                }
            }

            SaveHistory();
            _logger?.LogDebug("Added clipboard item, total: {Count}", _history.Count);
        }

        public void RemoveItem(ClipboardItem item)
        {
            _history.Remove(item);
            SaveHistory();
        }

        public void TogglePin(ClipboardItem item)
        {
            item.IsPinned = !item.IsPinned;
            SaveHistory();
        }

        public void ClearHistory(bool keepPinned = true)
        {
            if (keepPinned)
            {
                _history = _history.Where(x => x.IsPinned).ToList();
            }
            else
            {
                _history.Clear();
            }
            SaveHistory();
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    _history = JsonConvert.DeserializeObject<List<ClipboardItem>>(json) ?? new List<ClipboardItem>();
                    _logger?.LogInformation("Loaded {Count} clipboard history items", _history.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load clipboard history");
                _history = new List<ClipboardItem>();
            }
        }

        private void SaveHistory()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_history, Formatting.Indented);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save clipboard history");
            }
        }
    }
}

