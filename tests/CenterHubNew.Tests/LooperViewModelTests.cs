using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;
using Xunit;

namespace CenterHubNew.Tests;

public class LooperTextTests
{
    [Theory]
    [InlineData(LooperState.Empty, "Record")]
    [InlineData(LooperState.Armed, "Cancel")]
    [InlineData(LooperState.Playing, "Overdub")]
    [InlineData(LooperState.Overdubbing, "Stop overdub")]
    public void Record_label_follows_the_state(LooperState state, string expected)
    {
        Assert.Equal(expected, LooperText.RecordLabel(state, lengthBars: 4));
    }

    [Fact]
    public void Record_label_while_recording_depends_on_free_mode()
    {
        Assert.Equal("Finish", LooperText.RecordLabel(LooperState.Recording, lengthBars: 0));
        Assert.NotEqual("Finish", LooperText.RecordLabel(LooperState.Recording, lengthBars: 4));
    }

    [Fact]
    public void Stop_label_becomes_play_when_stopped()
    {
        Assert.Equal("Stop loop", LooperText.StopLabel(LooperState.Playing));
        Assert.Equal("Play loop", LooperText.StopLabel(LooperState.Stopped));
    }

    [Fact]
    public void Status_text_describes_the_loop()
    {
        Assert.Equal("Empty — press Record", LooperText.Status(LooperState.Empty, 0, 0, 4));
        Assert.Equal("Armed — starts on the next bar", LooperText.Status(LooperState.Armed, 0, 0, 4));
        Assert.Equal("Playing · 4 bars · 2 layers", LooperText.Status(LooperState.Playing, 4, 2, 4));
        Assert.Equal("Playing · 1 bar · 1 layer", LooperText.Status(LooperState.Playing, 1, 1, 1));
        Assert.Equal("Stopped · 4 bars", LooperText.Status(LooperState.Stopped, 4, 2, 4));
        Assert.Equal("Overdubbing · 2 bars", LooperText.Status(LooperState.Overdubbing, 2, 2, 2));
    }

    [Fact]
    public void Loop_state_flags()
    {
        Assert.False(LooperText.HasLoop(LooperState.Empty));
        Assert.False(LooperText.HasLoop(LooperState.Armed));
        Assert.False(LooperText.HasLoop(LooperState.Recording));
        Assert.True(LooperText.HasLoop(LooperState.Playing));
        Assert.True(LooperText.HasLoop(LooperState.Overdubbing));
        Assert.True(LooperText.HasLoop(LooperState.Stopped));

        Assert.True(LooperText.IsRecording(LooperState.Recording));
        Assert.True(LooperText.IsRecording(LooperState.Overdubbing));
        Assert.False(LooperText.IsRecording(LooperState.Playing));
    }

    [Fact]
    public void Tempo_mismatch_needs_a_loop_and_a_difference_of_one_bpm()
    {
        Assert.False(LooperText.TempoMismatch(LooperState.Empty, 120, 90));
        Assert.False(LooperText.TempoMismatch(LooperState.Playing, 120, 120));
        Assert.False(LooperText.TempoMismatch(LooperState.Playing, 120.4, 120));
        Assert.True(LooperText.TempoMismatch(LooperState.Playing, 120, 121));
        Assert.True(LooperText.TempoMismatch(LooperState.Stopped, 100, 120));
        Assert.False(LooperText.TempoMismatch(LooperState.Playing, 0, 120));
        Assert.Equal("Recorded at 120 BPM", LooperText.TempoMismatchText(120.2));
        Assert.Equal("Back to 120 BPM", LooperText.MatchTempoText(119.8));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.25, 0.25)]
    [InlineData(7, 1)]
    [InlineData(double.NaN, 0)]
    public void Fraction_is_clamped(double input, double expected)
    {
        Assert.Equal(expected, LooperText.Fraction(input));
    }

    [Fact]
    public void Export_file_name_is_timestamped()
    {
        Assert.Equal("loop-20261005-1430.wav", LooperText.ExportFileName(new DateTime(2026, 10, 5, 14, 30, 59)));
    }
}

public class LooperSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubTests-" + Guid.NewGuid().ToString("N"));

    public LooperSettingsTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    private MetronomeSettings LoadFromJson(string json)
    {
        File.WriteAllText(Path.Combine(_folder, "metronome.json"), json);
        return new MetronomeSettingsService(null, _folder).Load();
    }

    [Fact]
    public void Defaults()
    {
        var s = new MetronomeSettingsService(null, _folder).Load();
        Assert.Equal(4, s.LooperLengthBars);
        Assert.Equal(1.0, s.LooperVolume);
        Assert.Equal(60, s.LooperLatencyMs);
        Assert.Null(s.LooperInputDeviceId);
    }

    [Fact]
    public void Values_round_trip()
    {
        var service = new MetronomeSettingsService(null, _folder);
        service.Save(new MetronomeSettings
        {
            LooperLengthBars = 8,
            LooperVolume = 1.25,
            LooperLatencyMs = 85,
            LooperInputDeviceId = "{0.0.1.00000000}.guitar",
        });

        var loaded = new MetronomeSettingsService(null, _folder).Load();
        Assert.Equal(8, loaded.LooperLengthBars);
        Assert.Equal(1.25, loaded.LooperVolume);
        Assert.Equal(85, loaded.LooperLatencyMs);
        Assert.Equal("{0.0.1.00000000}.guitar", loaded.LooperInputDeviceId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(3, 4)]
    [InlineData(16, 4)]
    [InlineData(-2, 4)]
    public void Length_bars_are_restricted_to_the_offered_values(int stored, int expected)
    {
        var s = LoadFromJson($"{{ \"LooperLengthBars\": {stored} }}");
        Assert.Equal(expected, s.LooperLengthBars);
    }

    [Fact]
    public void Volume_and_latency_are_clamped()
    {
        var high = LoadFromJson("{ \"LooperVolume\": 9, \"LooperLatencyMs\": 5000 }");
        Assert.Equal(1.5, high.LooperVolume);
        Assert.Equal(400, high.LooperLatencyMs);

        var low = LoadFromJson("{ \"LooperVolume\": -3, \"LooperLatencyMs\": -20 }");
        Assert.Equal(0.0, low.LooperVolume);
        Assert.Equal(0, low.LooperLatencyMs);
    }

    [Fact]
    public void Blank_device_id_means_automatic()
    {
        var s = LoadFromJson("{ \"LooperInputDeviceId\": \"  \" }");
        Assert.Null(s.LooperInputDeviceId);
    }
}
