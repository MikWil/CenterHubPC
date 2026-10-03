using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

/// <summary>
/// Builds Assets/Drums/acoustic.chdk from "The Open Source Drumkit" by Real Music Media (public
/// domain; https://github.com/crabacus/the-open-source-drumkit). The source WAVs are 96 kHz /
/// 24-bit stereo, numbered soft → loud. For each drum this picks hits spread evenly across the
/// loudness range, resamples to 44.1 kHz, shortens long cymbal tails with a smooth fade and
/// normalises the drum so its loudest hit matches the synthesized kit's level.
///
///   dotnet run --project tools/page-render -- <outDir> build-drumkit <source folder> [output file]
/// </summary>
internal static class DrumKitBuilder
{
    private const int SampleRate = 44100;

    /// <summary>Which recordings make which drum.</summary>
    private sealed record Spec(DrumVoice Voice, string Folder, string FilePrefix, int Count, double MaxSeconds, float Peak);

    private static readonly Spec[] Specs =
    {
        new(DrumVoice.Kick,      "kick",                      "kick",         12, 0.70, 0.95f),
        new(DrumVoice.Snare,     "snare",                     "snare-top",    16, 0.45, 0.90f),
        new(DrumVoice.SideStick, "sidestick",                 "sidestick",     8, 0.30, 0.70f),
        new(DrumVoice.ClosedHat, Path.Combine("hihat", "closed-hihat"),    "chh",  14, 0.45, 0.55f),
        new(DrumVoice.PedalHat,  Path.Combine("hihat", "foot-hihat"),      "fhh",   6, 0.25, 0.40f),
        new(DrumVoice.OpenHat,   Path.Combine("hihat", "half-open-hihat"), "hohh", 10, 1.20, 0.55f),
        new(DrumVoice.Ride,      "ride",                      "ride-mid-in",   6, 2.80, 0.50f),
        new(DrumVoice.RideBell,  "ride",                      "ride-bell",     5, 2.80, 0.55f),
        new(DrumVoice.Crash,     "crash",                     "crash",         6, 3.20, 0.70f),
        new(DrumVoice.HighTom,   "toms",                      "small-tom",     8, 0.90, 0.85f),
        new(DrumVoice.MidTom,    "toms",                      "medium-tom",    8, 0.90, 0.85f),
        new(DrumVoice.FloorTom,  "toms",                      "large-tom",     8, 1.20, 0.85f),
    };

    public static int Build(string sourceDir, string outFile)
    {
        var entries = new List<(DrumVoice, DrumSampleLayer)>();

        foreach (var spec in Specs)
        {
            // "<prefix><number>.wav" only — "snare-top12.wav" but not "snare-top-off12.wav".
            var files = Directory.GetFiles(Path.Combine(sourceDir, spec.Folder), spec.FilePrefix + "*.wav")
                .Where(f => int.TryParse(Path.GetFileNameWithoutExtension(f)[spec.FilePrefix.Length..], out _))
                .Select(f => (File: f, Audio: Load(f, spec.MaxSeconds)))
                .OrderBy(x => Peak(x.Audio))
                .ToList();
            if (files.Count == 0) throw new InvalidOperationException($"No recordings for {spec.Voice} in {spec.Folder}");

            // Spread the picks over the whole dynamic range, softest and loudest included.
            int count = Math.Min(spec.Count, files.Count);
            var picked = Enumerable.Range(0, count)
                .Select(i => files[(int)Math.Round(i * (files.Count - 1) / (double)Math.Max(1, count - 1))])
                .Distinct()
                .ToList();

            float gain = spec.Peak / Math.Max(1e-6f, picked.Max(p => Peak(p.Audio)));
            foreach (var (_, audio) in picked)
            {
                for (int i = 0; i < audio.L.Length; i++) { audio.L[i] *= gain; audio.R[i] *= gain; }
                entries.Add((spec.Voice, new DrumSampleLayer(Peak(audio), audio.L, audio.R)));
            }

            Console.WriteLine($"{spec.Voice,-10} {picked.Count,2} hits from {files.Count,2} recordings, " +
                              $"{picked.Min(p => p.Audio.L.Length) / (double)SampleRate:F2}-{picked.Max(p => p.Audio.L.Length) / (double)SampleRate:F2} s, " +
                              $"dynamic range {20 * Math.Log10(Peak(picked[^1].Audio) / Math.Max(1e-6f, Peak(picked[0].Audio))):F1} dB");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        using (var fs = File.Create(outFile))
            DrumSamplePack.Write(fs, SampleRate, entries);

        Console.WriteLine($"wrote {outFile}: {new FileInfo(outFile).Length / 1048576.0:F1} MB, {entries.Count} hits");
        return 0;
    }

    private sealed class Stereo
    {
        public required float[] L;
        public required float[] R;
    }

    private static float Peak(Stereo s)
    {
        float p = 0;
        for (int i = 0; i < s.L.Length; i++) p = Math.Max(p, Math.Max(Math.Abs(s.L[i]), Math.Abs(s.R[i])));
        return p;
    }

    /// <summary>Reads a hit as 44.1 kHz stereo, cut to <paramref name="maxSeconds"/> with a smooth fade.</summary>
    private static Stereo Load(string file, double maxSeconds)
    {
        using var reader = new AudioFileReader(file);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);

        var interleaved = new List<float>();
        var buffer = new float[SampleRate * 2];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) interleaved.Add(buffer[i]);

        int frames = interleaved.Count / 2;

        // Drop a natural ending that is already silent, then cap the length.
        int last = frames - 1;
        while (last > 0 && Math.Abs(interleaved[last * 2]) < 1e-4f && Math.Abs(interleaved[last * 2 + 1]) < 1e-4f) last--;
        frames = Math.Min(last + 1, (int)(maxSeconds * SampleRate));

        var l = new float[frames];
        var r = new float[frames];
        for (int i = 0; i < frames; i++) { l[i] = interleaved[i * 2]; r[i] = interleaved[i * 2 + 1]; }

        // Fade the last 30 % on a raised cosine (a cut cymbal otherwise ends in a click), and
        // ramp the first 0.5 ms so a hit never starts with a step.
        int fade = Math.Max(1, (int)(frames * 0.30));
        for (int i = 0; i < fade; i++)
        {
            float g = 0.5f * (1 + MathF.Cos(MathF.PI * (i + 1) / fade));
            l[frames - fade + i] *= g;
            r[frames - fade + i] *= g;
        }
        int ramp = Math.Min(frames, 22);
        for (int i = 0; i < ramp; i++) { l[i] *= i / (float)ramp; r[i] *= i / (float)ramp; }

        return new Stereo { L = l, R = r };
    }
}
