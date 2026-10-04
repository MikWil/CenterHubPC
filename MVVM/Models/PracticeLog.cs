using System;
using System.Collections.Generic;
using System.Globalization;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>Everything practised on one calendar day.</summary>
    public sealed class PracticeDay
    {
        /// <summary>The calendar day (time of day is always midnight).</summary>
        public DateTime Date { get; set; }

        /// <summary>Total practice time that day, in seconds.</summary>
        public int Seconds { get; set; }

        /// <summary>Highest tempo reached in any session that day.</summary>
        public int MaxBpm { get; set; }

        public int Sessions { get; set; }

        /// <summary>What the last session played (a style name or "Click").</summary>
        public string? LastActivity { get; set; }
    }

    /// <summary>The file layout of practice-log.json.</summary>
    public sealed class PracticeLogData
    {
        public List<PracticeDay> Days { get; set; } = new();
    }

    /// <summary>One bar of the 14-day chart.</summary>
    public readonly record struct PracticeDayMinutes(DateTime Date, double Minutes);

    /// <summary>Display text for practice times (pure, so it can be unit-tested).</summary>
    public static class PracticeText
    {
        /// <summary>"0 min", "25 min", "1 h 05 min". Rounds to the nearest minute (under a minute shows "&lt;1 min").</summary>
        public static string Duration(TimeSpan time)
        {
            if (time <= TimeSpan.Zero) return "0 min";
            if (time < TimeSpan.FromSeconds(30)) return "<1 min";
            int minutes = (int)Math.Round(time.TotalMinutes);
            if (minutes < 60) return $"{minutes} min";
            return $"{minutes / 60} h {minutes % 60:00} min";
        }

        /// <summary>"Mon 3 Oct · 25 min".</summary>
        public static string BarTooltip(DateTime date, double minutes) =>
            $"{date.ToString("ddd d MMM", CultureInfo.InvariantCulture)} · {(int)Math.Round(minutes)} min";
    }
}
