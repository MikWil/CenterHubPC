using System;
using System.Collections.Generic;
using System.Linq;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>The selectable click timbres for the metronome.</summary>
    public enum MetronomeSound
    {
        Clock,
        WoodBlock,
        Beep,
        Click,
        Cowbell,
        Rim,
    }

    /// <summary>Every drum-kit piece the drum machine can play.</summary>
    public enum DrumVoice
    {
        Kick,
        Snare,
        SideStick,
        Clap,
        ClosedHat,
        PedalHat,
        OpenHat,
        Ride,
        RideBell,
        Crash,
        HighTom,
        MidTom,
        FloorTom,
        Cowbell,
        Tambourine,
        Shaker,
        Sticks,
    }

    /// <summary>The drum kits. A style suggests one; the user can override it.</summary>
    public enum DrumKitKind
    {
        Rock,
        Electro,
        Jazz,
        /// <summary>Recorded drums (multi-velocity samples; clap, cowbell, tambourine, shaker and sticks stay synthesized).</summary>
        Acoustic,
    }

    /// <summary>What the click plays between the beats.</summary>
    public enum ClickSubdivision
    {
        None,
        Eighths,
        Triplets,
        Sixteenths,
        Swing,
    }

    /// <summary>How the click treats one beat of the bar.</summary>
    public enum BeatAccent
    {
        Mute = 0,
        Normal = 1,
        Accent = 2,
    }

    /// <summary>Where the drum machine is in the song.</summary>
    public enum DrumSection
    {
        Stopped,
        CountIn,
        Intro,
        Main,
        Fill,
        Transition,
        Outro,
    }

    /// <summary>One drum hit: which piece, and how hard (0..1).</summary>
    public readonly record struct DrumHit(DrumVoice Voice, float Velocity);

    /// <summary>
    /// One bar of drums on a fixed step grid, written like a drum tab:
    /// <code>
    /// DrumBar.Parse(
    ///     "HH|x-x-x-x-x-x-x-x-|",
    ///     "SD|----X-------X---|",
    ///     "BD|x-------x-x-----|");
    /// </code>
    /// Each line is a two-letter voice label followed by the steps. <c>|</c> and spaces are
    /// only visual separators (use them to mark beats). Step characters:
    /// <c>-</c> or <c>.</c> rest, <c>g</c> ghost (0.30), <c>s</c> soft (0.55),
    /// <c>x</c> normal (0.80), <c>X</c> accent (1.00).
    /// Labels: BD kick, SD snare, SS side stick, CP clap, HH closed hat, PH pedal hat,
    /// OH open hat, RD ride, RB ride bell, CR crash, T1 high tom, T2 mid tom, T3 floor tom,
    /// CB cowbell, TB tambourine, SH shaker, ST sticks.
    /// </summary>
    public sealed class DrumBar
    {
        private static readonly Dictionary<string, DrumVoice> Labels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BD"] = DrumVoice.Kick,
            ["SD"] = DrumVoice.Snare,
            ["SS"] = DrumVoice.SideStick,
            ["CP"] = DrumVoice.Clap,
            ["HH"] = DrumVoice.ClosedHat,
            ["PH"] = DrumVoice.PedalHat,
            ["OH"] = DrumVoice.OpenHat,
            ["RD"] = DrumVoice.Ride,
            ["RB"] = DrumVoice.RideBell,
            ["CR"] = DrumVoice.Crash,
            ["T1"] = DrumVoice.HighTom,
            ["T2"] = DrumVoice.MidTom,
            ["T3"] = DrumVoice.FloorTom,
            ["CB"] = DrumVoice.Cowbell,
            ["TB"] = DrumVoice.Tambourine,
            ["SH"] = DrumVoice.Shaker,
            ["ST"] = DrumVoice.Sticks,
        };

        private readonly DrumHit[][] _hits;

        private DrumBar(DrumHit[][] hits, IReadOnlyList<DrumVoice> voices)
        {
            _hits = hits;
            Voices = voices;
        }

        /// <summary>Number of grid steps in the bar.</summary>
        public int Steps => _hits.Length;

        /// <summary>The voices this bar uses, in the order the tab lines were written.</summary>
        public IReadOnlyList<DrumVoice> Voices { get; }

        /// <summary>The hits that start on <paramref name="step"/> (empty for a rest).</summary>
        public IReadOnlyList<DrumHit> HitsAt(int step) => _hits[step];

        /// <summary>Velocity of <paramref name="voice"/> on <paramref name="step"/>, or 0 when it rests.</summary>
        public float VelocityAt(DrumVoice voice, int step)
        {
            foreach (var hit in _hits[step])
                if (hit.Voice == voice) return hit.Velocity;
            return 0f;
        }

        /// <summary>Two-letter tab label for a voice (e.g. "BD").</summary>
        public static string LabelOf(DrumVoice voice) => Labels.First(p => p.Value == voice).Key;

        /// <summary>A silent bar of <paramref name="steps"/> steps.</summary>
        public static DrumBar Empty(int steps)
        {
            if (steps <= 0) throw new ArgumentOutOfRangeException(nameof(steps));
            var hits = new DrumHit[steps][];
            for (int i = 0; i < steps; i++) hits[i] = Array.Empty<DrumHit>();
            return new DrumBar(hits, Array.Empty<DrumVoice>());
        }

        /// <summary>Parses drum-tab lines (see the class summary). Throws <see cref="FormatException"/> on bad input.</summary>
        public static DrumBar Parse(params string[] lines)
        {
            if (lines is null || lines.Length == 0)
                throw new FormatException("A drum bar needs at least one line.");

            var voices = new List<DrumVoice>();
            List<DrumHit>[]? steps = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                int bar = line.IndexOf('|');
                if (bar <= 0) throw new FormatException($"Drum line '{raw}' must start with a voice label and '|'.");

                var label = line[..bar].Trim();
                if (!Labels.TryGetValue(label, out var voice))
                    throw new FormatException($"Unknown drum voice label '{label}' in '{raw}'.");
                if (voices.Contains(voice))
                    throw new FormatException($"Drum voice '{label}' appears twice in the same bar.");

                var body = line[bar..].Replace("|", "").Replace(" ", "");
                if (body.Length == 0) throw new FormatException($"Drum line '{raw}' has no steps.");

                steps ??= Enumerable.Range(0, body.Length).Select(_ => new List<DrumHit>()).ToArray();
                if (body.Length != steps.Length)
                    throw new FormatException(
                        $"Drum line '{raw}' has {body.Length} steps but the bar has {steps.Length}.");

                voices.Add(voice);
                for (int i = 0; i < body.Length; i++)
                {
                    float velocity = body[i] switch
                    {
                        '-' or '.' => 0f,
                        'g' => 0.30f,
                        's' => 0.55f,
                        'x' => 0.80f,
                        'X' => 1.00f,
                        _ => throw new FormatException($"Unknown step character '{body[i]}' in '{raw}'."),
                    };
                    if (velocity > 0f) steps[i].Add(new DrumHit(voice, velocity));
                }
            }

            return new DrumBar(steps!.Select(s => s.ToArray()).ToArray(), voices);
        }
    }

    /// <summary>One song part (verse, chorus…): a looping groove plus its fills.</summary>
    public sealed class DrumPart
    {
        /// <summary>Shown in the UI, e.g. "Verse".</summary>
        public string Name { get; init; } = "";

        /// <summary>The groove: one or more bars played in a loop.</summary>
        public IReadOnlyList<DrumBar> Main { get; init; } = Array.Empty<DrumBar>();

        /// <summary>One-bar fills, used round-robin when a fill is requested.</summary>
        public IReadOnlyList<DrumBar> Fills { get; init; } = Array.Empty<DrumBar>();

        /// <summary>The one-bar fill that leads into the next part.</summary>
        public DrumBar? Transition { get; init; }
    }

    /// <summary>
    /// A complete drum "song" in the BeatBuddy sense: intro fill, one or more parts
    /// (each with a main groove, fills and a transition) and an outro fill.
    /// Every bar in a style has <see cref="StepsPerBar"/> steps.
    /// </summary>
    public sealed class DrumStyle
    {
        /// <summary>Stable id used for persistence, e.g. "rock-8ths".</summary>
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Genre { get; init; } = "";

        /// <summary>Short feel hint shown under the name, e.g. "Straight 8ths, backbeat on 2 and 4".</summary>
        public string Description { get; init; } = "";

        /// <summary>Display only, e.g. "4/4", "12/8".</summary>
        public string Signature { get; init; } = "4/4";

        /// <summary>Beats per bar. The tempo (BPM) counts these beats.</summary>
        public int Beats { get; init; } = 4;

        /// <summary>Grid steps per beat (4 = sixteenths, 3 = triplets, 6 = sixteenth triplets). Must divide 48.</summary>
        public int StepsPerBeat { get; init; } = 4;

        public int DefaultBpm { get; init; } = 120;
        public DrumKitKind SuggestedKit { get; init; } = DrumKitKind.Rock;

        public DrumBar? Intro { get; init; }
        public IReadOnlyList<DrumPart> Parts { get; init; } = Array.Empty<DrumPart>();
        public DrumBar? Outro { get; init; }

        public int StepsPerBar => Beats * StepsPerBeat;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A position update from the drum machine, stamped with the output frame it becomes
    /// audible at, so the UI can light up in sync with the sound.
    /// </summary>
    /// <param name="Frame">Engine frame (sample pair) index at which this step starts.</param>
    /// <param name="Step">0-based grid step within the bar (in click-only mode: the 0-based beat).</param>
    /// <param name="StepsInBar">Grid steps in the current bar.</param>
    /// <param name="Beat">1-based beat this step belongs to.</param>
    /// <param name="BeatsInBar">Beats in the current bar.</param>
    /// <param name="IsBeatStart">True when the step is the first of its beat.</param>
    /// <param name="Bar">1-based count of bars since the groove started (0 during count-in / intro).</param>
    /// <param name="Section">Song section being played; <see cref="DrumSection.Stopped"/> for the final event.</param>
    /// <param name="PartIndex">Index of the current part in <see cref="DrumStyle.Parts"/>.</param>
    /// <param name="Pattern">The bar being played (null in click-only mode and for the final event).</param>
    /// <param name="Muted">True while the gap trainer is silencing this bar.</param>
    public readonly record struct DrumEngineEvent(
        long Frame,
        int Step,
        int StepsInBar,
        int Beat,
        int BeatsInBar,
        bool IsBeatStart,
        int Bar,
        DrumSection Section,
        int PartIndex,
        DrumBar? Pattern,
        bool Muted);

    /// <summary>One song in a setlist: the tempo and beat to load for it.</summary>
    public sealed class SetlistSong
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public int Bpm { get; set; } = 120;

        /// <summary>True = drum beat, false = plain click.</summary>
        public bool DrumsMode { get; set; }

        public string? StyleId { get; set; }
        public DrumKitKind Kit { get; set; } = DrumKitKind.Acoustic;
        public int BeatsPerMeasure { get; set; } = 4;
        public ClickSubdivision Subdivision { get; set; } = ClickSubdivision.None;
        public bool CountIn { get; set; }
    }

    /// <summary>A named, ordered list of songs.</summary>
    public sealed class Setlist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public List<SetlistSong> Songs { get; set; } = new();
    }

    /// <summary>Pure helpers for the setlist UI (kept free of UI types so they can be unit-tested).</summary>
    public static class SetlistHelper
    {
        /// <summary>"120 BPM · Rock 8ths" for a drum song, "120 BPM · Click 4/4" for a click song.</summary>
        public static string Describe(SetlistSong song, string? styleName)
        {
            string what = song.DrumsMode
                ? (string.IsNullOrWhiteSpace(styleName) ? "Drums" : styleName!)
                : $"Click {song.BeatsPerMeasure}/4";
            return $"{song.Bpm} BPM · {what}";
        }

        /// <summary>
        /// The index reached by moving <paramref name="delta"/> songs from <paramref name="current"/>, wrapping
        /// around at both ends. With nothing current (-1) next goes to the first song and previous to the last.
        /// Returns -1 for an empty list.
        /// </summary>
        public static int Step(int current, int delta, int count)
        {
            if (count <= 0) return -1;
            if (current < 0 || current >= count) return delta >= 0 ? 0 : count - 1;
            return (((current + delta) % count) + count) % count;
        }

        /// <summary>Fixes values a hand-edited or older file may hold. Never throws.</summary>
        public static void Sanitize(SetlistSong song)
        {
            song.Id = string.IsNullOrWhiteSpace(song.Id) ? Guid.NewGuid().ToString("N") : song.Id;
            song.Name ??= "";
            song.Bpm = Math.Clamp(song.Bpm, 30, 280);
            song.BeatsPerMeasure = Math.Clamp(song.BeatsPerMeasure, 1, 12);
            if (!Enum.IsDefined(song.Kit)) song.Kit = DrumKitKind.Acoustic;
            if (!Enum.IsDefined(song.Subdivision)) song.Subdivision = ClickSubdivision.None;
        }
    }

    /// <summary>Everything the Metronome page remembers between sessions (metronome.json).</summary>
    public sealed class MetronomeSettings
    {
        public int Bpm { get; set; } = 120;
        public double Volume { get; set; } = 0.75;

        /// <summary>True = drum beats, false = plain click.</summary>
        public bool DrumsMode { get; set; }

        // ── Click ──
        public MetronomeSound ClickSound { get; set; } = MetronomeSound.Clock;
        public int BeatsPerMeasure { get; set; } = 4;
        public ClickSubdivision Subdivision { get; set; } = ClickSubdivision.None;
        public List<BeatAccent> Accents { get; set; } = new();

        // ── Drums ──
        public string? StyleId { get; set; }
        public DrumKitKind Kit { get; set; } = DrumKitKind.Acoustic;

        /// <summary>
        /// True once the one-time move from the synthesized Rock/Jazz kit to the recorded drums has
        /// happened, so a user who then picks the synth kit again keeps it.
        /// </summary>
        public bool RealDrumsOffered { get; set; }

        public bool ClickWithDrums { get; set; }
        public bool CountIn { get; set; }
        public bool IntroFill { get; set; } = true;
        public int AutoFillBars { get; set; }

        // ── Practice tools ──
        public bool TrainerEnabled { get; set; }
        public int TrainerStepBpm { get; set; } = 5;
        public int TrainerEveryBars { get; set; } = 4;
        public int TrainerTargetBpm { get; set; } = 160;
        public bool GapEnabled { get; set; }
        public int GapPlayBars { get; set; } = 2;
        public int GapMuteBars { get; set; } = 2;

        // ── Guitar looper ──
        /// <summary>Length of a new loop in bars; 0 = free (ends when Record is pressed again).</summary>
        public int LooperLengthBars { get; set; } = 4;
        /// <summary>Loop playback level, 0–2.</summary>
        public double LooperVolume { get; set; } = 1.0;
        /// <summary>Delay compensation in milliseconds, 0–400.</summary>
        public int LooperLatencyMs { get; set; } = 60;
        /// <summary>Capture device id; null = automatic (guitar from the Sound setup, else the Windows default).</summary>
        public string? LooperInputDeviceId { get; set; }
        /// <summary>The loop plays as soon as the first take ends; off = it waits for Play loop.</summary>
        public bool LooperAutoPlay { get; set; } = true;
        /// <summary>
        /// Record starts the drum machine when it isn't playing (bar-synced loop). Off (default) = the looper
        /// works on its own: count-in, record, loop. (Replaces the old LooperStartsDrums key, which is ignored.)
        /// </summary>
        public bool LooperRecordStartsDrums { get; set; }
        /// <summary>Count-in before a free-mode take, in bars: 0, 1 or 2.</summary>
        public int LooperCountInBars { get; set; } = 1;
        /// <summary>Make quiet takes louder automatically.</summary>
        public bool LooperAutoLevel { get; set; } = true;

        // ── Setlists ──
        public List<Setlist> Setlists { get; set; } = new();
        public string? ActiveSetlistId { get; set; }
        public int ActiveSongIndex { get; set; }
    }
}
