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
