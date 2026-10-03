using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using Xunit;

namespace CenterHubNew.Tests;

/// <summary>Renders the drum machine offline (no audio device) and collects what it did.</summary>
internal sealed class EngineHarness
{
    public const int SampleRate = 44100;

    /// <summary>Frames in one beat / one 4-beat bar at 120 BPM.</summary>
    public const int Beat = 22050;
    public const int Bar = Beat * 4;

    public static readonly DrumKit Kit = new(DrumKitKind.Rock);
    public static DrumStyle Rock => DrumStyleLibrary.Find("rock-8ths")!;

    private readonly List<float> _audio = new();

    public EngineHarness(DrumKit? kit = null) => Engine = new DrumMachineEngine(kit ?? Kit) { Bpm = 120 };

    public DrumMachineEngine Engine { get; }
    public List<DrumEngineEvent> Events { get; } = new();

    /// <summary>A one-part style whose groove, fills and transition are all <paramref name="bar"/> (no intro/outro).</summary>
    public static DrumStyle OneBarStyle(DrumBar bar) => new()
    {
        Id = "test-bar",
        Name = "Test bar",
        Parts = new[] { new DrumPart { Name = "A", Main = new[] { bar }, Fills = new[] { bar }, Transition = bar } },
    };

    /// <summary>The interleaved stereo samples of the frame range [from, from + frames).</summary>
    public float[] Slice(long from, int frames) => _audio.GetRange((int)(from * 2), frames * 2).ToArray();

    /// <summary>First frame in [from, to) with any non-zero sample, or -1 when it is silent there.</summary>
    public long FirstSound(long from, long to)
    {
        for (long f = from; f < to && f * 2 + 1 < _audio.Count; f++)
            if (_audio[(int)(f * 2)] != 0f || _audio[(int)(f * 2 + 1)] != 0f) return f;
        return -1;
    }

    public void Run(int frames, int block = 512)
    {
        var buffer = new float[block * 2];
        while (frames > 0)
        {
            int n = Math.Min(block, frames);
            Assert.Equal(n * 2, Engine.Read(buffer, 0, n * 2));
            for (int i = 0; i < n * 2; i++) _audio.Add(buffer[i]);
            frames -= n;
            while (Engine.TryDequeueEvent(out var e)) Events.Add(e);
        }
    }

    /// <summary>Loudest absolute sample in the frame range [from, to).</summary>
    public float Peak(long from, long to)
    {
        float peak = 0;
        for (long i = from * 2; i < to * 2 && i < _audio.Count; i++)
            peak = Math.Max(peak, Math.Abs(_audio[(int)i]));
        return peak;
    }

    /// <summary>The section each bar started in, keyed by bar number.</summary>
    public Dictionary<int, DrumSection> SectionOfBar() =>
        Events.Where(e => e.Step == 0 && e.Section != DrumSection.Stopped)
              .GroupBy(e => e.Bar)
              .ToDictionary(g => g.Key, g => g.Last().Section);
}

public class DrumBarTests
{
    [Fact]
    public void Parse_reads_velocities_and_ignores_beat_separators()
    {
        var bar = DrumBar.Parse(
            "HH|x-x-|x-x-|",
            "SD|--g-|s--X|");

        Assert.Equal(8, bar.Steps);
        Assert.Equal(new[] { DrumVoice.ClosedHat, DrumVoice.Snare }, bar.Voices);
        Assert.Equal(0.80f, bar.VelocityAt(DrumVoice.ClosedHat, 0));
        Assert.Equal(0f, bar.VelocityAt(DrumVoice.ClosedHat, 1));
        Assert.Equal(0.30f, bar.VelocityAt(DrumVoice.Snare, 2));
        Assert.Equal(0.55f, bar.VelocityAt(DrumVoice.Snare, 4));
        Assert.Equal(1.00f, bar.VelocityAt(DrumVoice.Snare, 7));
        Assert.Equal(2, bar.HitsAt(2).Count);   // hat + ghost snare together
        Assert.Empty(bar.HitsAt(1));
    }

    [Theory]
    [InlineData("HH|x-x-|", "SD|x-x|")]      // lines of different length
    [InlineData("ZZ|x-x-|", "SD|x-x-|")]     // unknown voice
    [InlineData("HH|x-q-|", "SD|x-x-|")]     // unknown step character
    [InlineData("HH|x-x-|", "HH|x-x-|")]     // same voice twice
    [InlineData("HH x-x-", "SD|x-x-|")]      // missing separator
    public void Parse_rejects_malformed_tabs(string first, string second)
        => Assert.Throws<FormatException>(() => DrumBar.Parse(first, second));
}

public class DrumStyleLibraryTests
{
    [Fact]
    public void Every_builtin_style_is_structurally_valid()
    {
        var problems = DrumStyleLibrary.All.SelectMany(DrumStyleLibrary.Validate).ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Style_ids_are_unique_and_findable()
    {
        var ids = DrumStyleLibrary.All.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(DrumStyleLibrary.All, s => Assert.Same(s, DrumStyleLibrary.Find(s.Id)));
        Assert.Null(DrumStyleLibrary.Find("no-such-style"));
        Assert.Null(DrumStyleLibrary.Find(null));
    }

    [Fact]
    public void Library_covers_many_genres()
    {
        Assert.True(DrumStyleLibrary.All.Count >= 25, $"only {DrumStyleLibrary.All.Count} styles");
        Assert.True(DrumStyleLibrary.Genres.Count >= 8, $"only {DrumStyleLibrary.Genres.Count} genres");
    }

    [Fact]
    public void Every_part_has_a_backbeat_or_cymbal_and_a_kick()
    {
        // A groove with no kick at all, or nothing keeping time, is almost certainly a typo.
        foreach (var style in DrumStyleLibrary.All)
            foreach (var part in style.Parts)
            {
                var voices = part.Main.SelectMany(b => b.Voices).ToHashSet();
                Assert.True(voices.Contains(DrumVoice.Kick), $"{style.Id}/{part.Name}: no kick");
                Assert.True(voices.Count >= 2, $"{style.Id}/{part.Name}: only one voice");
            }
    }
}

public class DrumKitTests
{
    public static IEnumerable<object[]> Kits => Enum.GetValues<DrumKitKind>().Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kits))]
    public void Every_voice_is_a_clean_sample(DrumKitKind kind)
    {
        var kit = new DrumKit(kind);
        foreach (var voice in Enum.GetValues<DrumVoice>())
        {
            var s = kit.GetSample(voice);
            Assert.True(s.Length > 100, $"{kind}/{voice} is empty");
            Assert.All(s, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f, $"{kind}/{voice} out of range"));
            Assert.True(s.Max(Math.Abs) > 0.2f, $"{kind}/{voice} is nearly silent");
            Assert.True(Math.Abs(s[^1]) < 0.01f, $"{kind}/{voice} ends with a click");
            Assert.True(Math.Abs(s[0]) < 0.2f, $"{kind}/{voice} starts with a click");
            Assert.InRange(kit.GetPan(voice), -1f, 1f);
        }
    }

    [Fact]
    public void Kits_are_deterministic()
    {
        var a = new DrumKit(DrumKitKind.Rock);
        var b = new DrumKit(DrumKitKind.Rock);
        foreach (var voice in Enum.GetValues<DrumVoice>())
            Assert.Equal(a.GetSample(voice), b.GetSample(voice));
    }

    [Fact]
    public void Kick_is_dark_and_hats_are_bright()
    {
        // Zero-crossing rate is a cheap brightness measure.
        static double Zcr(float[] s)
        {
            int crossings = 0;
            for (int i = 1; i < s.Length; i++)
                if ((s[i - 1] < 0) != (s[i] < 0)) crossings++;
            return (double)crossings / s.Length;
        }

        // Synthesized kits only: a zero-crossing-rate threshold tuned for synthesis doesn't fit real
        // recordings (the recorded hat is bright but has a low-frequency body, ZCR 0.24 vs the 0.25 bar).
        foreach (var kind in Enum.GetValues<DrumKitKind>().Where(k => k != DrumKitKind.Acoustic))
        {
            var kit = new DrumKit(kind);
            double kick = Zcr(kit.GetSample(DrumVoice.Kick));
            double hat = Zcr(kit.GetSample(DrumVoice.ClosedHat));
            double floorTom = Zcr(kit.GetSample(DrumVoice.FloorTom));
            double highTom = Zcr(kit.GetSample(DrumVoice.HighTom));
            Assert.True(kick < 0.05, $"{kind} kick too bright ({kick:F3})");
            Assert.True(hat > 0.25, $"{kind} hat too dark ({hat:F3})");
            Assert.True(floorTom < highTom, $"{kind} floor tom should be lower than the high tom");
        }
    }

    [Fact]
    public void Every_click_sound_has_a_distinct_accent()
    {
        var kit = EngineHarness.Kit;
        foreach (var sound in Enum.GetValues<MetronomeSound>())
        {
            var tick = kit.GetClick(sound, accent: false);
            var accent = kit.GetClick(sound, accent: true);
            Assert.True(tick.Length > 100 && accent.Length > 100);
            Assert.All(tick, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
            Assert.NotEqual(tick, accent);
        }
    }
}

public class DrumEngineClickTests
{
    [Fact]
    public void Beats_land_on_the_exact_sample()
    {
        var h = new EngineHarness();
        h.Engine.Start();
        h.Run(EngineHarness.Bar + EngineHarness.Beat + 100);

        var beats = h.Events.Where(e => e.IsBeatStart).ToList();
        Assert.Equal(6, beats.Count);
        for (int i = 0; i < beats.Count; i++)
            Assert.InRange(beats[i].Frame, (long)i * EngineHarness.Beat - 1, (long)i * EngineHarness.Beat + 1);

        Assert.Equal(new[] { 1, 2, 3, 4, 1, 2 }, beats.Select(e => e.Beat));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2 }, beats.Select(e => e.Bar));
        Assert.All(beats, e => Assert.Equal(DrumSection.Main, e.Section));
        Assert.All(beats, e => Assert.Null(e.Pattern));
    }

    [Fact]
    public void Click_sounds_at_the_beat_and_is_silent_in_between()
    {
        var h = new EngineHarness();
        h.Engine.Start();
        h.Run(EngineHarness.Bar);

        for (int beat = 0; beat < 4; beat++)
        {
            long at = (long)beat * EngineHarness.Beat;
            Assert.True(h.Peak(at, at + 600) > 0.05f, $"beat {beat + 1} is silent");
            Assert.Equal(0f, h.Peak(at + EngineHarness.Beat - 4000, at + EngineHarness.Beat - 2));
        }
    }

    [Fact]
    public void Timing_does_not_depend_on_the_audio_buffer_size()
    {
        static List<long> Frames(int block)
        {
            var h = new EngineHarness();
            h.Engine.Bpm = 137;                       // a tempo with fractional samples per tick
            h.Engine.Subdivision = ClickSubdivision.Triplets;
            h.Engine.Start();
            h.Run(EngineHarness.SampleRate * 5, block);
            return h.Events.Select(e => e.Frame).ToList();
        }

        var reference = Frames(512);
        Assert.Equal(reference, Frames(333));
        Assert.Equal(reference, Frames(4410));
    }

    [Fact]
    public void Tempo_stays_accurate_over_a_long_run()
    {
        var h = new EngineHarness();
        h.Engine.Bpm = 137;
        h.Engine.Start();
        h.Run(EngineHarness.SampleRate * 60, 2048);

        var beats = h.Events.Where(e => e.IsBeatStart).ToList();
        double framesPerBeat = EngineHarness.SampleRate * 60.0 / 137;
        Assert.Equal(137, beats.Count);
        for (int i = 0; i < beats.Count; i++)
            Assert.InRange(beats[i].Frame, i * framesPerBeat - 1.5, i * framesPerBeat + 1.5);   // no drift
    }

    [Fact]
    public void Tempo_change_takes_effect_while_playing()
    {
        var h = new EngineHarness();
        h.Engine.Start();
        h.Run(EngineHarness.Beat * 2 + 10);
        h.Engine.Bpm = 60;
        h.Run(EngineHarness.SampleRate * 3);

        var beats = h.Events.Where(e => e.IsBeatStart).Select(e => e.Frame).ToList();
        // Beat 3 was already due at 120 BPM spacing from beat 2; from then on beats are a second apart.
        long gap = beats[4] - beats[3];
        Assert.InRange(gap, EngineHarness.SampleRate - 2, EngineHarness.SampleRate + 2);
    }

    [Fact]
    public void Muted_beats_are_silent_and_accents_are_louder()
    {
        var h = new EngineHarness();
        h.Engine.SetAccents(new[] { BeatAccent.Accent, BeatAccent.Mute, BeatAccent.Normal, BeatAccent.Normal });
        h.Engine.Start();
        h.Run(EngineHarness.Bar);

        float accent = h.Peak(0, 2000);
        float muted = h.Peak(EngineHarness.Beat, EngineHarness.Beat + 2000);
        float normal = h.Peak(EngineHarness.Beat * 2, EngineHarness.Beat * 2 + 2000);
        Assert.Equal(0f, muted);
        Assert.True(normal > 0.05f);
        Assert.True(accent > normal, $"accent {accent} should be louder than normal {normal}");
    }

    [Theory]
    [InlineData(ClickSubdivision.Eighths, new[] { 0.5 })]
    [InlineData(ClickSubdivision.Triplets, new[] { 1 / 3.0, 2 / 3.0 })]
    [InlineData(ClickSubdivision.Sixteenths, new[] { 0.25, 0.5, 0.75 })]
    [InlineData(ClickSubdivision.Swing, new[] { 2 / 3.0 })]
    public void Subdivisions_click_between_the_beats(ClickSubdivision subdivision, double[] positions)
    {
        var plain = new EngineHarness();
        plain.Engine.Start();
        plain.Run(EngineHarness.Beat);

        var h = new EngineHarness();
        h.Engine.Subdivision = subdivision;
        h.Engine.Start();
        h.Run(EngineHarness.Beat);

        foreach (double p in positions)
        {
            long at = (long)Math.Round(p * EngineHarness.Beat);
            Assert.True(h.Peak(at, at + 600) > 0.02f, $"no click at {p:F2} of the beat");
            Assert.Equal(0f, plain.Peak(at, at + 600));
        }
    }

    [Fact]
    public void Beats_per_bar_follow_the_click_setting()
    {
        var h = new EngineHarness();
        h.Engine.SetClickBeats(3);
        h.Engine.Start();
        h.Run(EngineHarness.Beat * 7);

        var beats = h.Events.Where(e => e.IsBeatStart).ToList();
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 3, 1 }, beats.Select(e => e.Beat));
        Assert.All(beats, e => Assert.Equal(3, e.BeatsInBar));
    }

    [Fact]
    public void Gap_trainer_silences_every_other_bar()
    {
        var h = new EngineHarness();
        h.Engine.SetGap(1, 1);
        h.Engine.Start();
        h.Run(EngineHarness.Bar * 3);

        Assert.True(h.Peak(0, EngineHarness.Bar) > 0.05f);
        Assert.Equal(0f, h.Peak(EngineHarness.Bar, EngineHarness.Bar * 2));
        Assert.True(h.Peak(EngineHarness.Bar * 2, EngineHarness.Bar * 3) > 0.05f);
        Assert.All(h.Events.Where(e => e.Bar == 2), e => Assert.True(e.Muted));
        Assert.All(h.Events.Where(e => e.Bar != 2), e => Assert.False(e.Muted));
    }

    [Fact]
    public void Stop_raises_a_final_event_and_goes_idle()
    {
        var h = new EngineHarness();
        h.Engine.Start();
        h.Run(EngineHarness.Beat);
        Assert.True(h.Engine.IsPlaying);

        h.Engine.Stop();
        h.Run(EngineHarness.SampleRate);

        Assert.False(h.Engine.IsPlaying);
        Assert.True(h.Engine.IsIdle);
        Assert.Equal(DrumSection.Stopped, h.Events[^1].Section);
        Assert.Equal(0f, h.Peak(EngineHarness.Beat + EngineHarness.SampleRate - 1000, EngineHarness.Beat + EngineHarness.SampleRate));
    }

    [Fact]
    public void Restart_begins_again_from_beat_one()
    {
        var h = new EngineHarness();
        h.Engine.Start();
        h.Run(EngineHarness.Beat * 2 + 500);
        h.Engine.Stop();
        h.Run(5000);
        h.Events.Clear();

        long restartFrame = h.Engine.FramesRendered;
        h.Engine.Start();
        h.Run(EngineHarness.Beat);

        Assert.Equal(1, h.Events[0].Beat);
        Assert.Equal(1, h.Events[0].Bar);
        Assert.InRange(h.Events[0].Frame, restartFrame, restartFrame + 1);
    }

    [Fact]
    public void Idle_engine_renders_silence_and_previews_are_audible()
    {
        var h = new EngineHarness();
        h.Run(4000);
        Assert.Equal(0f, h.Peak(0, 4000));
        Assert.True(h.Engine.IsIdle);

        h.Engine.PreviewClick(MetronomeSound.Cowbell, accent: false);
        Assert.False(h.Engine.IsIdle);
        h.Run(4000);
        Assert.True(h.Peak(4000, 8000) > 0.05f);
    }
}

public class DrumEngineSongTests
{
    private static EngineHarness Song(bool intro = false, bool countIn = false)
    {
        var h = new EngineHarness();
        h.Engine.SetStyle(EngineHarness.Rock);
        h.Engine.ClickEnabled = false;
        h.Engine.IntroEnabled = intro;
        h.Engine.CountInEnabled = countIn;
        return h;
    }

    [Fact]
    public void Steps_follow_the_style_grid()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Bar);

        Assert.Equal(16, h.Events.Count);
        Assert.Equal(Enumerable.Range(0, 16), h.Events.Select(e => e.Step));
        Assert.All(h.Events, e => Assert.Equal(16, e.StepsInBar));
        Assert.All(h.Events, e => Assert.Same(EngineHarness.Rock.Parts[0].Main[0], e.Pattern));
        Assert.Equal(new[] { 0, 4, 8, 12 }, h.Events.Where(e => e.IsBeatStart).Select(e => e.Step));
        Assert.True(h.Peak(0, 2000) > 0.1f);   // kick + hat on the downbeat
    }

    [Fact]
    public void Main_groove_loops_through_its_bars()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Bar * 3);

        var part = EngineHarness.Rock.Parts[0];
        var patterns = h.Events.Where(e => e.Step == 0).Select(e => e.Pattern).ToList();
        Assert.Same(part.Main[0], patterns[0]);
        Assert.Same(part.Main[1], patterns[1]);
        Assert.Same(part.Main[0], patterns[2]);
    }

    [Fact]
    public void Count_in_then_intro_then_groove()
    {
        var h = Song(intro: true, countIn: true);
        h.Engine.Start();
        h.Run(EngineHarness.Bar * 3);

        var firstSteps = h.Events.Where(e => e.Step == 0).ToList();
        Assert.Equal(DrumSection.CountIn, firstSteps[0].Section);
        Assert.Equal(DrumSection.Intro, firstSteps[1].Section);
        Assert.Equal(DrumSection.Main, firstSteps[2].Section);
        Assert.Equal(0, firstSteps[0].Bar);
        Assert.Equal(0, firstSteps[1].Bar);
        Assert.Equal(1, firstSteps[2].Bar);
        Assert.Same(EngineHarness.Rock.Intro, firstSteps[1].Pattern);

        // Count-in: sticks on every beat.
        for (int beat = 0; beat < 4; beat++)
            Assert.True(h.Peak((long)beat * EngineHarness.Beat, (long)beat * EngineHarness.Beat + 600) > 0.05f);
    }

    [Fact]
    public void Fill_requested_mid_bar_takes_over_the_rest_of_the_bar()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Beat + 100);
        h.Engine.RequestFill();
        int before = h.Events.Count;
        h.Run(EngineHarness.Bar * 2 - EngineHarness.Beat - 100);

        var restOfBar = h.Events.Skip(before).Where(e => e.Bar == 1).ToList();
        Assert.NotEmpty(restOfBar);
        Assert.All(restOfBar, e => Assert.Equal(DrumSection.Fill, e.Section));
        Assert.All(restOfBar, e => Assert.Contains(e.Pattern, EngineHarness.Rock.Parts[0].Fills));

        var nextBar = h.Events.Where(e => e.Bar == 2).ToList();
        Assert.Equal(16, nextBar.Count);
        Assert.All(nextBar, e => Assert.Equal(DrumSection.Main, e.Section));
        Assert.All(nextBar, e => Assert.Equal(0, e.PartIndex));
    }

    [Fact]
    public void Fill_requested_on_the_last_beat_plays_over_the_next_bar()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Beat * 3 + EngineHarness.Beat / 2);
        h.Engine.RequestFill();
        h.Run(EngineHarness.Bar * 3 - (EngineHarness.Beat * 3 + EngineHarness.Beat / 2));

        var sections = h.SectionOfBar();
        Assert.All(h.Events.Where(e => e.Bar == 1), e => Assert.Equal(DrumSection.Main, e.Section));
        Assert.Equal(DrumSection.Fill, sections[2]);
        Assert.Equal(DrumSection.Main, sections[3]);
    }

    [Fact]
    public void Fills_rotate()
    {
        var h = Song();
        h.Engine.AutoFillEveryBars = 2;
        h.Engine.Start();
        h.Run(EngineHarness.Bar * 4);

        var fills = h.Events.Where(e => e.Step == 0 && e.Section == DrumSection.Fill).Select(e => e.Pattern).ToList();
        Assert.Equal(2, fills.Count);
        Assert.NotSame(fills[0], fills[1]);
    }

    [Fact]
    public void Auto_fill_plays_on_every_fourth_bar()
    {
        var h = Song();
        h.Engine.AutoFillEveryBars = 4;
        h.Engine.Start();
        h.Run(EngineHarness.Bar * 8);

        var sections = h.SectionOfBar();
        for (int bar = 1; bar <= 8; bar++)
            Assert.Equal(bar % 4 == 0 ? DrumSection.Fill : DrumSection.Main, sections[bar]);
    }

    [Fact]
    public void Next_part_plays_the_transition_then_switches()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Beat + 100);
        h.Engine.RequestNextPart();
        int before = h.Events.Count;
        h.Run(EngineHarness.Bar * 2 - EngineHarness.Beat - 100);

        var style = EngineHarness.Rock;
        var restOfBar = h.Events.Skip(before).Where(e => e.Bar == 1).ToList();
        Assert.All(restOfBar, e => Assert.Equal(DrumSection.Transition, e.Section));
        Assert.All(restOfBar, e => Assert.Same(style.Parts[0].Transition, e.Pattern));

        var nextBar = h.Events.Where(e => e.Bar == 2).ToList();
        Assert.All(nextBar, e => Assert.Equal(1, e.PartIndex));
        Assert.All(nextBar, e => Assert.Equal(DrumSection.Main, e.Section));
        Assert.Same(style.Parts[1].Main[0], nextBar[0].Pattern);
        Assert.Equal(1, h.Engine.PartIndex);
    }

    [Fact]
    public void Next_part_wraps_back_to_the_first_part()
    {
        var h = Song();
        h.Engine.Start();
        for (int i = 0; i < EngineHarness.Rock.Parts.Count; i++)
        {
            h.Run(EngineHarness.Beat);
            h.Engine.RequestNextPart();
            h.Run(EngineHarness.Bar - EngineHarness.Beat);
        }
        h.Run(EngineHarness.Beat);

        Assert.Equal(0, h.Engine.PartIndex);
        Assert.Equal(DrumSection.Main, h.Engine.Section);
    }

    [Fact]
    public void Outro_plays_the_ending_fill_then_a_final_hit_and_stops()
    {
        var h = Song();
        h.Engine.Start();
        h.Run(EngineHarness.Beat);
        h.Engine.RequestOutro();
        h.Run(EngineHarness.Bar * 2 - EngineHarness.Beat + 200);

        var outroBar = h.Events.Where(e => e.Section == DrumSection.Outro).ToList();
        Assert.Equal(16, outroBar.Count);
        Assert.All(outroBar, e => Assert.Same(EngineHarness.Rock.Outro, e.Pattern));

        var last = h.Events[^1];
        Assert.Equal(DrumSection.Stopped, last.Section);
        Assert.InRange(last.Frame, EngineHarness.Bar * 2 - 1, EngineHarness.Bar * 2 + 1);
        Assert.False(h.Engine.IsPlaying);

        // The ending hit (crash + kick) rings on the downbeat after the outro, then decays to idle.
        Assert.True(h.Peak(EngineHarness.Bar * 2, EngineHarness.Bar * 2 + 200) > 0.1f);
        h.Run(EngineHarness.SampleRate * 3);
        Assert.True(h.Engine.IsIdle);
    }

    [Fact]
    public void Switching_style_while_playing_waits_for_the_bar_line()
    {
        var h = new EngineHarness();
        h.Engine.Start();                       // click only
        h.Run(EngineHarness.Beat);
        h.Engine.SetStyle(EngineHarness.Rock);
        Assert.Same(EngineHarness.Rock, h.Engine.Style);
        h.Run(EngineHarness.Bar * 2 - EngineHarness.Beat);

        Assert.All(h.Events.Where(e => e.Frame < EngineHarness.Bar), e => Assert.Null(e.Pattern));
        var second = h.Events.Where(e => e.Frame >= EngineHarness.Bar).ToList();
        Assert.Equal(16, second.Count);
        Assert.All(second, e => Assert.Same(EngineHarness.Rock.Parts[0].Main[0], e.Pattern));
    }

    [Fact]
    public void Open_hat_is_choked_by_the_closed_hat()
    {
        static float TailAfterHit(bool choke)
        {
            var h = new EngineHarness();
            h.Engine.PreviewVoice(DrumVoice.OpenHat);
            h.Run(2000);
            if (choke) h.Engine.PreviewVoice(DrumVoice.PedalHat);
            h.Run(8000);
            return h.Peak(8000, 10000);        // pedal hat (60 ms) is over; only an unchoked open hat still rings
        }

        Assert.True(TailAfterHit(choke: false) > 0.01f);
        Assert.True(TailAfterHit(choke: true) < TailAfterHit(choke: false) * 0.25f);
    }

    public static IEnumerable<object[]> StyleIds => DrumStyleLibrary.All.Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(StyleIds))]
    public void Every_style_plays_through_without_clipping(string id)
    {
        var style = DrumStyleLibrary.Find(id)!;
        var engine = new DrumMachineEngine(new DrumKit(style.SuggestedKit))
        {
            Bpm = style.DefaultBpm,
            MasterVolume = 1f,
            Humanize = 0.1f,
            AutoFillEveryBars = 2,
            IntroEnabled = true,
        };
        engine.SetStyle(style);
        engine.Start();

        int framesPerBar = (int)(EngineHarness.SampleRate * 60.0 / style.DefaultBpm * style.Beats);
        var buffer = new float[1024];
        var sections = new HashSet<DrumSection>();
        float peak = 0;
        bool nextPartRequested = false, outroRequested = false;

        for (long frame = 0; frame < framesPerBar * 9L && (engine.IsPlaying || !engine.IsIdle); frame += 512)
        {
            if (!nextPartRequested && frame > framesPerBar * 4.3) { engine.RequestNextPart(); nextPartRequested = true; }
            if (!outroRequested && frame > framesPerBar * 6.3) { engine.RequestOutro(); outroRequested = true; }

            engine.Read(buffer, 0, buffer.Length);
            foreach (float v in buffer)
            {
                Assert.True(float.IsFinite(v), $"{id}: non-finite sample");
                peak = Math.Max(peak, Math.Abs(v));
            }
            while (engine.TryDequeueEvent(out var e)) sections.Add(e.Section);
        }

        Assert.True(peak < 1f, $"{id}: clipped ({peak})");
        Assert.True(peak > 0.2f, $"{id}: too quiet ({peak})");
        foreach (var expected in new[] { DrumSection.Intro, DrumSection.Main, DrumSection.Fill,
                                         DrumSection.Transition, DrumSection.Outro, DrumSection.Stopped })
            Assert.Contains(expected, sections);
        Assert.False(engine.IsPlaying);
    }
}

public class MetronomeSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubTests-" + Guid.NewGuid().ToString("N"));

    public MetronomeSettingsTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new MetronomeSettingsService(null, _folder).Load();
        Assert.Equal(120, settings.Bpm);
        Assert.False(settings.DrumsMode);
        Assert.True(settings.IntroFill);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var service = new MetronomeSettingsService(null, _folder);
        service.Save(new MetronomeSettings
        {
            Bpm = 97,
            DrumsMode = true,
            StyleId = "blues-shuffle",
            Kit = DrumKitKind.Jazz,
            ClickSound = MetronomeSound.Cowbell,
            Subdivision = ClickSubdivision.Triplets,
            Accents = new() { BeatAccent.Accent, BeatAccent.Mute, BeatAccent.Normal },
            AutoFillBars = 8,
            GapEnabled = true,
            GapMuteBars = 3,
        });

        var loaded = new MetronomeSettingsService(null, _folder).Load();
        Assert.Equal(97, loaded.Bpm);
        Assert.True(loaded.DrumsMode);
        Assert.Equal("blues-shuffle", loaded.StyleId);
        Assert.Equal(DrumKitKind.Jazz, loaded.Kit);
        Assert.Equal(MetronomeSound.Cowbell, loaded.ClickSound);
        Assert.Equal(ClickSubdivision.Triplets, loaded.Subdivision);
        Assert.Equal(new[] { BeatAccent.Accent, BeatAccent.Mute, BeatAccent.Normal }, loaded.Accents);
        Assert.Equal(8, loaded.AutoFillBars);
        Assert.True(loaded.GapEnabled);
        Assert.Equal(3, loaded.GapMuteBars);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        File.WriteAllText(Path.Combine(_folder, "metronome.json"),
            """{ "Bpm": 9999, "Volume": 7, "BeatsPerMeasure": 0, "AutoFillBars": -3, "Accents": null }""");

        var loaded = new MetronomeSettingsService(null, _folder).Load();
        Assert.Equal(280, loaded.Bpm);
        Assert.Equal(1.0, loaded.Volume);
        Assert.Equal(1, loaded.BeatsPerMeasure);
        Assert.Equal(0, loaded.AutoFillBars);
        Assert.NotNull(loaded.Accents);
    }

    [Fact]
    public void Corrupt_file_is_quarantined_not_overwritten()
    {
        var path = Path.Combine(_folder, "metronome.json");
        File.WriteAllText(path, "{ this is not json");

        var loaded = new MetronomeSettingsService(null, _folder).Load();

        Assert.Equal(120, loaded.Bpm);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_folder, "metronome.json.corrupt-*"));
    }
}

public class MetronomeServiceTests
{
    [Fact]
    public void Service_builds_headless_and_switches_kits_without_opening_audio()
    {
        using var service = new MetronomeService();
        Assert.Equal(DrumKitKind.Rock, service.KitKind);
        Assert.False(service.IsPlaying);

        service.SetKit(DrumKitKind.Jazz);
        Assert.Equal(DrumKitKind.Jazz, service.KitKind);

        // The engine is usable without a device: render a bar offline.
        service.Engine.Start();
        var buffer = new float[2048];
        Assert.Equal(buffer.Length, service.Engine.Read(buffer, 0, buffer.Length));
        Assert.True(service.Engine.IsPlaying);
        service.Engine.Stop();
    }
}

public class DrumSamplePackTests
{
    private static readonly DrumVoice[] Recorded =
    {
        DrumVoice.Kick, DrumVoice.Snare, DrumVoice.SideStick, DrumVoice.ClosedHat, DrumVoice.PedalHat,
        DrumVoice.OpenHat, DrumVoice.Ride, DrumVoice.RideBell, DrumVoice.Crash,
        DrumVoice.HighTom, DrumVoice.MidTom, DrumVoice.FloorTom,
    };

    [Fact]
    public void Pack_loads_with_every_recorded_drum_sorted_soft_to_loud()
    {
        var pack = DrumSamplePack.LoadEmbedded("acoustic");
        Assert.NotNull(pack);
        Assert.True(pack!.SampleRate >= 8000);

        foreach (var voice in Recorded)
        {
            Assert.True(pack.Voices.TryGetValue(voice, out var layers), $"{voice} is missing from the pack");
            Assert.True(layers!.Count >= 5, $"{voice} has only {layers.Count} hits");
            Assert.Equal(layers.Select(l => l.Loudness).OrderBy(x => x), layers.Select(l => l.Loudness));

            foreach (var layer in layers)
            {
                Assert.True(layer.Frames > 100, $"{voice}: empty hit");
                Assert.NotNull(layer.Right);
                Assert.Equal(layer.Frames, layer.Right!.Length);
                Assert.InRange(layer.Loudness, 0.0001f, 1f);
                Assert.All(layer.Left, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f, $"{voice}: left out of range"));
                Assert.All(layer.Right, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f, $"{voice}: right out of range"));
            }
        }
    }
}

public class AcousticKitTests
{
    [Fact]
    public void Recorded_voices_are_sampled_and_the_rest_stay_synthesized()
    {
        var kit = new DrumKit(DrumKitKind.Acoustic);
        foreach (var voice in new[]
                 {
                     DrumVoice.Kick, DrumVoice.Snare, DrumVoice.SideStick, DrumVoice.ClosedHat, DrumVoice.PedalHat,
                     DrumVoice.OpenHat, DrumVoice.Ride, DrumVoice.RideBell, DrumVoice.Crash,
                     DrumVoice.HighTom, DrumVoice.MidTom, DrumVoice.FloorTom,
                 })
        {
            Assert.True(kit.IsSampled(voice), $"{voice} should be recorded");
            Assert.True(kit.GetLayers(voice).Count >= 5);
        }

        foreach (var voice in new[] { DrumVoice.Clap, DrumVoice.Cowbell, DrumVoice.Tambourine, DrumVoice.Shaker, DrumVoice.Sticks })
        {
            Assert.False(kit.IsSampled(voice), $"{voice} should be synthesized");
            Assert.Empty(kit.GetLayers(voice));
        }

        // The synthesized kits never use recordings.
        var rock = new DrumKit(DrumKitKind.Rock);
        Assert.All(Enum.GetValues<DrumVoice>(), v => Assert.False(rock.IsSampled(v)));
    }

    [Fact]
    public void Acoustic_kit_is_deterministic()
    {
        var a = new DrumKit(DrumKitKind.Acoustic);
        var b = new DrumKit(DrumKitKind.Acoustic);
        foreach (var voice in Enum.GetValues<DrumVoice>())
            Assert.Equal(a.GetSample(voice), b.GetSample(voice));
    }

    [Fact]
    public void A_different_sample_rate_falls_back_to_synthesis()
    {
        var kit = new DrumKit(DrumKitKind.Acoustic, 48000);
        Assert.All(Enum.GetValues<DrumVoice>(), v => Assert.False(kit.IsSampled(v)));
        Assert.True(kit.GetSample(DrumVoice.Snare).Length > 100);
    }
}

public class AcousticEngineTests
{
    private static readonly DrumKit Real = new(DrumKitKind.Acoustic);

    private static EngineHarness Harness(string snareLine, double bpm = 120, float humanize = 0f)
    {
        var h = new EngineHarness(Real);
        h.Engine.Bpm = bpm;
        h.Engine.Humanize = humanize;
        h.Engine.ClickEnabled = false;
        h.Engine.IntroEnabled = false;
        h.Engine.SetStyle(EngineHarness.OneBarStyle(DrumBar.Parse(snareLine)));
        return h;
    }

    [Fact]
    public void Velocity_follows_the_recording()
    {
        // Ghost note on step 0, accent on step 8 (two beats later).
        var h = Harness("SD|g-------X-------|");
        h.Engine.Start();
        h.Run(EngineHarness.Bar);

        float ghost = h.Peak(0, 6000);
        float accent = h.Peak(EngineHarness.Beat * 2, EngineHarness.Beat * 2 + 6000);
        Assert.True(ghost > 0f);
        Assert.True(accent > ghost * 2f, $"accent {accent} should be more than twice the ghost {ghost}");
    }

    [Fact]
    public void Repeated_notes_are_not_identical()
    {
        var h = Harness("SD|x---x---x---x---|");
        h.Engine.Start();
        h.Run(EngineHarness.Bar);

        var windows = Enumerable.Range(0, 4)
            .Select(beat => h.Slice((long)beat * EngineHarness.Beat, 4096))
            .ToList();
        int distinct = windows.Select(w => string.Join(",", w)).Distinct().Count();
        Assert.True(distinct >= 2, "four equal snare hits rendered identically");
    }

    [Fact]
    public void Consecutive_hits_of_a_drum_use_different_recordings()
    {
        // Previews are full-velocity and let each hit ring out, so no tail of the last hit muddies the comparison.
        var h = new EngineHarness(Real);
        var windows = new List<float[]>();
        for (int i = 0; i < 4; i++)
        {
            long at = h.Engine.FramesRendered;
            h.Engine.PreviewVoice(DrumVoice.Snare);
            h.Run(EngineHarness.SampleRate * 3);
            windows.Add(h.Slice(at, 4096));
        }

        for (int i = 1; i < windows.Count; i++)
            Assert.False(windows[i].SequenceEqual(windows[i - 1]), $"hit {i + 1} repeated hit {i}");
    }

    [Fact]
    public void Hits_start_exactly_on_the_tick_without_humanize_and_a_few_ms_late_with_it()
    {
        // The recordings may begin with a few exactly-silent frames; that is the recording, not timing.
        var snare = Real.GetLayers(DrumVoice.Snare);
        long minLead = snare.Min(Lead), maxLead = snare.Max(Lead);

        // One snare per bar at 30 BPM (8 s bars): every hit has fully died away before the next tick.
        static List<long> Delays(float humanize)
        {
            var h = Harness("SD|X---------------|", bpm: 30, humanize: humanize);
            h.Engine.Start();
            h.Run(EngineHarness.SampleRate * 8 * 6);

            var delays = new List<long>();
            foreach (var tick in h.Events.Where(e => e.Step == 0))
            {
                long onset = h.FirstSound(tick.Frame, tick.Frame + EngineHarness.SampleRate);
                if (onset < 0) continue;     // the last bar may not have been rendered far enough
                delays.Add(onset - tick.Frame);
            }
            Assert.True(delays.Count >= 5, "snare is silent");
            return delays;
        }

        // Without Humanize the hit starts on the tick frame: the only offset is the recording's own silent lead-in.
        Assert.All(Delays(0f), d => Assert.InRange(d, minLead, maxLead));

        var humanized = Delays(0.08f);
        long max = (long)(0.0064 * EngineHarness.SampleRate) + 2;       // 6.4 ms
        Assert.All(humanized, d => Assert.InRange(d, minLead, max + maxLead));
        Assert.Contains(humanized, d => d > maxLead);                    // Random(1) makes this reproducible
    }

    /// <summary>Frames of exact silence at the start of a recorded hit.</summary>
    private static long Lead(DrumSampleLayer layer)
    {
        for (int i = 0; i < layer.Frames; i++)
            if (layer.Left[i] != 0f || (layer.Right != null && layer.Right[i] != 0f)) return i;
        return layer.Frames;
    }

    public static IEnumerable<object[]> StyleIds => DrumStyleLibrary.All.Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(StyleIds))]
    public void Every_style_plays_through_without_clipping_with_real_drums(string id)
    {
        var style = DrumStyleLibrary.Find(id)!;
        var engine = new DrumMachineEngine(Real)
        {
            Bpm = style.DefaultBpm,
            MasterVolume = 1f,
            Humanize = 0.1f,
            AutoFillEveryBars = 2,
            IntroEnabled = true,
        };
        engine.SetStyle(style);
        engine.Start();

        int framesPerBar = (int)(EngineHarness.SampleRate * 60.0 / style.DefaultBpm * style.Beats);
        var buffer = new float[1024];
        float peak = 0;
        bool nextPartRequested = false, outroRequested = false;

        for (long frame = 0; frame < framesPerBar * 9L && (engine.IsPlaying || !engine.IsIdle); frame += 512)
        {
            if (!nextPartRequested && frame > framesPerBar * 4.3) { engine.RequestNextPart(); nextPartRequested = true; }
            if (!outroRequested && frame > framesPerBar * 6.3) { engine.RequestOutro(); outroRequested = true; }

            engine.Read(buffer, 0, buffer.Length);
            foreach (float v in buffer)
            {
                Assert.True(float.IsFinite(v), $"{id}: non-finite sample");
                peak = Math.Max(peak, Math.Abs(v));
            }
            while (engine.TryDequeueEvent(out _)) { }
        }

        Assert.True(peak < 1f, $"{id}: clipped ({peak})");
        Assert.True(peak > 0.2f, $"{id}: too quiet ({peak})");
        Assert.False(engine.IsPlaying);
    }
}
