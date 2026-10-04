using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Remembers how long the user practises with the metronome / drum machine, per day, in
    /// %AppData%\CenterHub\practice-log.json. Only per-day totals are kept (at most 400 days).
    /// </summary>
    public sealed class PracticeLogService
    {
        /// <summary>Sessions shorter than this are accidental starts and are ignored.</summary>
        public static readonly TimeSpan MinSession = TimeSpan.FromSeconds(10);

        public const int MaxDays = 400;

        private readonly ILogger<PracticeLogService>? _logger;
        private readonly string _filePath;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new();
        private readonly Dictionary<DateTime, PracticeDay> _days = new();

        /// <summary>Raised (on the recording thread) after a session was added.</summary>
        public event Action? Changed;

        public PracticeLogService(ILogger<PracticeLogService>? logger = null)
            : this(logger, storageFolder: null) { }

        /// <param name="storageFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        public PracticeLogService(ILogger<PracticeLogService>? logger, string? storageFolder)
            : this(logger, storageFolder, now: null) { }

        /// <param name="storageFolder">Override for tests; defaults to %AppData%\CenterHub.</param>
        /// <param name="now">Clock override for tests; defaults to <see cref="DateTime.Now"/>.</param>
        public PracticeLogService(ILogger<PracticeLogService>? logger, string? storageFolder, Func<DateTime>? now)
        {
            _logger = logger;
            _now = now ?? (() => DateTime.Now);
            var folder = storageFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "practice-log.json");
            Load();
        }

        // ── Recording ──

        /// <summary>
        /// Adds a finished session to the day it started on. Returns false (and records nothing)
        /// for sessions shorter than <see cref="MinSession"/>.
        /// </summary>
        public bool RecordSession(DateTime start, TimeSpan duration, int maxBpm, string? styleOrClick)
        {
            if (duration < MinSession) return false;

            lock (_gate)
            {
                var date = start.Date;
                if (!_days.TryGetValue(date, out var day))
                {
                    day = new PracticeDay { Date = date };
                    _days[date] = day;
                }

                day.Seconds += (int)Math.Round(duration.TotalSeconds);
                day.MaxBpm = Math.Max(day.MaxBpm, maxBpm);
                day.Sessions++;
                if (!string.IsNullOrWhiteSpace(styleOrClick)) day.LastActivity = styleOrClick;

                Prune();
                Save();
            }

            Changed?.Invoke();
            return true;
        }

        // ── Queries ──

        public TimeSpan TodayTotal
        {
            get
            {
                lock (_gate) return SecondsOn(_now().Date);
            }
        }

        /// <summary>Practice time since Monday of the current week.</summary>
        public TimeSpan ThisWeekTotal
        {
            get
            {
                lock (_gate)
                {
                    var today = _now().Date;
                    var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    double seconds = 0;
                    for (var d = monday; d <= today; d = d.AddDays(1))
                        seconds += SecondsOn(d).TotalSeconds;
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }

        /// <summary>
        /// Consecutive days with at least one session, ending today. A day that has not had a session
        /// yet does not break a streak that ran up to yesterday.
        /// </summary>
        public int Streak
        {
            get
            {
                lock (_gate)
                {
                    var day = _now().Date;
                    if (!_days.ContainsKey(day)) day = day.AddDays(-1);

                    int streak = 0;
                    while (_days.ContainsKey(day))
                    {
                        streak++;
                        day = day.AddDays(-1);
                    }
                    return streak;
                }
            }
        }

        /// <summary>Highest tempo practised in the last 30 days (today included); 0 when there is none.</summary>
        public int BestBpmLast30Days
        {
            get
            {
                lock (_gate)
                {
                    var first = _now().Date.AddDays(-29);
                    return _days.Values.Where(d => d.Date >= first).Select(d => d.MaxBpm).DefaultIfEmpty(0).Max();
                }
            }
        }

        /// <summary>Minutes practised on each of the last 14 days, oldest first, today last (days without practice included).</summary>
        public IReadOnlyList<PracticeDayMinutes> Last14Days
        {
            get
            {
                lock (_gate)
                {
                    var today = _now().Date;
                    var list = new List<PracticeDayMinutes>(14);
                    for (int i = 13; i >= 0; i--)
                    {
                        var d = today.AddDays(-i);
                        list.Add(new PracticeDayMinutes(d, SecondsOn(d).TotalMinutes));
                    }
                    return list;
                }
            }
        }

        private TimeSpan SecondsOn(DateTime date) =>
            _days.TryGetValue(date, out var day) ? TimeSpan.FromSeconds(day.Seconds) : TimeSpan.Zero;

        // ── Persistence ──

        private void Prune()
        {
            if (_days.Count <= MaxDays) return;
            foreach (var date in _days.Keys.OrderBy(d => d).Take(_days.Count - MaxDays).ToList())
                _days.Remove(date);
        }

        private void Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(_filePath)) return;

                    var data = JsonConvert.DeserializeObject<PracticeLogData>(File.ReadAllText(_filePath));
                    foreach (var day in data?.Days ?? new List<PracticeDay>())
                    {
                        if (day is null) continue;
                        var date = day.Date.Date;
                        if (!_days.TryGetValue(date, out var merged))
                        {
                            merged = new PracticeDay { Date = date };
                            _days[date] = merged;
                        }
                        merged.Seconds += Math.Max(0, day.Seconds);
                        merged.MaxBpm = Math.Max(merged.MaxBpm, Math.Clamp(day.MaxBpm, 0, 1000));
                        merged.Sessions += Math.Max(0, day.Sessions);
                        merged.LastActivity = day.LastActivity ?? merged.LastActivity;
                    }
                    Prune();
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "practice-log.json is corrupt; quarantining file");
                    _days.Clear();
                    AtomicFile.QuarantineCorrupt(_filePath);
                }
                // Not quarantined: a transient lock must not hide the user's file.
                catch (Exception ex) { _logger?.LogError(ex, "Error loading practice-log.json"); }
            }
        }

        // Called with _gate held.
        private void Save()
        {
            try
            {
                var data = new PracticeLogData { Days = _days.Values.OrderBy(d => d.Date).ToList() };
                AtomicFile.WriteAllText(_filePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex) { _logger?.LogError(ex, "Error saving practice-log.json"); }
        }
    }
}
