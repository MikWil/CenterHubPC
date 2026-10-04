using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using Newtonsoft.Json;
using Xunit;

namespace CenterHubNew.Tests;

public class SetlistHelperTests
{
    [Fact]
    public void Describe_drum_song_uses_the_style_name()
    {
        var song = new SetlistSong { Bpm = 120, DrumsMode = true, StyleId = "rock-8ths" };
        Assert.Equal("120 BPM · Rock 8ths", SetlistHelper.Describe(song, "Rock 8ths"));
    }

    [Fact]
    public void Describe_drum_song_without_a_known_style_still_reads_well()
    {
        var song = new SetlistSong { Bpm = 90, DrumsMode = true };
        Assert.Equal("90 BPM · Drums", SetlistHelper.Describe(song, null));
    }

    [Fact]
    public void Describe_click_song_shows_the_bar_length()
    {
        var song = new SetlistSong { Bpm = 120, DrumsMode = false, BeatsPerMeasure = 4 };
        Assert.Equal("120 BPM · Click 4/4", SetlistHelper.Describe(song, "ignored"));
        song.BeatsPerMeasure = 3;
        Assert.Equal("120 BPM · Click 3/4", SetlistHelper.Describe(song, null));
    }

    [Theory]
    [InlineData(0, +1, 5, 1)]
    [InlineData(4, +1, 5, 0)]   // next wraps to the first song
    [InlineData(0, -1, 5, 4)]   // previous wraps to the last song
    [InlineData(3, -1, 5, 2)]
    [InlineData(-1, +1, 5, 0)]  // nothing active yet: next = first
    [InlineData(-1, -1, 5, 4)]  // nothing active yet: previous = last
    [InlineData(0, +1, 1, 0)]   // single song stays put
    [InlineData(0, +1, 0, -1)]  // empty list
    public void Step_wraps_around(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, SetlistHelper.Step(current, delta, count));
    }

    [Fact]
    public void Sanitize_song_clamps_and_defaults()
    {
        var song = new SetlistSong
        {
            Id = "",
            Bpm = 5,
            BeatsPerMeasure = 99,
            Kit = (DrumKitKind)42,
            Subdivision = (ClickSubdivision)42,
        };
        SetlistHelper.Sanitize(song);

        Assert.False(string.IsNullOrWhiteSpace(song.Id));
        Assert.Equal(30, song.Bpm);
        Assert.Equal(12, song.BeatsPerMeasure);
        Assert.Equal(DrumKitKind.Acoustic, song.Kit);
        Assert.Equal(ClickSubdivision.None, song.Subdivision);
    }
}

public class SetlistSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubTests-" + Guid.NewGuid().ToString("N"));

    public SetlistSettingsTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    private MetronomeSettings LoadFromJson(string json)
    {
        File.WriteAllText(Path.Combine(_folder, "metronome.json"), json);
        return new MetronomeSettingsService(null, _folder).Load();
    }

    [Fact]
    public void Defaults_have_no_setlists()
    {
        var settings = new MetronomeSettingsService(null, _folder).Load();
        Assert.Empty(settings.Setlists);
        Assert.Null(settings.ActiveSetlistId);
        Assert.Equal(0, settings.ActiveSongIndex);
    }

    [Fact]
    public void Setlists_round_trip()
    {
        var service = new MetronomeSettingsService(null, _folder);
        var list = new Setlist
        {
            Name = "Friday gig",
            Songs =
            {
                new SetlistSong
                {
                    Name = "Opener", Bpm = 132, DrumsMode = true, StyleId = "rock-8ths",
                    Kit = DrumKitKind.Electro, BeatsPerMeasure = 4, Subdivision = ClickSubdivision.Triplets, CountIn = true,
                },
                new SetlistSong { Name = "Click 90", Bpm = 90, DrumsMode = false, BeatsPerMeasure = 7 },
            },
        };
        service.Save(new MetronomeSettings
        {
            Setlists = { list },
            ActiveSetlistId = list.Id,
            ActiveSongIndex = 1,
        });

        var loaded = new MetronomeSettingsService(null, _folder).Load();
        var back = Assert.Single(loaded.Setlists);
        Assert.Equal(list.Id, back.Id);
        Assert.Equal("Friday gig", back.Name);
        Assert.Equal(2, back.Songs.Count);

        var first = back.Songs[0];
        Assert.Equal(list.Songs[0].Id, first.Id);
        Assert.Equal("Opener", first.Name);
        Assert.Equal(132, first.Bpm);
        Assert.True(first.DrumsMode);
        Assert.Equal("rock-8ths", first.StyleId);
        Assert.Equal(DrumKitKind.Electro, first.Kit);
        Assert.Equal(ClickSubdivision.Triplets, first.Subdivision);
        Assert.True(first.CountIn);

        Assert.Equal(7, back.Songs[1].BeatsPerMeasure);
        Assert.False(back.Songs[1].DrumsMode);

        Assert.Equal(list.Id, loaded.ActiveSetlistId);
        Assert.Equal(1, loaded.ActiveSongIndex);
    }

    [Fact]
    public void Bad_setlist_values_are_sanitised()
    {
        var loaded = LoadFromJson("""
        {
          "ActiveSetlistId": "does-not-exist",
          "ActiveSongIndex": 7,
          "Setlists": [
            { "Id": "a", "Name": "Gig", "Songs": [
                { "Id": "s1", "Name": "One", "Bpm": 999, "BeatsPerMeasure": 0, "Kit": 99, "Subdivision": "Bogus" },
                null ] },
            { "Id": "b", "Name": "No songs", "Songs": null },
            null
          ]
        }
        """);

        Assert.Equal(2, loaded.Setlists.Count);
        var gig = loaded.Setlists[0];
        var song = Assert.Single(gig.Songs);
        Assert.Equal(280, song.Bpm);
        Assert.Equal(1, song.BeatsPerMeasure);
        Assert.Equal(DrumKitKind.Acoustic, song.Kit);
        Assert.Equal(ClickSubdivision.None, song.Subdivision);

        Assert.NotNull(loaded.Setlists[1].Songs);
        Assert.Empty(loaded.Setlists[1].Songs);

        Assert.Null(loaded.ActiveSetlistId);
        Assert.Equal(0, loaded.ActiveSongIndex);
    }

    [Fact]
    public void Null_setlists_become_an_empty_list()
    {
        var loaded = LoadFromJson("""{ "Bpm": 100, "Setlists": null }""");
        Assert.Equal(100, loaded.Bpm);
        Assert.NotNull(loaded.Setlists);
        Assert.Empty(loaded.Setlists);
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(-4, 0)]
    [InlineData(1, 1)]
    public void Active_song_index_is_clamped_to_the_active_setlist(int stored, int expected)
    {
        var loaded = LoadFromJson($$"""
        {
          "ActiveSetlistId": "a",
          "ActiveSongIndex": {{stored}},
          "Setlists": [ { "Id": "a", "Name": "Gig", "Songs": [ { "Name": "One" }, { "Name": "Two" } ] } ]
        }
        """);

        Assert.Equal("a", loaded.ActiveSetlistId);
        Assert.Equal(expected, loaded.ActiveSongIndex);
    }

    [Fact]
    public void An_unknown_enum_name_does_not_quarantine_the_file()
    {
        var loaded = LoadFromJson("""{ "Bpm": 101, "ClickSound": "Vuvuzela", "Subdivision": "Quintuplets" }""");

        Assert.Equal(101, loaded.Bpm);
        Assert.Equal(MetronomeSound.Clock, loaded.ClickSound);
        Assert.Equal(ClickSubdivision.None, loaded.Subdivision);
        Assert.Empty(Directory.GetFiles(_folder, "metronome.json.corrupt-*"));
    }
}

public class PracticeLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubTests-" + Guid.NewGuid().ToString("N"));

    // 2026-10-03 is a Saturday; the week started on Monday 2026-09-28.
    private DateTime _now = new(2026, 10, 3, 12, 0, 0);

    public PracticeLogTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    private PracticeLogService Create() => new(null, _folder, () => _now);

    private static void Practice(PracticeLogService log, DateTime day, int minutes, int bpm = 100) =>
        Assert.True(log.RecordSession(day.Date.AddHours(18), TimeSpan.FromMinutes(minutes), bpm, "Click"));

    [Fact]
    public void Empty_log_reads_zero()
    {
        var log = Create();
        Assert.Equal(TimeSpan.Zero, log.TodayTotal);
        Assert.Equal(TimeSpan.Zero, log.ThisWeekTotal);
        Assert.Equal(0, log.Streak);
        Assert.Equal(0, log.BestBpmLast30Days);
        Assert.Equal(14, log.Last14Days.Count);
        Assert.All(log.Last14Days, d => Assert.Equal(0, d.Minutes));
    }

    [Fact]
    public void Sessions_shorter_than_ten_seconds_are_ignored()
    {
        var log = Create();

        Assert.False(log.RecordSession(_now, TimeSpan.FromSeconds(9), 120, "Click"));
        Assert.Equal(TimeSpan.Zero, log.TodayTotal);
        Assert.Equal(0, log.Streak);
        Assert.False(File.Exists(Path.Combine(_folder, "practice-log.json")));

        Assert.True(log.RecordSession(_now, TimeSpan.FromSeconds(10), 120, "Click"));
        Assert.Equal(TimeSpan.FromSeconds(10), log.TodayTotal);
    }

    [Fact]
    public void Sessions_add_up_per_day()
    {
        var log = Create();
        Practice(log, _now, 20, bpm: 100);
        Practice(log, _now, 15, bpm: 130);
        Practice(log, _now.AddDays(-1), 40, bpm: 90);

        Assert.Equal(TimeSpan.FromMinutes(35), log.TodayTotal);
        Assert.Equal(TimeSpan.FromMinutes(75), log.ThisWeekTotal);
    }

    [Fact]
    public void The_week_starts_on_monday()
    {
        var log = Create();
        Practice(log, new DateTime(2026, 9, 27), 30); // Sunday, previous week
        Practice(log, new DateTime(2026, 9, 28), 20); // Monday
        Practice(log, new DateTime(2026, 10, 3), 10); // Saturday (today)

        Assert.Equal(TimeSpan.FromMinutes(30), log.ThisWeekTotal);
        Assert.Equal(TimeSpan.FromMinutes(10), log.TodayTotal);

        _now = new DateTime(2026, 10, 4, 9, 0, 0); // Sunday: still the same week
        Assert.Equal(TimeSpan.FromMinutes(30), log.ThisWeekTotal);
        Assert.Equal(TimeSpan.Zero, log.TodayTotal);

        _now = new DateTime(2026, 10, 5, 9, 0, 0); // Monday: a new week
        Assert.Equal(TimeSpan.Zero, log.ThisWeekTotal);
    }

    [Fact]
    public void Streak_counts_consecutive_days_including_today()
    {
        var log = Create();
        Practice(log, new DateTime(2026, 9, 30), 5);
        Practice(log, new DateTime(2026, 10, 1), 5);
        Practice(log, new DateTime(2026, 10, 2), 5);
        Practice(log, new DateTime(2026, 10, 3), 5);
        Assert.Equal(4, log.Streak);
    }

    [Fact]
    public void Today_without_a_session_does_not_break_the_streak()
    {
        var log = Create();
        Practice(log, new DateTime(2026, 10, 1), 5);
        Practice(log, new DateTime(2026, 10, 2), 5);

        Assert.Equal(2, log.Streak); // today still to come

        _now = new DateTime(2026, 10, 4, 8, 0, 0); // a whole day was skipped now
        Assert.Equal(0, log.Streak);
    }

    [Fact]
    public void A_gap_ends_the_streak()
    {
        var log = Create();
        Practice(log, new DateTime(2026, 9, 28), 5);
        Practice(log, new DateTime(2026, 9, 29), 5);
        // 30 Sep and 1 Oct skipped
        Practice(log, new DateTime(2026, 10, 2), 5);
        Practice(log, new DateTime(2026, 10, 3), 5);

        Assert.Equal(2, log.Streak);
    }

    [Fact]
    public void Best_bpm_covers_the_last_30_days_only()
    {
        var log = Create();
        Practice(log, _now.AddDays(-30), 10, bpm: 220); // too old
        Practice(log, _now.AddDays(-29), 10, bpm: 140); // oldest day that counts
        Practice(log, _now.AddDays(-3), 10, bpm: 125);
        Practice(log, _now, 10, bpm: 90);

        Assert.Equal(140, log.BestBpmLast30Days);

        _now = _now.AddDays(1); // the 140 day falls out of the window
        Assert.Equal(125, log.BestBpmLast30Days);
    }

    [Fact]
    public void The_highest_bpm_of_a_day_is_kept()
    {
        var log = Create();
        Practice(log, _now, 10, bpm: 150);
        Practice(log, _now, 10, bpm: 110);
        Assert.Equal(150, log.BestBpmLast30Days);
    }

    [Fact]
    public void Last14Days_is_oldest_first_and_includes_empty_days()
    {
        var log = Create();
        Practice(log, _now, 25);
        Practice(log, _now.AddDays(-13), 5);
        Practice(log, _now.AddDays(-14), 99); // outside the window
        Practice(log, _now.AddDays(-2), 12);

        var days = log.Last14Days;
        Assert.Equal(14, days.Count);
        Assert.Equal(new DateTime(2026, 9, 20), days[0].Date);
        Assert.Equal(5, days[0].Minutes);
        Assert.Equal(new DateTime(2026, 10, 3), days[days.Count - 1].Date);
        Assert.Equal(25, days[days.Count - 1].Minutes);
        Assert.Equal(12, days[11].Minutes);
        Assert.Equal(0, days[5].Minutes);
    }

    [Fact]
    public void A_session_counts_for_the_day_it_started_on()
    {
        var log = Create();
        Assert.True(log.RecordSession(new DateTime(2026, 10, 2, 23, 50, 0), TimeSpan.FromMinutes(30), 100, "Click"));

        Assert.Equal(TimeSpan.Zero, log.TodayTotal);
        Assert.Equal(30, log.Last14Days[12].Minutes);
    }

    [Fact]
    public void Log_survives_a_restart()
    {
        var log = Create();
        Practice(log, _now, 25, bpm: 133);
        Practice(log, _now.AddDays(-1), 10, bpm: 100);

        var again = Create();
        Assert.Equal(TimeSpan.FromMinutes(25), again.TodayTotal);
        Assert.Equal(2, again.Streak);
        Assert.Equal(133, again.BestBpmLast30Days);
        Assert.Equal(10, again.Last14Days[12].Minutes);
    }

    [Fact]
    public void Only_400_days_are_kept()
    {
        var log = Create();
        var last = _now;
        for (int i = 404; i >= 0; i--)
            Assert.True(log.RecordSession(last.AddDays(-i), TimeSpan.FromMinutes(1), 100, "Click"));

        var data = JsonConvert.DeserializeObject<PracticeLogData>(
            File.ReadAllText(Path.Combine(_folder, "practice-log.json")))!;
        Assert.Equal(PracticeLogService.MaxDays, data.Days.Count);
        Assert.Equal(last.Date.AddDays(-(PracticeLogService.MaxDays - 1)), data.Days.Min(d => d.Date));
        Assert.Equal(last.Date, data.Days.Max(d => d.Date));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_not_overwritten()
    {
        var path = Path.Combine(_folder, "practice-log.json");
        File.WriteAllText(path, "{ this is not json");

        var log = Create();

        Assert.Equal(TimeSpan.Zero, log.TodayTotal);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_folder, "practice-log.json.corrupt-*"));

        Practice(log, _now, 10); // and the log works again afterwards
        Assert.Equal(TimeSpan.FromMinutes(10), Create().TodayTotal);
    }

    [Fact]
    public void Changed_is_raised_only_for_recorded_sessions()
    {
        var log = Create();
        int raised = 0;
        log.Changed += () => raised++;

        log.RecordSession(_now, TimeSpan.FromSeconds(3), 100, "Click");
        Assert.Equal(0, raised);

        log.RecordSession(_now, TimeSpan.FromSeconds(30), 100, "Click");
        Assert.Equal(1, raised);
    }
}

public class PracticeTextTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(20, "<1 min")]
    [InlineData(60, "1 min")]
    [InlineData(25 * 60, "25 min")]
    [InlineData(59 * 60, "59 min")]
    [InlineData(65 * 60, "1 h 05 min")]
    [InlineData(2 * 3600, "2 h 00 min")]
    public void Duration_reads_naturally(int seconds, string expected)
    {
        Assert.Equal(expected, PracticeText.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Bar_tooltip_shows_weekday_date_and_minutes()
    {
        Assert.Equal("Sat 3 Oct · 25 min", PracticeText.BarTooltip(new DateTime(2026, 10, 3), 25));
        Assert.Equal("Mon 28 Sep · 0 min", PracticeText.BarTooltip(new DateTime(2026, 9, 28), 0));
    }
}
