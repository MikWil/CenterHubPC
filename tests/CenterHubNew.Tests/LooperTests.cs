using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using NAudio.Wave;
using Xunit;

namespace CenterHubNew.Tests;

/// <summary>
/// The drum machine and the looper rendered offline, with synthetic guitar input. The engine plays no
/// sound of its own (click off, click-only style), so every non-zero output sample is the loop.
/// </summary>
internal sealed class LoopHarness
{
    public const int Rate = 44100;

    public readonly DrumMachineEngine Engine;
    public readonly LooperEngine Looper = new(Rate);
    public readonly int Beat;
    public readonly int Bar;

    /// <summary>What the player plays, by the engine frame at which it is heard.</summary>
    public Func<long, float> Signal = _ => 0f;

    /// <summary>Frames the capture is late against what is heard (input + output delay).</summary>
    public int InputDelay;

    /// <summary>Compensation applied to the capture stamps, like LooperService.LatencyMs.</summary>
    public int LatencyFrames;

    /// <summary>The capture delivers frames this long after the engine rendered them.</summary>
    public int CaptureLag = 1024;

    private readonly List<float> _left = new();
    private long _fed;

    public LoopHarness(double bpm = 120, int beatsPerBar = 2)
    {
        Engine = new DrumMachineEngine(EngineHarness.Kit, Rate) { Bpm = bpm, MasterVolume = 1f, ClickEnabled = false };
        Engine.SetStyle(null);
        Engine.SetClickBeats(beatsPerBar);
        Looper.AutoLevel = false;       // the timing tests use tiny impulses at unity; auto-level has its own tests
        Beat = (int)(Rate * 60.0 / bpm);
        Bar = Beat * beatsPerBar;
        Engine.AttachLooper(Looper);
    }

    public long Frame => Engine.FramesRendered;

    public static Func<long, float> Impulses(int period, int offset, float amplitude) =>
        h => h >= 0 && h % period == offset ? amplitude : 0f;

    public void Run(long frames, int block = 512)
    {
        var buffer = new float[block * 2];
        var input = new float[block];
        while (frames > 0)
        {
            int n = (int)Math.Min(block, frames);
            Assert.Equal(n * 2, Engine.Read(buffer, 0, n * 2));
            for (int i = 0; i < n; i++) _left.Add(buffer[i * 2]);
            frames -= n;

            // Feed the capture up to (rendered - lag): a sample is stamped with the frame it arrives at.
            long limit = Frame - CaptureLag;
            while (_fed < limit)
            {
                int m = (int)Math.Min(block, limit - _fed);
                for (int i = 0; i < m; i++) input[i] = Signal(_fed + i - InputDelay);
                Looper.WriteInput(_fed - LatencyFrames, input, 0, m);
                _fed += m;
            }
        }
    }

    public void RunTo(long frame) => Run(frame - Frame);

    public float Left(long frame) => _left[(int)frame];

    public List<long> NonZero(long from, long to)
    {
        var frames = new List<long>();
        for (long f = from; f < to && f < _left.Count; f++)
            if (Math.Abs(_left[(int)f]) > 1e-7f) frames.Add(f);
        return frames;
    }

    /// <summary>The frames in [from, to) at which the loop (length <paramref name="loop"/>, started at <paramref name="origin"/>) hits one of <paramref name="positions"/>.</summary>
    public static List<long> Expected(long from, long to, long origin, long loop, IEnumerable<int> positions)
    {
        var result = new List<long>();
        var pos = positions.ToList();
        for (long f = from; f < to; f++)
        {
            long p = (f - origin) % loop;
            if (p < 0) p += loop;
            if (pos.Contains((int)p)) result.Add(f);
        }
        return result;
    }

    public static int[] BeatPositions(int beat, int beats, int offset) =>
        Enumerable.Range(0, beats).Select(i => i * beat + offset).ToArray();
}

public class LooperLoopTests
{
    [Fact]
    public void Two_bars_play_back_at_the_same_bar_positions_for_several_cycles_without_drift()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);

        h.Looper.PressRecord(0);
        Assert.Equal(LooperState.Armed, h.Looper.State);
        h.Engine.Start();
        h.RunTo(h.Bar * 12);

        long loop = h.Bar * 2;
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(2, h.Looper.LoopBars);
        Assert.Equal(loop, h.Looper.LoopFrames);
        Assert.Equal(120.0, h.Looper.LoopBpm);
        Assert.Equal(1, h.Looper.LayerCount);
        Assert.False(h.Looper.CanUndo);

        // Nothing is heard while recording the first take.
        Assert.Empty(h.NonZero(0, loop));

        var expected = LoopHarness.Expected(loop, h.Bar * 12, 0, loop, LoopHarness.BeatPositions(h.Beat, 4, 1000));
        var actual = h.NonZero(loop, h.Bar * 12);
        Assert.Equal(expected, actual);
        Assert.All(actual, f => Assert.Equal(0.5f, h.Left(f), 5));
    }

    [Fact]
    public void Free_length_ends_at_the_next_bar_start()
    {
        var h = new LoopHarness();
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();

        h.RunTo(h.Bar * 2 + h.Bar / 2);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.Looper.PressRecord(h.Frame);                    // mid-bar: the take goes on until the bar line
        h.RunTo(h.Bar * 3 - 1);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(h.Bar * 3 + 10);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(3, h.Looper.LoopBars);
        Assert.Equal(h.Bar * 3, h.Looper.LoopFrames);
    }

    [Fact]
    public void A_free_take_is_at_least_one_bar()
    {
        var h = new LoopHarness();
        h.Looper.PressRecord(0);
        h.Engine.Start();

        h.RunTo(100);
        h.Looper.PressRecord(h.Frame);                    // right after the take began
        h.RunTo(h.Bar - 1);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(h.Bar + 10);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(1, h.Looper.LoopBars);
    }

    [Fact]
    public void A_fixed_length_ends_by_itself_and_the_pedal_does_not_cut_it_short()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 4;
        h.Looper.PressRecord(0);
        h.Engine.Start();

        h.RunTo(h.Bar + 100);
        h.Looper.PressRecord(h.Frame);                    // ignored: the length is fixed
        h.RunTo(h.Bar * 4 - 1);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(h.Bar * 4 + 10);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(4, h.Looper.LoopBars);
    }

    [Fact]
    public void The_take_starts_at_the_first_bar_after_pressing_Record_and_waits_for_the_engine()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Engine.Start();
        h.RunTo(h.Bar * 2 + 5000);
        h.Looper.PressRecord(h.Frame);                    // mid-bar
        Assert.Equal(LooperState.Armed, h.Looper.State);
        h.RunTo(h.Bar * 3 + 10);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(h.Bar * 8);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        // The loop begins at bar 3 (where the take started) and plays on from bar 4.
        var expected = LoopHarness.Expected(h.Bar * 4, h.Bar * 8, h.Bar * 3, h.Bar, LoopHarness.BeatPositions(h.Beat, 2, 1000));
        Assert.Equal(expected, h.NonZero(h.Bar * 4, h.Bar * 8));
    }

    [Fact]
    public void Compensation_puts_a_delayed_input_back_on_the_beat()
    {
        var h = new LoopHarness { InputDelay = 1500, LatencyFrames = 1500 };
        h.Looper.LengthBars = 2;
        // One hit on every beat, and one 600 frames before each beat so the last one lands in the take's tail.
        h.Signal = t => LoopHarness.Impulses(h.Beat, 1000, 0.5f)(t) + LoopHarness.Impulses(h.Beat, h.Beat - 600, 0.3f)(t);

        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 10);

        long loop = h.Bar * 2;
        var positions = LoopHarness.BeatPositions(h.Beat, 4, 1000).Concat(LoopHarness.BeatPositions(h.Beat, 4, h.Beat - 600));
        var expected = LoopHarness.Expected(loop, h.Bar * 10, 0, loop, positions);
        var actual = h.NonZero(loop, h.Bar * 10);
        Assert.Equal(expected, actual);

        // The last hit sits 600 frames before the loop end: it arrived after the take ended, and is there.
        Assert.Equal(0.3f, h.Left(loop + loop - 600), 5);
        Assert.Equal(0.3f, h.Left(loop * 3 - 600), 5);
    }

    [Fact]
    public void Without_compensation_the_hits_land_late_by_the_delay()
    {
        var h = new LoopHarness { InputDelay = 1500, LatencyFrames = 0 };
        h.Looper.LengthBars = 1;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);

        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 5);

        var expected = LoopHarness.Expected(h.Bar, h.Bar * 5, 0, h.Bar, LoopHarness.BeatPositions(h.Beat, 2, 2500));
        Assert.Equal(expected, h.NonZero(h.Bar, h.Bar * 5));
    }

    [Fact]
    public void Overdub_adds_a_layer_Undo_removes_only_it_and_Clear_empties()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Signal = LoopHarness.Impulses(h.Bar, 1000, 0.5f);

        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar + 500);
        h.Signal = LoopHarness.Impulses(h.Bar, 5000, 0.25f);
        h.RunTo(h.Bar + 2000);

        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Overdubbing, h.Looper.State);
        h.RunTo(h.Bar + 30000);
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(2, h.Looper.LayerCount);
        Assert.True(h.Looper.CanUndo);

        h.RunTo(h.Bar * 6);
        for (int k = 2; k < 6; k++)
        {
            Assert.Equal(new List<long> { h.Bar * k + 1000, h.Bar * k + 5000 }, h.NonZero(h.Bar * k, h.Bar * (k + 1)));
            Assert.Equal(0.5f, h.Left(h.Bar * k + 1000), 5);
            Assert.Equal(0.25f, h.Left(h.Bar * k + 5000), 5);
        }

        Assert.True(h.Looper.Undo());
        Assert.Equal(1, h.Looper.LayerCount);
        Assert.False(h.Looper.CanUndo);
        Assert.False(h.Looper.Undo());                    // the first take is never undone
        h.RunTo(h.Bar * 8);
        Assert.Equal(new List<long> { h.Bar * 7 + 1000 }, h.NonZero(h.Bar * 7, h.Bar * 8));

        h.Looper.Clear();
        Assert.Equal(LooperState.Empty, h.Looper.State);
        Assert.Equal(0, h.Looper.LayerCount);
        Assert.Equal(0, h.Looper.LoopBars);
        Assert.Equal(0.0, h.Looper.LoopBpm);
        h.RunTo(h.Bar * 10);
        Assert.Empty(h.NonZero(h.Bar * 8, h.Bar * 10));
    }

    [Fact]
    public void The_ninth_layer_merges_the_two_oldest_and_loses_nothing()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Signal = LoopHarness.Impulses(h.Bar, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();

        for (int i = 0; i < 9; i++)
        {
            long bar = h.Bar * (i + 1);
            h.RunTo(bar + 300);
            h.Signal = LoopHarness.Impulses(h.Bar, 2000 + 300 * i, 0.05f);
            h.RunTo(bar + 500);
            h.Looper.PressRecord(h.Frame);
            h.RunTo(bar + 30000);
            h.Looper.PressRecord(h.Frame);
            Assert.Equal(Math.Min(i + 2, LooperEngine.MaxLayers), h.Looper.LayerCount);
        }
        Assert.Equal(LooperEngine.MaxLayers, h.Looper.LayerCount);

        h.RunTo(h.Bar * 13);
        var positions = new List<int> { 1000 };
        for (int i = 0; i < 9; i++) positions.Add(2000 + 300 * i);
        long from = h.Bar * 11;
        var hits = h.NonZero(from, from + h.Bar);
        Assert.Equal(positions.Select(p => from + p).OrderBy(x => x).ToList(), hits);
        Assert.Equal(0.5f, h.Left(from + 1000), 5);
        Assert.Equal(0.05f, h.Left(from + 2000 + 300 * 8), 5);
    }

    [Fact]
    public void Stopped_then_Stop_again_resumes_in_phase_at_a_bar_start()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        long loop = h.Bar * 2;

        h.RunTo(h.Bar * 5 + 20000);
        h.Looper.PressStop(h.Frame);
        Assert.Equal(LooperState.Stopped, h.Looper.State);
        long stopped = h.Frame;

        h.RunTo(h.Bar * 8 + 10000);
        Assert.Empty(h.NonZero(stopped + 2700, h.Bar * 8 + 10000));     // silent after the 60 ms fade
        Assert.Equal(1, h.Looper.LayerCount);                            // but kept

        h.Looper.PressStop(h.Frame);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.RunTo(h.Bar * 9 - 1);
        Assert.Empty(h.NonZero(stopped + 2700, h.Bar * 9));             // waits for the bar line

        h.RunTo(h.Bar * 14);
        // Bar 9 is the loop's second bar: it resumes where the loop would be had it never stopped.
        var expected = LoopHarness.Expected(h.Bar * 9, h.Bar * 14, 0, loop, LoopHarness.BeatPositions(h.Beat, 4, 1000));
        Assert.Equal(expected, h.NonZero(h.Bar * 9, h.Bar * 14));
    }

    [Fact]
    public void With_auto_play_off_the_take_waits_silently_until_Play_then_starts_in_phase()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Looper.AutoPlay = false;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        long loop = h.Bar * 2;

        h.RunTo(h.Bar * 5 + 10000);
        Assert.Equal(LooperState.Stopped, h.Looper.State);     // recorded and kept…
        Assert.Equal(2, h.Looper.LoopBars);
        Assert.Equal(1, h.Looper.LayerCount);
        Assert.Empty(h.NonZero(0, h.Bar * 5 + 10000));         // …but never played by itself

        h.Looper.PressStop(h.Frame);                           // the Play loop pedal
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.RunTo(h.Bar * 10);
        Assert.Empty(h.NonZero(0, h.Bar * 6));                 // waits for the bar line
        var expected = LoopHarness.Expected(h.Bar * 6, h.Bar * 10, 0, loop, LoopHarness.BeatPositions(h.Beat, 4, 1000));
        Assert.Equal(expected, h.NonZero(h.Bar * 6, h.Bar * 10));
    }

    [Fact]
    public void A_clock_correction_of_a_frame_leaves_no_click_in_the_recording()
    {
        // A 220 Hz tone captured in 441-frame buffers; every 4th buffer arrives one frame early or
        // late (the capture clock nudging the placement). The recorded loop must stay a smooth sine.
        var h = new LoopHarness { CaptureLag = 100000 };   // the harness feeds nothing: this test feeds by hand
        h.Looper.LengthBars = 1;
        h.Looper.PressRecord(0);
        h.Engine.Start();

        const int buffer = 441;
        const double w = 2 * Math.PI * 220 / LoopHarness.Rate;
        var data = new float[buffer];
        long source = 0, stamp = 0;
        int n = 0;
        while (stamp < h.Bar + buffer * 4)
        {
            h.RunTo(Math.Max(h.Frame, stamp + buffer + 2000));
            for (int i = 0; i < buffer; i++) data[i] = 0.5f * (float)Math.Sin(w * (source + i));
            if (n % 4 == 3) stamp += n % 8 == 3 ? 1 : -1;   // the correction
            h.Looper.WriteInput(stamp, data, 0, buffer);
            source += buffer;
            stamp += buffer;
            n++;
        }
        h.RunTo(h.Bar * 2);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        var loop = h.Looper.RenderCycle()!;
        // Largest step of a clean 0.5-amplitude 220 Hz sine is 0.5·w ≈ 0.0157. A dropped or repeated
        // sample shows up as a step of twice that (or a flat spot followed by a double step).
        double maxStep = 0.5 * w;
        for (int i = 400; i < loop.Length - 400; i++)   // away from the seam fades
            Assert.True(Math.Abs(loop[i] - loop[i - 1]) <= maxStep * 1.25, $"step {Math.Abs(loop[i] - loop[i - 1]):F4} at {i}");
    }

    [Fact]
    public void Stopped_then_Record_overdubs_from_the_next_bar_start()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Signal = LoopHarness.Impulses(h.Bar, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 3 + 100);

        h.Looper.PressStop(h.Frame);
        h.RunTo(h.Bar * 3 + 20000);
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Overdubbing, h.Looper.State);
        h.Signal = LoopHarness.Impulses(h.Bar, 7000, 0.25f);

        h.RunTo(h.Bar * 4 + 500);
        // The new layer started with the loop at the bar line: it records the hit at 7000 of bar 4.
        h.RunTo(h.Bar * 5 + 20000);
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.RunTo(h.Bar * 8);

        Assert.Equal(new List<long> { h.Bar * 7 + 1000, h.Bar * 7 + 7000 }, h.NonZero(h.Bar * 7, h.Bar * 8));
    }

    [Fact]
    public void Stopping_and_restarting_the_engine_restarts_the_loop_at_bar_one()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        long loop = h.Bar * 2;

        h.RunTo(h.Bar * 5 + 20000);
        h.Engine.Stop();
        Assert.Equal(LooperState.Playing, h.Looper.State);   // stays as it was
        long stopped = h.Frame;
        h.RunTo(h.Bar * 7);
        Assert.Empty(h.NonZero(stopped + 2700, h.Bar * 7));

        long restart = h.Frame;
        h.Engine.Start();
        h.RunTo(restart + h.Bar * 6);

        var expected = LoopHarness.Expected(restart, restart + h.Bar * 6, restart, loop, LoopHarness.BeatPositions(h.Beat, 4, 1000));
        Assert.Equal(expected, h.NonZero(restart, restart + h.Bar * 6));
    }

    [Fact]
    public void Engine_stop_discards_a_take_and_ends_an_overdub_but_keeps_an_arming()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar + 1000);

        // Recording -> discarded
        h.Looper.LengthBars = 4;
        h.Looper.Clear();
        h.Looper.PressRecord(h.Frame);
        h.RunTo(h.Bar * 2 + 1000);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.Engine.Stop();
        Assert.Equal(LooperState.Empty, h.Looper.State);

        // Armed stays armed
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Armed, h.Looper.State);
        h.Engine.Stop();
        Assert.Equal(LooperState.Armed, h.Looper.State);
        h.Looper.PressStop(h.Frame);
        Assert.Equal(LooperState.Empty, h.Looper.State);

        // Overdubbing -> playing
        h.Looper.LengthBars = 1;
        h.Looper.PressRecord(h.Frame);
        h.Engine.Start();
        h.RunTo(h.Frame + h.Bar * 3);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Overdubbing, h.Looper.State);
        h.RunTo(h.Frame + 5000);
        h.Engine.Stop();
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(2, h.Looper.LayerCount);
    }

    [Fact]
    public void Pedal_cancels_an_arming_and_a_take()
    {
        var looper = new LooperEngine();
        looper.PressRecord(0);
        Assert.Equal(LooperState.Armed, looper.State);
        looper.PressRecord(0);
        Assert.Equal(LooperState.Empty, looper.State);

        looper.PressRecord(0);
        looper.PressStop(0);
        Assert.Equal(LooperState.Empty, looper.State);

        looper.PressRecord(0);
        looper.OnEngineStart(0);
        looper.OnBarStart(0, 120);
        Assert.Equal(LooperState.Recording, looper.State);
        looper.PressStop(10);                              // Stop during the first take discards it
        Assert.Equal(LooperState.Empty, looper.State);
        Assert.Equal(0, looper.LayerCount);

        looper.PressStop(0);                               // nothing to stop
        Assert.Equal(LooperState.Empty, looper.State);
    }

    [Fact]
    public void A_loop_never_gets_longer_than_sixty_seconds()
    {
        var h = new LoopHarness(bpm: 30, beatsPerBar: 4);      // 8 s bars
        h.Looper.LengthBars = 8;                                // 64 s asked for
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 8 + 100);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(7, h.Looper.LoopBars);                     // the last full bar before 60 s
        Assert.Equal(h.Bar * 7L, h.Looper.LoopFrames);
    }

    [Fact]
    public void An_attached_empty_looper_leaves_the_engine_output_bit_identical()
    {
        float[] Render(LooperEngine? looper, bool armed)
        {
            var engine = new DrumMachineEngine(EngineHarness.Kit) { Bpm = 120, Humanize = 0.1f };
            engine.SetStyle(EngineHarness.Rock);
            if (looper != null) engine.AttachLooper(looper);
            if (armed) looper!.PressRecord(0);
            engine.Start();
            var all = new List<float>();
            var buffer = new float[1024];
            for (int i = 0; i < 160; i++)
            {
                engine.Read(buffer, 0, buffer.Length);
                all.AddRange(buffer);
            }
            return all.ToArray();
        }

        var plain = Render(null, false);
        Assert.Contains(plain, s => s != 0f);
        Assert.Equal(plain, Render(new LooperEngine(), false));
        Assert.Equal(plain, Render(new LooperEngine(), true));  // armed and recording alone make no sound either
    }

    [Fact]
    public void The_loop_wrap_is_faded_so_a_constant_take_never_clicks()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Signal = t => t >= 0 ? 0.5f : 0f;
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 6);

        float worst = 0f;
        for (long f = h.Bar + 1; f < h.Bar * 6; f++)
            worst = Math.Max(worst, Math.Abs(h.Left(f) - h.Left(f - 1)));
        Assert.True(worst < 0.01f, $"largest step {worst}");
        Assert.True(h.Left(h.Bar * 3 - 1) < 0.01f);
        Assert.True(h.Left(h.Bar * 3) < 0.01f);
        Assert.Equal(0.5f, h.Left(h.Bar * 3 + h.Bar / 2), 5);
    }

    [Fact]
    public void Volume_and_master_volume_scale_the_loop()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 1;
        h.Looper.Volume = 0.5f;
        h.Engine.MasterVolume = 0.5f;
        h.Signal = LoopHarness.Impulses(h.Bar, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 4);
        Assert.Equal(0.125f, h.Left(h.Bar * 2 + 1000), 5);
    }
}

public class LooperCaptureClockTests
{
    [Fact]
    public void Consecutive_buffers_land_contiguously_despite_a_jittery_device_position()
    {
        var clock = new CaptureClock();
        var rng = new Random(7);
        const long truth = 123456;      // engine frame of capture sample 0
        const int buffer = 441;
        long total = 0, previousEnd = -1, lastStart = 0;
        int placed = 0, slips = 0;

        for (int i = 0; i < 600; i++)
        {
            total += buffer;
            long audible = truth + total + rng.Next(-300, 301);
            long start = clock.Stamp(audible, total, buffer);
            if (start == CaptureClock.NotReady) { Assert.Equal(0, placed); continue; }   // only at the very start
            if (placed++ > 0)
            {
                // While settling (the first ~2 s) it glides a few frames per buffer; afterwards single frames, rarely.
                Assert.InRange(start - previousEnd, -6, 6);
                if (i > 210)
                {
                    Assert.InRange(start - previousEnd, -1, 1);
                    if (start != previousEnd) slips++;
                }
            }
            previousEnd = start + buffer;
            lastStart = start;
        }

        Assert.InRange(600 - placed, 1, 10);                              // a short warm-up, then every buffer
        // Jitter must not be chased: each slip is a dropped/repeated sample (the "robotic" loop bug).
        Assert.True(slips <= placed / 8, $"{slips} slips in {placed} buffers");
        Assert.InRange(lastStart - (truth + total - buffer), -150, 150);  // within ~3 ms even with ±7 ms jitter
    }

    [Fact]
    public void A_steady_device_clock_gives_no_slips_at_all_and_exact_placement()
    {
        var clock = new CaptureClock();
        long total = 0, previousEnd = -1;
        int placed = 0;
        for (int i = 0; i < 400; i++)
        {
            total += 441;
            long start = clock.Stamp(1000 + total + (i % 2 == 0 ? 5 : -5), total, 441);   // ±5 frames of jitter
            if (start == CaptureClock.NotReady) continue;
            if (placed++ > 0) Assert.Equal(previousEnd, start);
            previousEnd = start + 441;
        }
        Assert.InRange(previousEnd - (1000 + total), -6, 6);
    }

    [Fact]
    public void Drift_between_the_two_devices_is_followed_with_rare_single_frame_slips()
    {
        var clock = new CaptureClock();
        long total = 0, previousEnd = -1;
        int placed = 0, slips = 0;
        for (int i = 0; i < 3000; i++)   // 30 s; the output runs 100 ppm fast: 4.4 frames a second
        {
            total += 441;
            long start = clock.Stamp(1000 + total + (long)(total * 0.0001), total, 441);
            if (start == CaptureClock.NotReady) continue;
            if (placed++ > 0 && start != previousEnd)
            {
                slips++;
                Assert.InRange(start - previousEnd, i > 210 ? -1 : -6, i > 210 ? 1 : 6);   // glides while settling
            }
            previousEnd = start + 441;
        }
        Assert.InRange(slips, 60, 180);                                              // ≈ 132 frames of drift
        Assert.InRange(previousEnd - (1000 + total + (long)(total * 0.0001)), -60, 60);
    }

    [Fact]
    public void A_big_jump_warms_up_again_and_a_reset_forgets_the_estimate()
    {
        var clock = new CaptureClock();
        long total = 0;
        for (int i = 0; i < 100; i++)
        {
            total += 441;
            clock.Stamp(1000 + total, total, 441);
        }

        // The output clock jumped 20000 frames (the device was reopened): measure again, then place.
        long start = CaptureClock.NotReady;
        int skipped = 0;
        while (start == CaptureClock.NotReady)
        {
            total += 441;
            start = clock.Stamp(1000 + total + 20000, total, 441);
            if (start == CaptureClock.NotReady) skipped++;
        }
        Assert.InRange(skipped, 1, 10);
        Assert.Equal(1000 + 20000 + total - 441, start);

        clock.Reset();
        total += 441;
        Assert.Equal(CaptureClock.NotReady, clock.Stamp(5000 + total, total, 441));
    }
}

public class LooperCalibrationTests
{
    private const int Rate = 44100;

    private static float[] Strums(int length, IEnumerable<long> clicks, double delayMs, float amplitude = 0.6f)
    {
        var input = new float[length];
        foreach (long c in clicks)
        {
            long at = c + (long)Math.Round(delayMs * Rate / 1000.0);
            for (int i = 0; i < 4000 && at + i < length; i++)
                input[at + i] = amplitude * MathF.Exp(-i / 600f) * MathF.Sin(i * 0.3f + 1.2f);
        }
        return input;
    }

    private static List<long> Clicks(long first = 10000, int count = 8) =>
        Enumerable.Range(0, count).Select(i => first + i * 26460L).ToList();

    [Fact]
    public void Strums_37_ms_after_the_clicks_measure_as_37_ms()
    {
        var clicks = Clicks();
        var input = Strums(300000, clicks, 37);
        var ms = LooperEngine.MeasureLatencyMs(input, 0, clicks, Rate);
        Assert.NotNull(ms);
        Assert.InRange(ms!.Value, 36, 38);
    }

    [Fact]
    public void The_median_shrugs_off_a_player_who_is_off_the_beat_now_and_then()
    {
        var clicks = Clicks();
        var input = new float[300000];
        double[] delays = { 40, 41, 39, 120, 40, 38, 5, 40 };
        for (int i = 0; i < clicks.Count; i++)
        {
            var one = Strums(300000, new[] { clicks[i] }, delays[i]);
            for (int j = 0; j < input.Length; j++) input[j] += one[j];
        }
        var ms = LooperEngine.MeasureLatencyMs(input, 0, clicks, Rate);
        Assert.InRange(ms!.Value, 38, 42);
    }

    [Fact]
    public void An_offset_input_buffer_is_read_at_the_right_frames()
    {
        var clicks = Clicks(first: 500000);
        var full = Strums(900000, clicks, 80);
        var part = full.AsSpan(400000).ToArray();
        var ms = LooperEngine.MeasureLatencyMs(part, 400000, clicks, Rate);
        Assert.InRange(ms!.Value, 79, 81);
    }

    [Fact]
    public void Too_few_usable_strums_or_a_silent_input_measure_nothing()
    {
        var clicks = Clicks();
        Assert.Null(LooperEngine.MeasureLatencyMs(Strums(300000, clicks.Take(3), 37), 0, clicks, Rate));
        Assert.Null(LooperEngine.MeasureLatencyMs(new float[300000], 0, clicks, Rate));
        Assert.Null(LooperEngine.MeasureLatencyMs(Strums(300000, clicks, 37, amplitude: 0.004f), 0, clicks, Rate));  // below the noise floor
    }

    [Fact]
    public void A_strum_right_on_the_click_measures_zero()
    {
        var clicks = Clicks();
        Assert.Equal(0, LooperEngine.MeasureLatencyMs(Strums(300000, clicks, 0), 0, clicks, Rate));
    }
}

public class LooperExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "centerhub-looper-" + Guid.NewGuid().ToString("N"));

    public LooperExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Export_writes_exactly_one_loop_as_16_bit_stereo()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar * 4);

        string path = Path.Combine(_folder, "loop.wav");
        Assert.True(h.Looper.ExportWav(path));

        using var reader = new WaveFileReader(path);
        Assert.Equal(44100, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(h.Bar * 2L * 4, reader.Length);

        var bytes = new byte[reader.Length];
        Assert.Equal(bytes.Length, reader.Read(bytes, 0, bytes.Length));
        short left = BitConverter.ToInt16(bytes, 1000 * 4);
        short right = BitConverter.ToInt16(bytes, 1000 * 4 + 2);
        Assert.InRange(left, 16383, 16385);          // 0.5 of full scale
        Assert.Equal(left, right);
        Assert.Equal(0, BitConverter.ToInt16(bytes, 2000 * 4));
    }

    [Fact]
    public void Export_of_an_empty_loop_writes_nothing()
    {
        var looper = new LooperEngine();
        string path = Path.Combine(_folder, "empty.wav");
        Assert.False(looper.ExportWav(path));
        Assert.False(File.Exists(path));
    }
}

internal static class LoopHarnessExtensions
{
    /// <summary>Arms a free take (drums off) with a count-in of <paramref name="countInBeats"/> clicks; returns the frame the take starts at.</summary>
    public static long ArmFree(this LoopHarness h, int countInBeats, int beatsPerBar = 2, long fixedFrames = 0, int fixedBars = 0)
    {
        long now = h.Frame;
        long start = now;
        if (countInBeats > 0)
        {
            long first = h.Engine.ScheduleCountIn(countInBeats, beatsPerBar, h.Beat);
            start = first + (long)countInBeats * h.Beat;
        }
        h.Looper.PressRecordFree(now, start, fixedFrames, 120, fixedBars);
        return start;
    }

    public static float Peak(this LoopHarness h, long from, long to)
    {
        float peak = 0f;
        for (long f = from; f < to; f++) peak = Math.Max(peak, Math.Abs(h.Left(f)));
        return peak;
    }
}

public class LooperFreeModeTests
{
    [Fact]
    public void Count_in_clicks_land_on_the_beats_and_the_take_starts_right_after_them()
    {
        var h = new LoopHarness();
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        long start = h.ArmFree(countInBeats: 2, beatsPerBar: 2, fixedFrames: 2L * h.Beat, fixedBars: 2);
        Assert.Equal(2L * h.Beat, start);                       // first click at frame 0, the downbeat after two beats
        Assert.Equal(LooperState.Armed, h.Looper.State);

        h.RunTo(start - 1);
        Assert.Equal(LooperState.Armed, h.Looper.State);
        h.RunTo(start + 1000);
        Assert.Equal(LooperState.Recording, h.Looper.State);

        long end = start + 2L * h.Beat;
        h.RunTo(end * 4);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.True(h.Looper.IsFreeLoop);
        Assert.Equal(2, h.Looper.LoopBars);
        Assert.Equal(2L * h.Beat, h.Looper.LoopFrames);

        // Two clicks, one per beat, sample-exact (same offset into each), the first one accented.
        long c0 = h.NonZero(0, 3000)[0];
        long c1 = h.NonZero(h.Beat, h.Beat + 3000)[0] - h.Beat;
        Assert.InRange(c0, 0, 3);
        Assert.Equal(c0, c1);
        Assert.True(h.Peak(0, 3000) > h.Peak(h.Beat, h.Beat + 3000));
        Assert.Empty(h.NonZero(start + 25000, end));            // no third click, and no loop yet

        // The loop is the take: hits at 1000 and 1000 + Beat, cycling by its own length.
        var expected = LoopHarness.Expected(end, end * 4, end, 2L * h.Beat, new[] { 1000, 1000 + h.Beat });
        Assert.Equal(expected, h.NonZero(end, end * 4));
    }

    [Fact]
    public void Without_a_count_in_the_take_starts_at_once_and_a_free_take_ends_at_the_frame_of_the_press()
    {
        var h = new LoopHarness();
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        long start = h.ArmFree(0);
        Assert.Equal(0, start);

        h.RunTo(10000);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(50000);
        h.Looper.PressRecord(50000);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(50000, h.Looper.LoopFrames);
        Assert.Equal(0, h.Looper.LoopBars);                     // a free-length take has no bar count
        Assert.Equal(120.0, h.Looper.LoopBpm);

        h.RunTo(50000 * 8);
        var expected = LoopHarness.Expected(50000, 50000 * 8, 50000, 50000, new[] { 1000, 1000 + h.Beat, 1000 + 2 * h.Beat });
        Assert.Equal(expected, h.NonZero(50000, 50000 * 8));
    }

    [Fact]
    public void A_fixed_length_ends_by_itself_on_the_exact_frame_even_inside_a_buffer()
    {
        var h = new LoopHarness();
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        long fixedFrames = 3L * h.Beat;                         // 66150: not a multiple of the 512-frame blocks
        h.ArmFree(0, fixedFrames: fixedFrames);

        h.RunTo(fixedFrames / 2);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        Assert.Equal(0.5, h.Looper.PositionAt(h.Frame), 0.01);  // progress towards the target
        h.RunTo(fixedFrames - 10);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(fixedFrames + 600);

        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(fixedFrames, h.Looper.LoopFrames);
        h.RunTo(fixedFrames * 7);
        var expected = LoopHarness.Expected(fixedFrames, fixedFrames * 7, fixedFrames, fixedFrames, new[] { 1000, 1000 + h.Beat, 1000 + 2 * h.Beat });
        Assert.Equal(expected, h.NonZero(fixedFrames, fixedFrames * 7));
    }
}

public class LooperFreeModeLifecycleTests
{
    private static readonly int[] ThreeBeats = { 1000, 23050, 45100 };

    [Fact]
    public void Stop_then_Stop_again_restarts_a_free_loop_from_its_start_at_once()
    {
        var h = new LoopHarness();
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        long len = 3L * h.Beat;
        h.ArmFree(0, fixedFrames: len);
        h.RunTo(len * 3 + 5000);

        h.Looper.PressStop(h.Frame);
        Assert.Equal(LooperState.Stopped, h.Looper.State);
        long stopped = h.Frame;
        h.RunTo(stopped + 30000);
        Assert.Empty(h.NonZero(stopped + 2700, h.Frame));       // silent after the 60 ms fade
        Assert.Equal(1, h.Looper.LayerCount);                    // but kept

        long restart = h.Frame;
        h.Looper.PressStop(restart);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.RunTo(restart + len * 4);
        var expected = LoopHarness.Expected(restart, restart + len * 4, restart, len, ThreeBeats);
        Assert.Equal(expected, h.NonZero(restart, restart + len * 4));
    }

    [Fact]
    public void Overdub_and_Undo_work_on_a_free_loop()
    {
        var h = new LoopHarness();
        long len = 2L * h.Beat;
        h.Signal = LoopHarness.Impulses((int)len, 1000, 0.5f);
        h.ArmFree(0, fixedFrames: len);
        h.RunTo(len + 500);
        h.Signal = LoopHarness.Impulses((int)len, 5000, 0.25f);
        h.RunTo(len + 2000);

        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Overdubbing, h.Looper.State);
        h.RunTo(len + 30000);
        h.Looper.PressRecord(h.Frame);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(2, h.Looper.LayerCount);

        h.RunTo(len * 6);
        for (int k = 2; k < 6; k++)
            Assert.Equal(new List<long> { len * k + 1000, len * k + 5000 }, h.NonZero(len * k, len * (k + 1)));

        Assert.True(h.Looper.Undo());
        h.RunTo(len * 8);
        Assert.Equal(new List<long> { len * 7 + 1000 }, h.NonZero(len * 7, len * 8));
        Assert.False(h.Looper.Undo());
    }

    [Fact]
    public void A_free_loop_restarts_at_the_first_bar_when_the_drums_start_then_ignores_bars_and_survives_their_stop()
    {
        var h = new LoopHarness();                               // 44100-frame bars
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        long len = 3L * h.Beat;                                  // 66150: not a whole number of bars
        h.ArmFree(0, fixedFrames: len);
        h.RunTo(len * 2 + 7000);
        Assert.False(h.Engine.IsIdle);                           // a free loop keeps the output open

        long s0 = h.Frame;
        h.Engine.Start();
        h.RunTo(s0 + len * 2 + 12345);
        h.Engine.Stop();                                         // a free loop does not depend on the drums
        Assert.Equal(LooperState.Playing, h.Looper.State);
        h.RunTo(s0 + len * 6);

        var expected = LoopHarness.Expected(s0, s0 + len * 6, s0, len, ThreeBeats);
        Assert.Equal(expected, h.NonZero(s0, s0 + len * 6));
    }

    [Fact]
    public void Cancelling_during_the_count_in_stops_the_clicks_and_never_records()
    {
        var h = new LoopHarness();
        h.ArmFree(countInBeats: 4);
        h.RunTo(h.Beat + 3000);
        Assert.Equal(LooperState.Armed, h.Looper.State);

        h.Looper.PressRecord(h.Frame);
        h.Engine.CancelCountIn();
        long cancelled = h.Frame;
        Assert.Equal(LooperState.Empty, h.Looper.State);

        h.RunTo(cancelled + 6L * h.Beat);
        Assert.Empty(h.NonZero(cancelled + 500, h.Frame));       // the click that was sounding fades in 5 ms
        Assert.Equal(LooperState.Empty, h.Looper.State);
        Assert.Equal(0, h.Looper.LayerCount);
    }

    [Fact]
    public void A_very_short_press_is_ignored_and_Stop_discards_the_take()
    {
        var h = new LoopHarness();
        h.ArmFree(0);
        h.RunTo(5000);
        h.Looper.PressRecord(5000);                              // 113 ms: keep recording
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.RunTo(12000);
        h.Looper.PressRecord(12000);                             // 272 ms: ends the take
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(12000, h.Looper.LoopFrames);

        h.Looper.Clear();
        h.ArmFree(0);
        h.RunTo(h.Frame + 20000);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        h.Looper.PressStop(h.Frame);
        Assert.Equal(LooperState.Empty, h.Looper.State);
        Assert.Equal(0, h.Looper.LayerCount);
    }

    [Fact]
    public void A_free_take_closes_itself_at_sixty_seconds()
    {
        var h = new LoopHarness();
        h.ArmFree(0);
        h.RunTo(60L * LoopHarness.Rate + 1000);
        Assert.Equal(LooperState.Playing, h.Looper.State);
        Assert.Equal(60L * LoopHarness.Rate, h.Looper.LoopFrames);
    }

    [Fact]
    public void The_output_is_held_open_for_a_free_take_and_a_free_loop_but_not_for_a_stopped_one()
    {
        var h = new LoopHarness();
        Assert.False(h.Looper.IsBusy);
        h.ArmFree(0, fixedFrames: h.Beat);
        Assert.True(h.Looper.IsBusy);                            // armed
        h.RunTo(2000);
        Assert.True(h.Looper.IsBusy);                            // recording
        h.RunTo(h.Beat + 2000);
        Assert.True(h.Looper.IsBusy);                            // playing
        h.Looper.PressStop(h.Frame);
        h.RunTo(h.Frame + 5000);
        Assert.False(h.Looper.IsBusy);                           // stopped, faded out
    }

    [Fact]
    public void A_bar_synced_fixed_take_reports_its_progress()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Looper.PressRecord(0);
        h.Engine.Start();
        h.RunTo(h.Bar);
        Assert.Equal(LooperState.Recording, h.Looper.State);
        Assert.Equal(0.5, h.Looper.PositionAt(h.Bar), 0.01);
        Assert.Equal(0.25, h.Looper.PositionAt(h.Bar / 2), 0.01);
    }
}

public class LooperTrimTests : IDisposable
{
    private const int Beat = 22050;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "centerhub-looper-trim-" + Guid.NewGuid().ToString("N"));

    public LooperTrimTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    // A free loop of `frames` frames, playing, with the run position a few cycles in.
    private static LoopHarness FreeLoop(Func<long, float> signal, long frames, bool autoLevel = false)
    {
        var h = new LoopHarness();
        h.Looper.AutoLevel = autoLevel;
        h.Signal = signal;
        h.ArmFree(0, fixedFrames: frames);
        h.RunTo(frames * 3 + 10000);
        return h;
    }

    [Fact]
    public void Trimming_the_start_plays_the_rest_and_the_loop_is_shorter()
    {
        var h = FreeLoop(LoopHarness.Impulses(Beat, 1000, 0.5f), 44100);
        long len = 44100;
        long t = h.Frame;
        h.Looper.SetTrim(100, 0);

        Assert.True(h.Looper.IsFreeLoop);
        Assert.Equal(0.9, h.Looper.LoopSeconds, 3);
        Assert.Equal(1.0, h.Looper.RecordedSeconds, 3);
        Assert.Equal(100, h.Looper.TrimStartMs, 1);
        Assert.Equal(0, h.Looper.TrimEndMs, 1);

        long shorter = len - 4410;
        h.RunTo(t + 13050 + shorter * 5 + 4000);
        // The hit at 1000 is cut away; the one at 23050 goes on from where it was, then repeats by the shorter length.
        var expected = Enumerable.Range(0, 6).Select(k => t + 13050 + shorter * k).ToList();
        Assert.Equal(expected, h.NonZero(t + 2000, h.Frame));
    }

    [Fact]
    public void A_trimmed_constant_take_never_clicks_at_the_cut_edges_or_while_the_trim_changes()
    {
        var h = FreeLoop(t => t >= 0 ? 0.5f : 0f, 44100);
        long t0 = h.Frame;
        h.Looper.SetTrim(100, 200);
        h.RunTo(t0 + 30870L * 6);

        float worst = 0f;
        for (long f = t0 + 1; f < h.Frame; f++) worst = Math.Max(worst, Math.Abs(h.Left(f) - h.Left(f - 1)));
        Assert.True(worst < 0.01f, $"largest step {worst}");
        Assert.Equal(0.5f, h.Left(t0 + 15000), 5);

        h.Looper.SetTrim(50, 0);                                 // change it again, mid-play
        long t1 = h.Frame;
        h.RunTo(t1 + 40000 * 3);
        worst = 0f;
        for (long f = t1 + 1; f < h.Frame; f++) worst = Math.Max(worst, Math.Abs(h.Left(f) - h.Left(f - 1)));
        Assert.True(worst < 0.01f, $"largest step {worst}");
    }

    [Fact]
    public void Trim_is_clamped_so_at_least_100_ms_remain_and_Reset_restores_everything()
    {
        var h = FreeLoop(LoopHarness.Impulses(Beat, 1000, 0.5f), 44100);
        h.Looper.SetTrim(900, 900);
        Assert.InRange(h.Looper.LoopSeconds, 0.0999, 0.1001);
        h.Looper.SetTrim(-5, 99999);
        Assert.Equal(0, h.Looper.TrimStartMs, 1);
        Assert.InRange(h.Looper.LoopSeconds, 0.0999, 0.1001);

        h.Looper.ResetTrim();
        Assert.Equal(1.0, h.Looper.LoopSeconds, 3);
        Assert.Equal(0, h.Looper.TrimEndMs, 1);
    }

    [Fact]
    public void Trimming_a_bar_synced_loop_frees_it_and_Reset_syncs_it_again_from_the_next_bar()
    {
        var h = new LoopHarness();
        h.Looper.LengthBars = 2;
        h.Signal = LoopHarness.Impulses(h.Beat, 1000, 0.5f);
        h.Looper.PressRecord(0);
        h.Engine.Start();
        long loop = h.Bar * 2;                                   // 88200
        h.RunTo(loop * 4 + 10000);
        Assert.False(h.Looper.IsFreeLoop);

        long t0 = h.Frame;
        h.Looper.SetTrim(100, 0);
        Assert.True(h.Looper.IsFreeLoop);
        Assert.Equal(2, h.Looper.LoopBars);

        long shorter = loop - 4410;
        h.RunTo(t0 + shorter * 4);
        // Free: no re-alignment at the bar lines, so the hits repeat by the shorter length and drift against the bars.
        var hits = h.NonZero(t0 + 2000, h.Frame);
        var set = new HashSet<long>(hits);
        Assert.All(hits.Where(f => f + shorter < h.Frame), f => Assert.Contains(f + shorter, set));
        var barPositions = new[] { 1000, 23050, 45100, 67150 };
        Assert.Contains(hits, f => !barPositions.Contains((int)(f % loop)));

        long t1 = h.Frame;
        h.Looper.ResetTrim();
        Assert.False(h.Looper.IsFreeLoop);
        h.RunTo(t1 + loop * 3);
        var expected = LoopHarness.Expected(t1 + loop * 2, t1 + loop * 3, 0, loop, barPositions);
        Assert.Equal(expected, h.NonZero(t1 + loop * 2, t1 + loop * 3));
    }

    [Fact]
    public void Export_writes_the_trimmed_loop()
    {
        var h = FreeLoop(LoopHarness.Impulses(Beat, 1000, 0.5f), 44100);
        h.Looper.SetTrim(100, 200);
        string path = Path.Combine(_folder, "trimmed.wav");
        Assert.True(h.Looper.ExportWav(path));

        using var reader = new WaveFileReader(path);
        long frames = 44100 - 4410 - 8820;
        Assert.Equal(frames * 4, reader.Length);
        var bytes = new byte[reader.Length];
        Assert.Equal(bytes.Length, reader.Read(bytes, 0, bytes.Length));
        Assert.InRange(BitConverter.ToInt16(bytes, (23050 - 4410) * 4), 16383, 16385);   // the hit at 23050 moved by the start trim
        Assert.Equal(0, BitConverter.ToInt16(bytes, 1000 * 4));
    }

    [Fact]
    public void Waveform_is_the_untrimmed_take_after_the_gain_and_Clear_empties_it()
    {
        var h = FreeLoop(t => t >= 0 && t < 22050 ? 0.2f : 0.05f, 44100, autoLevel: true);
        float gain = h.Looper.LoopGain;
        Assert.InRange(gain, 2.10, 2.18);                        // 0.6 / (0.2 * 1.4)

        var wave = h.Looper.GetWaveform(4);
        Assert.Equal(4, wave.Length);
        Assert.Equal(0.2f * gain, wave[0], 3);
        Assert.Equal(0.2f * gain, wave[1], 3);
        Assert.Equal(0.05f * gain, wave[2], 3);
        Assert.Equal(0.05f * gain, wave[3], 3);

        h.Looper.SetTrim(300, 300);                              // the picture stays the whole take
        Assert.Equal(wave, h.Looper.GetWaveform(4));
        Assert.True(h.Looper.GetWaveform(100).All(v => v <= 1f));

        h.Looper.Clear();
        Assert.Empty(h.Looper.GetWaveform(4));
        Assert.Equal(1f, h.Looper.LoopGain);
    }
}

public class LooperAutoLevelTests
{
    private static LoopHarness Take(float amplitude, bool autoLevel = true)
    {
        var h = new LoopHarness();
        h.Looper.AutoLevel = autoLevel;
        h.Signal = t => t >= 0 ? (float)(amplitude * Math.Sin(2 * Math.PI * 220 * t / 44100.0)) : 0f;
        h.ArmFree(0, fixedFrames: 44100);
        h.RunTo(44100 * 4);
        return h;
    }

    [Fact]
    public void A_quiet_take_is_raised_to_about_06()
    {
        var h = Take(0.05f);
        Assert.InRange(h.Looper.LoopGain, 11f, 13f);
        Assert.InRange(h.Peak(44100 * 2, 44100 * 3), 0.56f, 0.64f);
    }

    [Fact]
    public void The_gain_is_capped_at_16()
    {
        var h = Take(0.01f);
        Assert.Equal(16f, h.Looper.LoopGain);
        Assert.InRange(h.Peak(44100 * 2, 44100 * 3), 0.155f, 0.165f);
    }

    [Fact]
    public void A_loud_take_is_left_alone_and_auto_level_off_means_unity()
    {
        var loud = Take(0.7f);
        Assert.Equal(1f, loud.Looper.LoopGain);
        Assert.InRange(loud.Peak(44100 * 2, 44100 * 3), 0.69f, 0.71f);

        var off = Take(0.05f, autoLevel: false);
        Assert.Equal(1f, off.Looper.LoopGain);
        Assert.InRange(off.Peak(44100 * 2, 44100 * 3), 0.049f, 0.051f);

        off.Looper.AutoLevel = true;                             // switching it on later levels the existing loop
        Assert.InRange(off.Looper.LoopGain, 11f, 13f);
    }

    [Fact]
    public void An_overdub_that_would_clip_lowers_the_gain_and_Undo_does_not_break_the_level()
    {
        var h = Take(0.05f);
        float first = h.Looper.LoopGain;
        long len = 44100;

        h.RunTo(len * 4 + 2000);
        h.Looper.PressRecord(h.Frame);                           // overdub the same (in phase) sine for 30000 frames
        h.RunTo(len * 4 + 32000);
        h.Looper.PressRecord(h.Frame);
        h.RunTo(len * 6);

        // The sum of the two layers would sit at ~1.2: the check lowers the gain (it runs off the capture thread).
        for (int i = 0; i < 300 && h.Looper.LoopGain > 8f; i++) Thread.Sleep(10);
        float lowered = h.Looper.LoopGain;
        Assert.True(lowered < first * 0.6f, $"{first} -> {lowered}");

        Assert.True(h.Looper.Undo());
        Assert.InRange(h.Looper.LoopGain, lowered - 0.01f, lowered + 0.01f);   // Undo never raises it
        h.RunTo(len * 8);
        float peak = h.Peak(len * 7, len * 8);
        Assert.InRange(peak, 0.2f, 0.9f);                        // still a healthy, unclipped level
    }
}

public class LooperDeviceMatchTests
{
    private static AudioDeviceInfo Dev(string name) => new() { Id = name, Name = name, IsCapture = true };

    [Fact]
    public void Banana_names_match_exact_first_then_prefix_then_containing_ignoring_case()
    {
        var devices = new List<AudioDeviceInfo>
        {
            Dev("Microphone (PRO X 2 LIGHTSPEED)"), Dev("PRIMARY (KATANA3)"), Dev("KATANA3 Line"),
            Dev("Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)"),
        };

        Assert.Equal("PRIMARY (KATANA3)", LooperService.MatchDeviceName(devices, "primary (katana3)")!.Name);              // exact
        Assert.Equal("PRIMARY (KATANA3)", LooperService.MatchDeviceName(devices, "PRIMARY (KATANA3) extra")!.Name);        // decorated: the device is the prefix
        Assert.Equal("Microphone (PRO X 2 LIGHTSPEED)", LooperService.MatchDeviceName(devices, "Microphone (PRO X 2 LIGHT")!.Name); // truncated by Banana
        Assert.Equal("PRIMARY (KATANA3)", LooperService.MatchDeviceName(devices, "(KATANA3)")!.Name);                      // contained
        Assert.Null(LooperService.MatchDeviceName(devices, "-"));
        Assert.Null(LooperService.MatchDeviceName(devices, ""));
        Assert.Null(LooperService.MatchDeviceName(devices, "Voicemeeter Out B1"));                                         // never Voicemeeter's own
        Assert.Null(LooperService.MatchDeviceName(devices, "Something else"));
    }
}
