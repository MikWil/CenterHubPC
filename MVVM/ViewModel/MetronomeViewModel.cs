using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    public sealed class MetronomePreset
    {
        public string Name { get; init; } = "";
        public string Italian { get; init; } = "";
        public int    Bpm { get; init; }
    }

    /// <summary>A selectable click timbre, shown in the Sound picker.</summary>
    public sealed class MetronomeSoundChoice
    {
        public string Name { get; init; } = "";
        public MetronomeSound Sound { get; init; }
        public override string ToString() => Name; // ComboBox fallback display
    }

    /// <summary>A selectable click subdivision.</summary>
    public sealed class SubdivisionChoice
    {
        public string Name { get; init; } = "";
        public ClickSubdivision Value { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>A selectable drum kit.</summary>
    public sealed class KitChoice
    {
        public string Name { get; init; } = "";
        public DrumKitKind Kind { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>A selectable auto-fill interval (0 = off).</summary>
    public sealed class AutoFillChoice
    {
        public string Name { get; init; } = "";
        public int Bars { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>A selectable loop length for the looper (0 bars = free).</summary>
    public sealed class LooperLengthChoice
    {
        public string Name { get; init; } = "";
        public int Bars { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>A selectable count-in length for free-mode looper takes (0 = none).</summary>
    public sealed class LooperCountInChoice
    {
        public string Name { get; init; } = "";
        public int Bars { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>A selectable looper input device; a null <see cref="Id"/> means automatic.</summary>
    public sealed class LooperDeviceChoice
    {
        public LooperDeviceChoice(string? id, string name)
        {
            Id = id;
            Name = name;
        }

        public string? Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    /// <summary>Pure text/format helpers for the looper card (unit-tested).</summary>
    public static class LooperText
    {
        public static string Bars(int bars) => bars == 1 ? "1 bar" : $"{bars} bars";

        public static string Layers(int layers) => layers == 1 ? "1 layer" : $"{layers} layers";

        /// <summary>"7.4 s" (one decimal, invariant culture).</summary>
        public static string Seconds(double seconds) =>
            seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        /// <summary>"120 ms" (whole milliseconds, invariant culture).</summary>
        public static string Ms(double ms) =>
            Math.Round(ms).ToString("0", CultureInfo.InvariantCulture) + " ms";

        /// <summary>Length of the loop: "7.4 s" for a free loop, "4 bars" for a bar-synced one; empty when unknown.</summary>
        public static string LoopLength(int bars, bool isFree, double seconds)
        {
            if (isFree) return seconds > 0 ? Seconds(seconds) : "";
            return bars > 0 ? Bars(bars) : (seconds > 0 ? Seconds(seconds) : "");
        }

        /// <summary>"4 bars · 2 layers" / "7.4 s · 2 layers"; empty when there is no loop.</summary>
        public static string LoopInfo(int bars, int layers, bool isFree = false, double seconds = 0)
        {
            string length = LoopLength(bars, isFree, seconds);
            if (length.Length == 0) return "";
            return layers > 0 ? $"{length} · {Layers(layers)}" : length;
        }

        public static string Status(
            LooperState state, int loopBars, int layers, int lengthBars,
            bool isCountingIn = false, int countInBeatsLeft = 0, bool isFree = false, double loopSeconds = 0)
        {
            string length = LoopLength(loopBars, isFree, loopSeconds);
            switch (state)
            {
                case LooperState.Armed:
                    if (!isCountingIn) return "Armed — starts on the next bar";
                    return countInBeatsLeft > 0 ? $"Count-in… {countInBeatsLeft}" : "Count-in…";
                case LooperState.Recording:
                    return lengthBars > 0 ? $"Recording… {Bars(lengthBars)}" : "Recording… press Record to finish";
                case LooperState.Playing:
                    return "Playing · " + LoopInfo(loopBars, layers, isFree, loopSeconds);
                case LooperState.Overdubbing:
                    return length.Length > 0 ? $"Overdubbing · {length}" : "Overdubbing";
                case LooperState.Stopped:
                    return length.Length > 0 ? $"Stopped · {length}" : "Stopped";
                default:
                    return "Empty — press Record";
            }
        }

        /// <summary>"Auto level: +12 dB"; empty when auto level is off or the gain is about 0 dB.</summary>
        public static string GainText(double gain, bool autoLevel)
        {
            if (!autoLevel || gain <= 0 || double.IsNaN(gain)) return "";
            double db = 20.0 * Math.Log10(gain);
            if (Math.Abs(db) < 0.5) return "";
            return "Auto level: " + (db > 0 ? "+" : "−") + Math.Abs(Math.Round(db)).ToString("0", CultureInfo.InvariantCulture) + " dB";
        }

        /// <summary>Share (0–1) of the recorded length taken by <paramref name="ms"/>; 0 when nothing is recorded.</summary>
        public static double TrimFraction(double ms, double recordedSeconds) =>
            recordedSeconds <= 0 ? 0 : Fraction(ms / (recordedSeconds * 1000.0));

        public static string RecordLabel(LooperState state, int lengthBars) => state switch
        {
            LooperState.Armed => "Cancel",
            LooperState.Recording => lengthBars > 0 ? "Recording…" : "Finish",
            LooperState.Playing => "Overdub",
            LooperState.Stopped => "Overdub",
            LooperState.Overdubbing => "Stop overdub",
            _ => "Record",
        };

        public static string StopLabel(LooperState state) => state == LooperState.Stopped ? "Play loop" : "Stop loop";

        /// <summary>True for states where a finished loop exists.</summary>
        public static bool HasLoop(LooperState state) =>
            state is LooperState.Playing or LooperState.Overdubbing or LooperState.Stopped;

        public static bool IsRecording(LooperState state) =>
            state is LooperState.Recording or LooperState.Overdubbing;

        /// <summary>The loop was recorded at a tempo that differs from the current one by at least 1 BPM.</summary>
        public static bool TempoMismatch(LooperState state, double loopBpm, int currentBpm, bool isFree = false) =>
            !isFree && HasLoop(state) && loopBpm > 0 && Math.Abs(loopBpm - currentBpm) >= 1.0;

        public static string TempoMismatchText(double loopBpm) => $"Recorded at {(int)Math.Round(loopBpm)} BPM";

        public static string MatchTempoText(double loopBpm) => $"Back to {(int)Math.Round(loopBpm)} BPM";

        /// <summary>Fraction 0–1 of a 0–1 / 0–100 style value, clamped (NaN becomes 0).</summary>
        public static double Fraction(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);

        public static string ExportFileName(DateTime now) => $"loop-{now:yyyyMMdd-HHmm}.wav";
    }

    /// <summary>One beat of the bar: its accent state and whether it is sounding right now.</summary>
    public partial class BeatLight : ObservableObject
    {
        public BeatLight(int number, BeatAccent accent)
        {
            Number = number;
            _accent = accent;
        }

        public int Number { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAccent))]
        [NotifyPropertyChangedFor(nameof(IsNormal))]
        [NotifyPropertyChangedFor(nameof(IsMuted))]
        private BeatAccent _accent;

        [ObservableProperty] private bool _isActive;

        public bool IsAccent => Accent == BeatAccent.Accent;
        public bool IsNormal => Accent == BeatAccent.Normal;
        public bool IsMuted  => Accent == BeatAccent.Mute;
    }

    /// <summary>One step of one drum voice in the pattern grid. Immutable.</summary>
    public sealed class PatternCell
    {
        public PatternCell(double level, bool isBeatStart, double width)
        {
            Level = level;
            IsHit = level > 0;
            IsBeatStart = isBeatStart;
            Width = width;
            CellOpacity = IsHit ? 0.35 + 0.65 * level : 1.0;
        }

        public double Level { get; }
        public double Width { get; }
        public bool IsHit { get; }
        public bool IsBeatStart { get; }
        public double CellOpacity { get; }
    }

    /// <summary>One line of the pattern grid (a drum voice).</summary>
    public sealed class PatternRow
    {
        public PatternRow(string label, IReadOnlyList<PatternCell> cells)
        {
            Label = label;
            Cells = cells;
        }

        public string Label { get; }
        public IReadOnlyList<PatternCell> Cells { get; }
    }

    /// <summary>One playhead marker above the pattern grid.</summary>
    public partial class StepMarker : ObservableObject
    {
        public StepMarker(bool isBeatStart, double width)
        {
            IsBeatStart = isBeatStart;
            Width = width;
        }

        public bool IsBeatStart { get; }
        public double Width { get; }

        [ObservableProperty] private bool _isCurrent;
    }

    /// <summary>A setlist as shown in the picker; renaming writes through to the model and asks for a save.</summary>
    public sealed class SetlistItem : ObservableObject
    {
        private readonly Action _changed;

        public SetlistItem(Setlist model, Action changed)
        {
            Model = model;
            _changed = changed;
        }

        public Setlist Model { get; }

        public string Name
        {
            get => Model.Name;
            set
            {
                value ??= "";
                if (Model.Name == value) return;
                Model.Name = value;
                OnPropertyChanged();
                _changed();
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>One row of the setlist's song list.</summary>
    public sealed class SetlistSongItem : ObservableObject
    {
        private readonly Func<SetlistSong, string> _describe;
        private readonly Action _changed;
        private int _number;
        private bool _isActive;
        private string _description;

        public SetlistSongItem(SetlistSong song, Func<SetlistSong, string> describe, Action changed)
        {
            Song = song;
            _describe = describe;
            _changed = changed;
            _description = describe(song);
        }

        public SetlistSong Song { get; }

        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        /// <summary>Editable in the list; an empty name is kept as typed and shown as "Untitled" elsewhere.</summary>
        public string Name
        {
            get => Song.Name;
            set
            {
                value ??= "";
                if (Song.Name == value) return;
                Song.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                _changed();
            }
        }

        /// <summary>"120 BPM · Rock 8ths".</summary>
        public string Description
        {
            get => _description;
            private set => SetProperty(ref _description, value);
        }

        /// <summary>True for the song most recently loaded / added (highlighted in the list).</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Song.Name) ? "Untitled" : Song.Name;

        public void Refresh() => Description = _describe(Song);
    }

    /// <summary>One bar of the 14-day practice chart. Immutable; heights are doubles on purpose.</summary>
    public sealed class PracticeBar
    {
        public const double MaxHeight = 56;
        public const double MinHeight = 3;

        public PracticeBar(DateTime date, double minutes, double scaleMinutes, bool isToday)
        {
            Date = date;
            Minutes = minutes;
            IsToday = isToday;
            HasPractice = minutes > 0;
            Height = HasPractice
                ? MinHeight + (MaxHeight - MinHeight) * Math.Clamp(minutes / Math.Max(1, scaleMinutes), 0, 1)
                : MinHeight;
            Tooltip = PracticeText.BarTooltip(date, minutes);
        }

        public DateTime Date { get; }
        public double Minutes { get; }
        public double Height { get; }
        public bool IsToday { get; }
        public bool HasPractice { get; }
        public string Tooltip { get; }
    }

    public partial class MetronomeViewModel : BaseViewModel
    {
        private const int MinBpm = 30;
        private const int MaxBpm = 280;

        /// <summary>Width (px) the pattern grid's step columns may use at the page's full width.</summary>
        private const double GridWidth = 700;

        private readonly MetronomeService _audio;
        private readonly MetronomeSettingsService _settingsService;
        private DispatcherTimer? _saveTimer;
        private bool _initializing = true;
        private bool _suppressStyleChange;

        private DrumMachineEngine Engine => _audio.Engine;

        // ── User config ──
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TempoName))]
        [NotifyPropertyChangedFor(nameof(IntervalDisplay))]
        [NotifyPropertyChangedFor(nameof(LooperTempoMismatch))]
        private int _bpm = 120;

        [ObservableProperty] private double _volume = 0.75; // 0.0–1.0

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsClickMode))]
        private bool _isDrumsMode;

        // Click
        [ObservableProperty] private MetronomeSoundChoice? _selectedSound;
        [ObservableProperty] private int _beatsPerMeasure = 4;
        [ObservableProperty] private SubdivisionChoice? _selectedSubdivision;

        // Drums
        [ObservableProperty] private string _selectedGenre = "All";
        [ObservableProperty] private IReadOnlyList<DrumStyle> _filteredStyles = Array.Empty<DrumStyle>();
        [ObservableProperty] private DrumStyle? _selectedStyle;
        [ObservableProperty] private KitChoice? _selectedKit;
        [ObservableProperty] private bool _clickWithDrums;
        [ObservableProperty] private bool _countIn;
        [ObservableProperty] private bool _introFill = true;
        [ObservableProperty] private AutoFillChoice? _selectedAutoFill;

        // Practice tools (decimal so NumericUpDown bindings stay simple)
        [ObservableProperty] private bool _trainerEnabled;
        [ObservableProperty] private decimal _trainerStepBpm = 5;
        [ObservableProperty] private decimal _trainerEveryBars = 4;
        [ObservableProperty] private decimal _trainerTargetBpm = 160;
        [ObservableProperty] private bool _gapEnabled;
        [ObservableProperty] private decimal _gapPlayBars = 2;
        [ObservableProperty] private decimal _gapMuteBars = 2;

        // ── Live state ──
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanNextPart))]
        private bool _isPlaying;

        [ObservableProperty] private int _currentBeat;            // 1..N during play, 0 when idle
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBar))]
        private int _barNumber;
        [ObservableProperty] private string _sectionLabel = "";
        [ObservableProperty] private bool _isGapMuted;
        [ObservableProperty] private string _nextPartName = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanNextPart))]
        private bool _hasMultipleParts;
        [ObservableProperty] private bool _hasPattern;

        public bool IsClickMode => !IsDrumsMode;
        public bool HasBar => BarNumber > 0;
        public bool CanNextPart => IsPlaying && HasMultipleParts;

        public string TempoName => Bpm switch
        {
            < 60  => "Largo",
            < 76  => "Adagio",
            < 108 => "Andante",
            < 120 => "Moderato",
            < 156 => "Allegro",
            < 176 => "Vivace",
            _     => "Presto",
        };

        public string IntervalDisplay => $"{(int)(60_000.0 / Math.Max(1, Bpm))} ms / beat";

        // ── Setlists ──
        public ObservableCollection<SetlistItem> Setlists { get; } = new();

        /// <summary>The songs of <see cref="SelectedSetlist"/>.</summary>
        public ObservableCollection<SetlistSongItem> Songs { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedSetlist))]
        private SetlistItem? _selectedSetlist;

        /// <summary>The song most recently loaded or added (highlighted in the list).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedSong))]
        private SetlistSongItem? _selectedSong;

        /// <summary>True while the setlist name is being edited in place of the picker.</summary>
        [ObservableProperty] private bool _isRenamingSetlist;

        public bool HasSelectedSetlist => SelectedSetlist is not null;
        public bool HasSelectedSong => SelectedSong is not null;
        public bool HasSongs => Songs.Count > 0;

        // ── Practice log ──
        private readonly PracticeLogService? _practiceLog;
        private DateTime? _sessionStart;
        private Stopwatch? _sessionClock;
        private int _sessionMaxBpm;

        [ObservableProperty] private string _todayText = "0 min";
        [ObservableProperty] private string _weekText = "0 min";
        [ObservableProperty] private string _streakText = "0 days";
        [ObservableProperty] private string _bestBpmText = "—";

        public ObservableCollection<PracticeBar> PracticeBars { get; } = new();

        /// <summary>False when no practice log is available (headless tools); the card is hidden then.</summary>
        public bool HasPracticeLog => _practiceLog is not null;

        // ── Guitar looper (everything here no-ops when _looper is null) ──
        private readonly LooperService? _looper;
        private readonly LooperDeviceChoice _automaticInput = new(null, "Automatic (guitar from Sound setup)");
        private CancellationTokenSource? _calibrateCts;
        private bool _looperDevicesBusy;
        private bool _looperDevicesSwapping;
        private bool _looperInputOpen;
        private bool _trimSyncing;
        private int _waveLayers = -1;
        private double _waveRecorded = -1;
        private bool _wavePrevRecording;

        /// <summary>False when no looper service is available (headless tools); the card is hidden then.</summary>
        public bool HasLooper => _looper is not null;

        public IReadOnlyList<LooperLengthChoice> LooperLengthOptions { get; } = new[]
        {
            new LooperLengthChoice { Name = "Free",   Bars = 0 },
            new LooperLengthChoice { Name = "1 bar",  Bars = 1 },
            new LooperLengthChoice { Name = "2 bars", Bars = 2 },
            new LooperLengthChoice { Name = "4 bars", Bars = 4 },
            new LooperLengthChoice { Name = "8 bars", Bars = 8 },
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperRecordLabel))]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        private LooperLengthChoice? _selectedLooperLength;

        public IReadOnlyList<LooperCountInChoice> LooperCountInOptions { get; } = new[]
        {
            new LooperCountInChoice { Name = "No count-in", Bars = 0 },
            new LooperCountInChoice { Name = "1 bar",       Bars = 1 },
            new LooperCountInChoice { Name = "2 bars",      Bars = 2 },
        };

        /// <summary>Count-in before a take when the drums are not playing.</summary>
        [ObservableProperty] private LooperCountInChoice? _selectedLooperCountIn;

        [ObservableProperty] private double _looperVolumePercent = 100; // 0–200
        [ObservableProperty] private int _looperLatencyMs = 60;         // 0–400
        /// <summary>The loop plays as soon as the first take ends (off: it waits for Play loop).</summary>
        [ObservableProperty] private bool _looperAutoPlay = true;
        /// <summary>Record also starts the drums when they aren't playing (off, the default: the looper works on its own).</summary>
        [ObservableProperty] private bool _looperStartsDrums;
        /// <summary>Make quiet takes louder.</summary>
        [ObservableProperty] private bool _looperAutoLevel = true;

        [ObservableProperty] private IReadOnlyList<LooperDeviceChoice> _looperInputDevices = Array.Empty<LooperDeviceChoice>();
        [ObservableProperty] private LooperDeviceChoice? _selectedLooperInputDevice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperIsRecording))]
        [NotifyPropertyChangedFor(nameof(LooperIsArmed))]
        [NotifyPropertyChangedFor(nameof(LooperIsPlaying))]
        [NotifyPropertyChangedFor(nameof(LooperHasLoop))]
        [NotifyPropertyChangedFor(nameof(LooperStopEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperClearEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperExportEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperRecordLabel))]
        [NotifyPropertyChangedFor(nameof(LooperStopLabel))]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        [NotifyPropertyChangedFor(nameof(LooperTempoMismatch))]
        [NotifyPropertyChangedFor(nameof(LooperCanTrim))]
        [NotifyPropertyChangedFor(nameof(LooperResetTrimEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperPlayhead))]
        private LooperState _looperState;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        [NotifyPropertyChangedFor(nameof(LooperLoopInfo))]
        private int _looperLoopBars;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        [NotifyPropertyChangedFor(nameof(LooperLoopInfo))]
        private int _looperLayerCount;

        /// <summary>The count-in clicks are running (state Armed).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        private bool _looperIsCountingIn;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        private int _looperCountInBeatsLeft;

        /// <summary>The loop is not tied to the drum machine's bars.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        [NotifyPropertyChangedFor(nameof(LooperLoopInfo))]
        [NotifyPropertyChangedFor(nameof(LooperTempoMismatch))]
        private bool _looperIsFreeLoop;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperStatusText))]
        [NotifyPropertyChangedFor(nameof(LooperLoopInfo))]
        [NotifyPropertyChangedFor(nameof(LooperLoopSecondsText))]
        private double _looperLoopSeconds;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperHasGainText))]
        private string _looperGainText = "";

        // ── Trim (non-destructive) ──
        /// <summary>Peaks (0..1) of the untrimmed take for the waveform strip; empty when there is no loop.</summary>
        [ObservableProperty] private float[] _looperWaveform = Array.Empty<float>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperTrimMaxMs))]
        [NotifyPropertyChangedFor(nameof(LooperTrimStartFraction))]
        [NotifyPropertyChangedFor(nameof(LooperTrimEndFraction))]
        private double _looperRecordedSeconds;

        /// <summary>Milliseconds cut from the start of the take (two-way; the service clamps so 100 ms remain).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperTrimStartFraction))]
        [NotifyPropertyChangedFor(nameof(LooperHasTrim))]
        [NotifyPropertyChangedFor(nameof(LooperResetTrimEnabled))]
        private double _looperTrimStartMs;

        /// <summary>Milliseconds cut from the end of the take (two-way).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperTrimEndFraction))]
        [NotifyPropertyChangedFor(nameof(LooperTrimEndMs))]
        [NotifyPropertyChangedFor(nameof(LooperHasTrim))]
        [NotifyPropertyChangedFor(nameof(LooperResetTrimEnabled))]
        private double _looperTrimEndCutMs;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperTempoMismatch))]
        [NotifyPropertyChangedFor(nameof(LooperTempoMismatchText))]
        [NotifyPropertyChangedFor(nameof(LooperMatchTempoText))]
        private double _looperLoopBpm;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperUndoEnabled))]
        private bool _looperCanUndo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperPositionFill))]
        [NotifyPropertyChangedFor(nameof(LooperPositionRest))]
        [NotifyPropertyChangedFor(nameof(LooperPlayhead))]
        private double _looperPositionPercent; // 0–100

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperInputLevelFill))]
        [NotifyPropertyChangedFor(nameof(LooperInputLevelRest))]
        private double _looperInputLevelPercent; // 0–100

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperHasInputDeviceName))]
        private string _looperInputDeviceName = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperHasError))]
        private string _looperError = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LooperControlsEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperStopEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperUndoEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperClearEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperExportEnabled))]
        [NotifyPropertyChangedFor(nameof(LooperCanTrim))]
        [NotifyPropertyChangedFor(nameof(LooperResetTrimEnabled))]
        private bool _isLooperCalibrating;

        [ObservableProperty] private string _looperCalibrationText = "";

        public bool LooperIsRecording => LooperText.IsRecording(LooperState);
        public bool LooperIsArmed => LooperState == LooperState.Armed;
        public bool LooperIsPlaying => LooperState == LooperState.Playing;
        public bool LooperHasLoop => LooperText.HasLoop(LooperState);
        public bool LooperHasError => LooperError.Length > 0;
        public bool LooperHasInputDeviceName => LooperInputDeviceName.Length > 0;

        public string LooperStatusText =>
            LooperText.Status(LooperState, LooperLoopBars, LooperLayerCount, SelectedLooperLength?.Bars ?? 0,
                LooperIsCountingIn, LooperCountInBeatsLeft, LooperIsFreeLoop, LooperLoopSeconds);
        public string LooperRecordLabel => LooperText.RecordLabel(LooperState, SelectedLooperLength?.Bars ?? 0);
        public string LooperStopLabel => LooperText.StopLabel(LooperState);
        public string LooperLoopInfo =>
            LooperText.LoopInfo(LooperLoopBars, LooperLayerCount, LooperIsFreeLoop, LooperLoopSeconds);
        /// <summary>"7.4 s"; empty when there is no loop.</summary>
        public string LooperLoopSecondsText => LooperLoopSeconds > 0 ? LooperText.Seconds(LooperLoopSeconds) : "";
        public bool LooperHasGainText => LooperGainText.Length > 0;

        public bool LooperTempoMismatch => LooperText.TempoMismatch(LooperState, LooperLoopBpm, Bpm, LooperIsFreeLoop);

        // Trim
        /// <summary>Largest value of the trim sliders (the service keeps 100 ms of the take).</summary>
        public double LooperTrimMaxMs => Math.Max(1.0, LooperRecordedSeconds * 1000.0 - 100.0);
        /// <summary>Absolute end of the kept part, in ms from the start of the take.</summary>
        public double LooperTrimEndMs => Math.Max(0.0, LooperRecordedSeconds * 1000.0 - LooperTrimEndCutMs);
        /// <summary>Share (0–1) of the take cut from the start / end (for the waveform shading).</summary>
        public double LooperTrimStartFraction => LooperText.TrimFraction(LooperTrimStartMs, LooperRecordedSeconds);
        public double LooperTrimEndFraction => LooperText.TrimFraction(LooperTrimEndCutMs, LooperRecordedSeconds);
        public bool LooperHasTrim => LooperTrimStartMs >= 0.5 || LooperTrimEndCutMs >= 0.5;
        /// <summary>A loop exists and isn't being recorded, overdubbed or calibrated.</summary>
        public bool LooperCanTrim => LooperHasLoop && !LooperIsRecording && !IsLooperCalibrating;
        public bool LooperResetTrimEnabled => LooperHasTrim && LooperCanTrim;
        /// <summary>Playhead for the waveform: 0..1 of the trimmed loop while playing, -1 (hidden) otherwise.</summary>
        public double LooperPlayhead =>
            LooperState is LooperState.Playing or LooperState.Overdubbing
                ? LooperText.Fraction(LooperPositionPercent / 100.0)
                : -1.0;
        public string LooperTempoMismatchText => LooperText.TempoMismatchText(LooperLoopBpm);
        public string LooperMatchTempoText => LooperText.MatchTempoText(LooperLoopBpm);

        // Enabled states (calibration disables everything else)
        public bool LooperControlsEnabled => !IsLooperCalibrating;
        public bool LooperStopEnabled => LooperHasLoop && !IsLooperCalibrating;
        public bool LooperUndoEnabled => LooperCanUndo && !IsLooperCalibrating;
        public bool LooperClearEnabled => LooperState != LooperState.Empty && !IsLooperCalibrating;
        public bool LooperExportEnabled => LooperHasLoop && !IsLooperCalibrating;

        // Fluid-width meters: two star columns whose widths are the filled / remaining fractions.
        public Avalonia.Controls.GridLength LooperPositionFill =>
            new(LooperText.Fraction(LooperPositionPercent / 100.0), Avalonia.Controls.GridUnitType.Star);
        public Avalonia.Controls.GridLength LooperPositionRest =>
            new(1.0 - LooperText.Fraction(LooperPositionPercent / 100.0), Avalonia.Controls.GridUnitType.Star);
        public Avalonia.Controls.GridLength LooperInputLevelFill =>
            new(LooperText.Fraction(LooperInputLevelPercent / 100.0), Avalonia.Controls.GridUnitType.Star);
        public Avalonia.Controls.GridLength LooperInputLevelRest =>
            new(1.0 - LooperText.Fraction(LooperInputLevelPercent / 100.0), Avalonia.Controls.GridUnitType.Star);

        /// <summary>"Input live" toggle: turning it on opens the capture device, off closes it.</summary>
        public bool LooperInputOpen
        {
            get => _looperInputOpen;
            set
            {
                if (_looper is null || IsDisposed || IsLooperCalibrating)
                {
                    OnPropertyChanged(); // snap the toggle back
                    return;
                }
                if (value == _looperInputOpen) return;

                if (value) _looper.OpenInput();
                else _looper.CloseInput();
                RefreshLooper();
                OnPropertyChanged(); // re-read in case opening failed and the state didn't change
            }
        }

        // ── Collections ──
        public ObservableCollection<BeatLight>   BeatLights   { get; } = new();
        public ObservableCollection<PatternRow>  PatternRows  { get; } = new();
        public ObservableCollection<StepMarker>  StepMarkers  { get; } = new();

        public IReadOnlyList<string> Genres { get; }

        // ── Presets ──
        public IReadOnlyList<MetronomePreset> Presets { get; } = new[]
        {
            new MetronomePreset { Name = "Largo",    Italian = "Very slow", Bpm = 50  },
            new MetronomePreset { Name = "Andante",  Italian = "Walking",   Bpm = 80  },
            new MetronomePreset { Name = "Moderato", Italian = "Moderate",  Bpm = 110 },
            new MetronomePreset { Name = "Allegro",  Italian = "Lively",    Bpm = 130 },
            new MetronomePreset { Name = "Vivace",   Italian = "Vivid",     Bpm = 160 },
            new MetronomePreset { Name = "Presto",   Italian = "Very fast", Bpm = 190 },
        };

        // Beats-per-bar choices for click mode
        public IReadOnlyList<int> BeatChoices { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12 };

        // Selectable click sounds
        public IReadOnlyList<MetronomeSoundChoice> SoundChoices { get; } = new[]
        {
            new MetronomeSoundChoice { Name = "Clock",      Sound = MetronomeSound.Clock },
            new MetronomeSoundChoice { Name = "Wood Block", Sound = MetronomeSound.WoodBlock },
            new MetronomeSoundChoice { Name = "Beep",       Sound = MetronomeSound.Beep },
            new MetronomeSoundChoice { Name = "Click",      Sound = MetronomeSound.Click },
            new MetronomeSoundChoice { Name = "Cowbell",    Sound = MetronomeSound.Cowbell },
            new MetronomeSoundChoice { Name = "Rim",        Sound = MetronomeSound.Rim },
        };

        public IReadOnlyList<SubdivisionChoice> SubdivisionChoices { get; } = new[]
        {
            new SubdivisionChoice { Name = "Quarter notes", Value = ClickSubdivision.None },
            new SubdivisionChoice { Name = "Eighths",       Value = ClickSubdivision.Eighths },
            new SubdivisionChoice { Name = "Triplets",      Value = ClickSubdivision.Triplets },
            new SubdivisionChoice { Name = "Sixteenths",    Value = ClickSubdivision.Sixteenths },
            new SubdivisionChoice { Name = "Swing",         Value = ClickSubdivision.Swing },
        };

        public IReadOnlyList<KitChoice> KitChoices { get; } = new[]
        {
            new KitChoice { Name = "Real drums",       Kind = DrumKitKind.Acoustic },
            new KitChoice { Name = "Rock kit (synth)", Kind = DrumKitKind.Rock },
            new KitChoice { Name = "Electro kit",      Kind = DrumKitKind.Electro },
            new KitChoice { Name = "Jazz kit (synth)", Kind = DrumKitKind.Jazz },
        };

        public IReadOnlyList<AutoFillChoice> AutoFillChoices { get; } = new[]
        {
            new AutoFillChoice { Name = "Off",           Bars = 0 },
            new AutoFillChoice { Name = "Every 2 bars",  Bars = 2 },
            new AutoFillChoice { Name = "Every 4 bars",  Bars = 4 },
            new AutoFillChoice { Name = "Every 8 bars",  Bars = 8 },
            new AutoFillChoice { Name = "Every 16 bars", Bars = 16 },
        };

        // ── Internal state ──
        private readonly List<BeatAccent> _accentMemory = new();
        private BeatLight? _activeLight;
        private DrumBar? _shownPattern;
        private int _prevMarker = -1;

        // Tap tempo
        private readonly Stopwatch _tapClock = Stopwatch.StartNew();
        private readonly List<long> _taps = new();

        private static readonly HashSet<string> PersistedProperties = new()
        {
            nameof(Bpm), nameof(Volume), nameof(IsDrumsMode),
            nameof(SelectedSound), nameof(BeatsPerMeasure), nameof(SelectedSubdivision),
            nameof(SelectedStyle), nameof(SelectedKit), nameof(ClickWithDrums),
            nameof(CountIn), nameof(IntroFill), nameof(SelectedAutoFill),
            nameof(TrainerEnabled), nameof(TrainerStepBpm), nameof(TrainerEveryBars), nameof(TrainerTargetBpm),
            nameof(GapEnabled), nameof(GapPlayBars), nameof(GapMuteBars),
            nameof(SelectedLooperLength), nameof(LooperVolumePercent), nameof(LooperLatencyMs),
            nameof(SelectedLooperInputDevice), nameof(LooperAutoPlay), nameof(LooperStartsDrums),
            nameof(SelectedLooperCountIn), nameof(LooperAutoLevel),
        };

        // Top-to-bottom order of the pattern grid rows.
        private static readonly DrumVoice[] RowOrder =
        {
            DrumVoice.Crash, DrumVoice.Ride, DrumVoice.RideBell, DrumVoice.OpenHat,
            DrumVoice.ClosedHat, DrumVoice.PedalHat,
            DrumVoice.Cowbell, DrumVoice.Tambourine, DrumVoice.Shaker, DrumVoice.Clap, DrumVoice.Sticks,
            DrumVoice.SideStick, DrumVoice.Snare,
            DrumVoice.HighTom, DrumVoice.MidTom, DrumVoice.FloorTom,
            DrumVoice.Kick,
        };

        private static string VoiceLabel(DrumVoice voice) => voice switch
        {
            DrumVoice.Kick       => "Kick",
            DrumVoice.Snare      => "Snare",
            DrumVoice.SideStick  => "Side stick",
            DrumVoice.Clap       => "Clap",
            DrumVoice.ClosedHat  => "Hi-hat",
            DrumVoice.PedalHat   => "Pedal hat",
            DrumVoice.OpenHat    => "Open hat",
            DrumVoice.Ride       => "Ride",
            DrumVoice.RideBell   => "Ride bell",
            DrumVoice.Crash      => "Crash",
            DrumVoice.HighTom    => "High tom",
            DrumVoice.MidTom     => "Mid tom",
            DrumVoice.FloorTom   => "Floor tom",
            DrumVoice.Cowbell    => "Cowbell",
            DrumVoice.Tambourine => "Tambourine",
            DrumVoice.Shaker     => "Shaker",
            DrumVoice.Sticks     => "Sticks",
            _                    => voice.ToString(),
        };

        public MetronomeViewModel(
            MetronomeService audio,
            MetronomeSettingsService settingsService,
            ILogger<MetronomeViewModel>? logger = null,
            PracticeLogService? practiceLog = null,
            LooperService? looper = null) : base(logger)
        {
            _audio = audio;
            _settingsService = settingsService;
            _looper = looper;
            _practiceLog = practiceLog ?? ResolvePracticeLog();
            Songs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSongs));

            MetronomeSettings s;
            try { s = settingsService.Load(); }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Couldn't load metronome settings");
                s = new MetronomeSettings();
            }

            Genres = new[] { "All" }.Concat(DrumStyleLibrary.Genres).ToList();
            var allStyles = DrumStyleLibrary.All;
            var style = DrumStyleLibrary.Find(s.StyleId) ?? allStyles.FirstOrDefault();

            // Assign BACKING FIELDS so the OnXChanged hooks (previews, toasts, saves) stay quiet.
            _bpm = Math.Clamp(s.Bpm, MinBpm, MaxBpm);
            _volume = Math.Clamp(s.Volume, 0.0, 1.0);
            _isDrumsMode = s.DrumsMode;
            _selectedSound = SoundChoices.FirstOrDefault(c => c.Sound == s.ClickSound) ?? SoundChoices[0];
            _beatsPerMeasure = BeatChoices.Contains(s.BeatsPerMeasure) ? s.BeatsPerMeasure : 4;
            _selectedSubdivision = SubdivisionChoices.FirstOrDefault(c => c.Value == s.Subdivision) ?? SubdivisionChoices[0];
            _selectedGenre = "All";
            _filteredStyles = allStyles.ToList();
            _selectedStyle = style;
            // One-time move from the synthesized Rock/Jazz kits (the old defaults) to the recorded drums.
            var kit = !s.RealDrumsOffered && s.Kit is DrumKitKind.Rock or DrumKitKind.Jazz ? DrumKitKind.Acoustic : s.Kit;
            _selectedKit = KitChoices.FirstOrDefault(k => k.Kind == kit) ?? KitChoices[0];
            _clickWithDrums = s.ClickWithDrums;
            _countIn = s.CountIn;
            _introFill = s.IntroFill;
            _selectedAutoFill = AutoFillChoices.FirstOrDefault(a => a.Bars == s.AutoFillBars) ?? AutoFillChoices[0];
            _trainerEnabled = s.TrainerEnabled;
            _trainerStepBpm = Math.Clamp(s.TrainerStepBpm, 1, 50);
            _trainerEveryBars = Math.Clamp(s.TrainerEveryBars, 1, 64);
            _trainerTargetBpm = Math.Clamp(s.TrainerTargetBpm, MinBpm, MaxBpm);
            _gapEnabled = s.GapEnabled;
            _gapPlayBars = Math.Clamp(s.GapPlayBars, 1, 16);
            _gapMuteBars = Math.Clamp(s.GapMuteBars, 1, 16);

            // Looper options (backing fields again), then push them into the service.
            int lengthBars = MetronomeSettingsHelper.LooperLengthOrDefault(s.LooperLengthBars);
            _selectedLooperLength = LooperLengthOptions.First(o => o.Bars == lengthBars);
            int countInBars = MetronomeSettingsHelper.LooperCountInOrDefault(s.LooperCountInBars);
            _selectedLooperCountIn = LooperCountInOptions.First(o => o.Bars == countInBars);
            _looperVolumePercent = double.IsNaN(s.LooperVolume) ? 100 : Math.Clamp(s.LooperVolume, 0.0, 2.0) * 100.0;
            _looperLatencyMs = Math.Clamp(s.LooperLatencyMs, 0, 400);
            _looperAutoPlay = s.LooperAutoPlay;
            _looperStartsDrums = s.LooperRecordStartsDrums;
            _looperAutoLevel = s.LooperAutoLevel;
            var initialDevices = new List<LooperDeviceChoice> { _automaticInput };
            LooperDeviceChoice selectedDevice = _automaticInput;
            if (!string.IsNullOrEmpty(s.LooperInputDeviceId))
            {
                // The real list is loaded when the dropdown first opens; until then show a stand-in for the saved device.
                selectedDevice = new LooperDeviceChoice(s.LooperInputDeviceId, "Saved input device");
                initialDevices.Add(selectedDevice);
            }
            _looperInputDevices = initialDevices;
            _selectedLooperInputDevice = selectedDevice;
            if (_looper is not null)
            {
                try
                {
                    _looper.LengthBars = lengthBars;
                    _looper.Volume = (float)(_looperVolumePercent / 100.0);
                    _looper.LatencyMs = _looperLatencyMs;
                    _looper.AutoPlay = _looperAutoPlay;
                    _looper.CountInBars = countInBars;
                    _looper.AutoLevel = _looperAutoLevel;
                    _looper.InputDeviceId = selectedDevice.Id;
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Couldn't apply the looper settings");
                }
            }

            if (s.Accents is { Count: > 0 }) _accentMemory.AddRange(s.Accents);
            else _accentMemory.Add(BeatAccent.Accent);

            // Setlists (backing fields again: the hooks would rebuild and save).
            foreach (var list in s.Setlists ?? new List<Setlist>())
                Setlists.Add(new SetlistItem(list, ScheduleSave));
            var activeList = s.ActiveSetlistId is null ? null : Setlists.FirstOrDefault(l => l.Model.Id == s.ActiveSetlistId);
            _selectedSetlist = activeList ?? Setlists.FirstOrDefault();
            RebuildSongs();
            if (activeList is not null && Songs.Count > 0)
            {
                _selectedSong = Songs[Math.Clamp(s.ActiveSongIndex, 0, Songs.Count - 1)];
                _selectedSong.IsActive = true;
            }

            RebuildBeatLights(CurrentLightCount());
            UpdatePartInfo(0);
            ShowPreview();
            RefreshPracticeStats();

            _initializing = false;
            ApplyAllToEngine();

            _audio.PositionChanged += OnPosition;

            if (_looper is not null)
            {
                RefreshLooper();
                _looper.Changed += OnLooperChanged;
            }
        }

        // =====================================================
        //  Commands
        // =====================================================

        [RelayCommand]
        private void ShowClickMode()
        {
            if (IsDisposed) return;
            IsDrumsMode = false;
        }

        [RelayCommand]
        private void ShowDrumsMode()
        {
            if (IsDisposed) return;
            IsDrumsMode = true;
        }

        [RelayCommand]
        private void TogglePlay()
        {
            if (IsDisposed) return;

            if (IsPlaying)
            {
                _audio.Stop();
                ResetLiveState();
                return;
            }

            StartPlaying();
        }

        /// <summary>Starts the drum machine / click (count-in etc. apply). Toasts and returns false when the output can't be opened.</summary>
        private bool StartPlaying()
        {
            try
            {
                ApplyAllToEngine();
                _audio.Start();
                IsPlaying = _audio.IsPlaying;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to start the metronome");
                IsPlaying = false;
            }

            if (IsPlaying)
                BeginSession();
            else
                ToastService.Instance.Error("Couldn't open the audio output");
            return IsPlaying;
        }

        // =====================================================
        //  Guitar looper
        // =====================================================

        /// <summary>
        /// The pedal: Record / Overdub / Finish. Never needs the drums: with them stopped the looper runs on
        /// its own (count-in, record, loop). When <see cref="LooperStartsDrums"/> is on, Record starts the
        /// drum machine first so the loop is bar-synced.
        /// </summary>
        [RelayCommand]
        public void LooperRecord()
        {
            if (IsDisposed || _looper is null || IsLooperCalibrating) return;
            if (!IsPlaying && LooperStartsDrums && !StartPlaying()) return;
            _looper.PressRecord();
            RefreshLooper();
        }

        [RelayCommand]
        public void LooperStop()
        {
            if (IsDisposed || _looper is null || IsLooperCalibrating) return;
            _looper.PressStop();
            RefreshLooper();
        }

        [RelayCommand]
        public void LooperUndo()
        {
            if (IsDisposed || _looper is null || IsLooperCalibrating) return;
            _looper.Undo();
            RefreshLooper();
        }

        [RelayCommand]
        public void LooperClear()
        {
            if (IsDisposed || _looper is null || IsLooperCalibrating) return;
            _looper.Clear();
            RefreshLooper();
        }

        /// <summary>Sets the tempo to the one the loop was recorded at.</summary>
        [RelayCommand]
        private void LooperMatchTempo()
        {
            if (IsDisposed || _looper is null || LooperLoopBpm <= 0) return;
            Bpm = Math.Clamp((int)Math.Round(LooperLoopBpm), MinBpm, MaxBpm);
        }

        /// <summary>Reads the device list (off the UI thread) when the dropdown first opens, or on demand.</summary>
        [RelayCommand]
        private async Task RefreshLooperDevicesAsync()
        {
            if (IsDisposed || _looper is null || _looperDevicesBusy) return;
            var looper = _looper;
            _looperDevicesBusy = true;
            try
            {
                IReadOnlyList<AudioDeviceInfo> devices;
                try
                {
                    devices = await Task.Run(() => looper.GetInputDevices());
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Couldn't list the looper input devices");
                    return;
                }
                if (IsDisposed) return;

                string? keepId = SelectedLooperInputDevice?.Id;
                var list = new List<LooperDeviceChoice> { _automaticInput };
                foreach (var d in devices) list.Add(new LooperDeviceChoice(d.Id, d.Name));

                LooperDeviceChoice selected = _automaticInput;
                if (keepId is not null)
                {
                    var match = list.FirstOrDefault(c => c.Id == keepId);
                    if (match is null)
                    {
                        // Saved device is unplugged right now: keep the choice (and the saved id) visible.
                        match = new LooperDeviceChoice(keepId, "Unavailable input device");
                        list.Add(match);
                    }
                    selected = match;
                }

                _looperDevicesSwapping = true;
                try
                {
                    LooperInputDevices = list;
                    SelectedLooperInputDevice = selected;
                }
                finally
                {
                    _looperDevicesSwapping = false;
                }

                // The ComboBox may have pushed null into the selection while its items were swapped.
                OnPropertyChanged(nameof(SelectedLooperInputDevice));
            }
            finally
            {
                _looperDevicesBusy = false;
            }
        }

        /// <summary>Stops the drum machine, then measures the input-to-output delay (the user strums along with clicks).</summary>
        [RelayCommand]
        private async Task CalibrateLooperAsync()
        {
            if (IsDisposed || _looper is null || IsLooperCalibrating) return;
            var looper = _looper;

            IsLooperCalibrating = true;
            if (IsPlaying)
            {
                _audio.Stop();
                ResetLiveState();
            }
            LooperCalibrationText = "Strum once on each of the 8 clicks…";

            var cts = new CancellationTokenSource();
            _calibrateCts = cts;
            try
            {
                int? ms = await looper.CalibrateAsync(cts.Token);
                if (IsDisposed) return;

                if (ms is int measured)
                {
                    LooperLatencyMs = Math.Clamp(measured, 0, 400);
                    ToastService.Instance.Success($"Delay measured: {LooperLatencyMs} ms");
                }
                else
                {
                    string? error = looper.LastError;
                    ToastService.Instance.Error(string.IsNullOrEmpty(error) ? "Couldn't measure the delay" : error);
                }
            }
            catch (OperationCanceledException)
            {
                // Page closed mid-calibration.
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Looper calibration failed");
                if (!IsDisposed) ToastService.Instance.Error("Couldn't measure the delay");
            }
            finally
            {
                if (ReferenceEquals(_calibrateCts, cts)) _calibrateCts = null;
                cts.Dispose();
                if (!IsDisposed)
                {
                    IsLooperCalibrating = false;
                    LooperCalibrationText = "";
                    RefreshLooper();
                }
            }
        }

        /// <summary>Writes the loop to a WAV file (the view's save picker supplies the path).</summary>
        public void ExportLoop(string path)
        {
            if (IsDisposed || _looper is null || string.IsNullOrWhiteSpace(path)) return;
            bool ok;
            try { ok = _looper.ExportWav(path); }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Couldn't export the loop");
                ok = false;
            }

            if (ok)
            {
                ToastService.Instance.Success($"Loop saved to {System.IO.Path.GetFileName(path)}");
            }
            else
            {
                string? error = _looper.LastError;
                ToastService.Instance.Error(string.IsNullOrEmpty(error) ? "Couldn't save the loop" : error);
            }
            RefreshLooper();
        }

        /// <summary>Suggested file name for the export picker.</summary>
        public string LooperExportFileName => LooperText.ExportFileName(DateTime.Now);

        private void OnLooperChanged()
        {
            if (IsDisposed) return;
            RefreshLooper();
        }

        /// <summary>Back to the whole take.</summary>
        [RelayCommand]
        private void ResetLooperTrim()
        {
            if (IsDisposed || _looper is null || !LooperCanTrim) return;
            _looper.ResetTrim();
            RefreshLooper();
        }

        /// <summary>Moves one trim slider by 10 ms; <paramref name="which"/> is "start-", "start+", "end-" or "end+".</summary>
        [RelayCommand]
        private void NudgeTrim(string? which)
        {
            if (IsDisposed || _looper is null || !LooperCanTrim) return;
            double max = LooperTrimMaxMs;
            switch (which)
            {
                case "start-": LooperTrimStartMs = Math.Clamp(LooperTrimStartMs - 10, 0, max); break;
                case "start+": LooperTrimStartMs = Math.Clamp(LooperTrimStartMs + 10, 0, max); break;
                case "end-":   LooperTrimEndCutMs = Math.Clamp(LooperTrimEndCutMs - 10, 0, max); break;
                case "end+":   LooperTrimEndCutMs = Math.Clamp(LooperTrimEndCutMs + 10, 0, max); break;
            }
        }

        /// <summary>A trim slider moved: hand both ends to the service, then show what it actually accepted.</summary>
        private void ApplyTrimFromUi()
        {
            var looper = _looper;
            if (IsDisposed || looper is null || LooperRecordedSeconds <= 0) return;
            double recordedMs = LooperRecordedSeconds * 1000.0;
            try
            {
                // The service takes the amounts cut from each side, like the sliders.
                looper.SetTrim(LooperTrimStartMs, LooperTrimEndCutMs);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Couldn't trim the loop");
            }
            ReadLooperTrim(looper);
        }

        /// <summary>Copies the service's trim into the sliders without echoing it back.</summary>
        private void ReadLooperTrim(LooperService looper)
        {
            double recordedMs = LooperRecordedSeconds * 1000.0;
            double start = Math.Max(0.0, looper.TrimStartMs);
            double cut = recordedMs <= 0 ? 0.0 : Math.Max(0.0, looper.TrimEndMs);   // ms cut from the end

            _trimSyncing = true;
            try
            {
                LooperTrimStartMs = recordedMs <= 0 ? 0.0 : start;
                LooperTrimEndCutMs = cut;
            }
            finally
            {
                _trimSyncing = false;
            }
        }

        /// <summary>Recorded length, trim and the waveform. The waveform is only re-read when the take itself changed.</summary>
        private void RefreshLooperTrim(LooperService looper)
        {
            bool hasLoop = LooperText.HasLoop(looper.State);
            bool recordingNow = LooperText.IsRecording(looper.State);
            double recorded = hasLoop ? looper.RecordedSeconds : 0.0;
            int layers = looper.LayerCount;

            LooperRecordedSeconds = recorded;
            ReadLooperTrim(looper);

            if (!hasLoop)
            {
                if (LooperWaveform.Length != 0) LooperWaveform = Array.Empty<float>();
                _waveLayers = -1;
                _waveRecorded = -1;
            }
            else if (layers != _waveLayers
                     || Math.Abs(recorded - _waveRecorded) > 0.0005
                     || (_wavePrevRecording && !recordingNow))
            {
                try { LooperWaveform = looper.GetWaveform(240) ?? Array.Empty<float>(); }
                catch (Exception ex) { Logger?.LogWarning(ex, "Couldn't read the loop waveform"); }
                _waveLayers = layers;
                _waveRecorded = recorded;
            }
            _wavePrevRecording = recordingNow;
        }

        /// <summary>Mirrors the service's state into the bindable properties (setters ignore unchanged values).</summary>
        private void RefreshLooper()
        {
            var looper = _looper;
            if (looper is null) return;

            LooperState = looper.State;
            LooperLoopBars = looper.LoopBars;
            LooperLayerCount = looper.LayerCount;
            LooperLoopBpm = looper.LoopBpm;
            LooperIsCountingIn = looper.IsCountingIn;
            LooperCountInBeatsLeft = looper.CountInBeatsLeft;
            LooperIsFreeLoop = looper.IsFreeLoop;
            LooperLoopSeconds = looper.LoopSeconds;
            LooperGainText = LooperText.GainText(looper.LoopGain, looper.AutoLevel);
            LooperCanUndo = looper.CanUndo;
            LooperPositionPercent = LooperText.Fraction(looper.Position) * 100.0;
            RefreshLooperTrim(looper);
            LooperInputLevelPercent = LooperText.Fraction(looper.InputLevel) * 100.0;
            LooperInputDeviceName = looper.InputDeviceName ?? "";
            LooperError = looper.LastError ?? "";

            bool open = looper.IsInputOpen;
            if (open != _looperInputOpen)
            {
                _looperInputOpen = open;
                OnPropertyChanged(nameof(LooperInputOpen));
            }
        }

        [RelayCommand]
        private void Fill()
        {
            if (IsDisposed || !IsPlaying || !IsDrumsMode) return;
            Engine.RequestFill();
        }

        [RelayCommand]
        private void NextPart()
        {
            if (IsDisposed || !IsPlaying || !IsDrumsMode) return;
            Engine.RequestNextPart();
        }

        [RelayCommand]
        private void Outro()
        {
            if (IsDisposed || !IsPlaying || !IsDrumsMode) return;
            Engine.RequestOutro();
        }

        [RelayCommand]
        private void AccentHit()
        {
            if (IsDisposed) return;
            _audio.AccentHit();
        }

        [RelayCommand]
        private void CycleAccent(BeatLight? light)
        {
            if (IsDisposed || light is null) return;

            light.Accent = light.Accent switch
            {
                BeatAccent.Accent => BeatAccent.Normal,
                BeatAccent.Normal => BeatAccent.Mute,
                _                 => BeatAccent.Accent,
            };

            SyncAccentMemory();
            Engine.SetAccents(CurrentAccents());
            ScheduleSave();
        }

        [RelayCommand]
        private void ApplyPreset(MetronomePreset? preset)
        {
            if (IsDisposed || preset is null) return;
            Bpm = preset.Bpm;
            ToastService.Instance.Info($"{preset.Name} · {preset.Bpm} BPM");
        }

        [RelayCommand]
        private void TapTempo()
        {
            if (IsDisposed) return;

            long now = _tapClock.ElapsedMilliseconds;
            if (_taps.Count > 0 && now - _taps[^1] > 2000)
                _taps.Clear();

            _taps.Add(now);
            while (_taps.Count > 6) _taps.RemoveAt(0);

            if (_taps.Count < 2) return;

            double avg = (double)(_taps[^1] - _taps[0]) / (_taps.Count - 1);
            if (avg <= 0) return;

            int detected = (int)Math.Round(60_000.0 / avg);
            if (detected is < MinBpm or > MaxBpm) return;
            Bpm = detected;
        }

        [RelayCommand]
        private void BpmDecrease() { if (!IsDisposed) Bpm = Math.Max(MinBpm, Bpm - 1); }
        [RelayCommand]
        private void BpmIncrease() { if (!IsDisposed) Bpm = Math.Min(MaxBpm, Bpm + 1); }
        [RelayCommand]
        private void BpmDownFive() { if (!IsDisposed) Bpm = Math.Max(MinBpm, Bpm - 5); }
        [RelayCommand]
        private void BpmUpFive() { if (!IsDisposed) Bpm = Math.Min(MaxBpm, Bpm + 5); }

        // =====================================================
        //  Setlists
        // =====================================================

        [RelayCommand]
        private void NewSetlist()
        {
            if (IsDisposed) return;
            CreateSetlist();
            IsRenamingSetlist = true; // type the name right away
        }

        private SetlistItem CreateSetlist()
        {
            int n = Setlists.Count + 1;
            while (Setlists.Any(l => l.Name == $"Setlist {n}")) n++;

            var item = new SetlistItem(new Setlist { Name = $"Setlist {n}" }, ScheduleSave);
            Setlists.Add(item);
            SelectedSetlist = item; // the hook rebuilds the song list and saves
            return item;
        }

        /// <summary>Toggles in-place renaming of the selected setlist (the name TextBox edits it directly).</summary>
        [RelayCommand]
        private void RenameSetlist()
        {
            if (IsDisposed) return;
            if (SelectedSetlist is null)
            {
                IsRenamingSetlist = false;
                return;
            }

            if (IsRenamingSetlist && string.IsNullOrWhiteSpace(SelectedSetlist.Name))
                SelectedSetlist.Name = "Setlist";
            IsRenamingSetlist = !IsRenamingSetlist;
        }

        [RelayCommand]
        private void DeleteSetlist()
        {
            if (IsDisposed || SelectedSetlist is not { } current) return;

            int index = Setlists.IndexOf(current);
            SetlistItem? next = Setlists.Count > 1
                ? Setlists[index + 1 < Setlists.Count ? index + 1 : index - 1]
                : null;

            SelectedSetlist = next; // move off the doomed item first so the picker never sees a dangling selection
            Setlists.Remove(current);
            IsRenamingSetlist = false;
            ScheduleSave();
        }

        /// <summary>Saves the page as it is right now (tempo, click/drums, style, kit, beats, subdivision, count-in) as a new song.</summary>
        [RelayCommand]
        private void AddCurrentAsSong()
        {
            if (IsDisposed) return;

            var list = SelectedSetlist ?? CreateSetlist();
            var song = new SetlistSong
            {
                Name = IsDrumsMode ? SelectedStyle?.Name ?? "Drums" : $"Click {Bpm}",
            };
            CaptureCurrentInto(song);

            list.Model.Songs.Add(song);
            var item = NewSongItem(song);
            Songs.Add(item);
            Renumber();
            SelectedSong = item;
            ScheduleSave();
        }

        /// <summary>Overwrites the active song's settings with the page's current ones (name unchanged).</summary>
        [RelayCommand]
        private void UpdateSongFromCurrent()
        {
            if (IsDisposed) return;
            if (SelectedSong is not { } item)
            {
                ToastService.Instance.Info("Load or add a song first");
                return;
            }

            CaptureCurrentInto(item.Song);
            item.Refresh();
            ScheduleSave();
            ToastService.Instance.Info($"Updated {item.DisplayName} · {item.Song.Bpm} BPM");
        }

        [RelayCommand]
        private void RemoveSong(SetlistSongItem? item)
        {
            if (IsDisposed || item is null || SelectedSetlist is not { } list) return;

            list.Model.Songs.Remove(item.Song);
            if (ReferenceEquals(SelectedSong, item)) SelectedSong = null;
            Songs.Remove(item);
            Renumber();
            ScheduleSave();
        }

        [RelayCommand]
        private void MoveSongUp(SetlistSongItem? item) => MoveSong(item, -1);

        [RelayCommand]
        private void MoveSongDown(SetlistSongItem? item) => MoveSong(item, +1);

        private void MoveSong(SetlistSongItem? item, int delta)
        {
            if (IsDisposed || item is null || SelectedSetlist is not { } list) return;

            int from = Songs.IndexOf(item);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= Songs.Count) return;

            Songs.Move(from, to);
            var songs = list.Model.Songs;
            songs.RemoveAt(from);
            songs.Insert(to, item.Song);
            Renumber();
            ScheduleSave();
        }

        /// <summary>
        /// Applies a song to the page exactly as if the user had set each control: tempo, click/drums mode,
        /// style, kit, beats per bar, subdivision and count-in. The song's tempo and kit win over the
        /// style's defaults. If the metronome is playing it keeps playing with the new settings.
        /// </summary>
        [RelayCommand]
        public void LoadSong(SetlistSong? song)
        {
            if (IsDisposed || song is null) return;

            // Style first, silently: picking a style through the normal path would reset Bpm and Kit.
            var style = DrumStyleLibrary.Find(song.StyleId);
            bool styleChanged = style is not null && !ReferenceEquals(style, SelectedStyle);
            if (styleChanged)
            {
                if (!FilteredStyles.Contains(style!))
                    SelectedGenre = "All"; // keeps the current style, so no style-change side effects
                _suppressStyleChange = true;
                try { SelectedStyle = style; }
                finally { _suppressStyleChange = false; }
                UpdatePartInfo(0);
            }

            // The song's own values come after, so they win.
            var kit = KitChoices.FirstOrDefault(k => k.Kind == song.Kit);
            if (kit is not null) SelectedKit = kit;
            Bpm = Math.Clamp(song.Bpm, MinBpm, MaxBpm);
            CountIn = song.CountIn;
            BeatsPerMeasure = NearestBeatChoice(song.BeatsPerMeasure);
            SelectedSubdivision = SubdivisionChoices.FirstOrDefault(c => c.Value == song.Subdivision) ?? SelectedSubdivision;

            if (IsDrumsMode != song.DrumsMode)
                IsDrumsMode = song.DrumsMode; // applies the style/click mode, lights and preview
            else if (styleChanged && IsDrumsMode)
                Engine.SetStyle(SelectedStyle);

            if (BeatLights.Count != CurrentLightCount())
                RebuildBeatLights(CurrentLightCount());
            if (!IsPlaying) ShowPreview();

            var item = Songs.FirstOrDefault(i => ReferenceEquals(i.Song, song));
            if (item is not null) SelectedSong = item;
            ScheduleSave();
        }

        /// <summary>Loads the next song of the setlist (wraps to the first). Global hotkeys call this.</summary>
        [RelayCommand]
        public void NextSong() => StepSong(+1);

        /// <summary>Loads the previous song of the setlist (wraps to the last). Global hotkeys call this.</summary>
        [RelayCommand]
        public void PreviousSong() => StepSong(-1);

        private void StepSong(int delta)
        {
            if (IsDisposed) return;
            if (Songs.Count == 0)
            {
                ToastService.Instance.Info("The setlist has no songs yet");
                return;
            }

            int current = SelectedSong is null ? -1 : Songs.IndexOf(SelectedSong);
            int index = SetlistHelper.Step(current, delta, Songs.Count);
            var item = Songs[index];
            LoadSong(item.Song);
            ToastService.Instance.Info($"Song {index + 1}/{Songs.Count} · {item.DisplayName} · {item.Song.Bpm} BPM");
        }

        private void CaptureCurrentInto(SetlistSong song)
        {
            song.Bpm = Bpm;
            song.DrumsMode = IsDrumsMode;
            song.StyleId = SelectedStyle?.Id;
            song.Kit = SelectedKit?.Kind ?? DrumKitKind.Acoustic;
            song.BeatsPerMeasure = BeatsPerMeasure;
            song.Subdivision = SelectedSubdivision?.Value ?? ClickSubdivision.None;
            song.CountIn = CountIn;
        }

        private int NearestBeatChoice(int beats) =>
            BeatChoices.OrderBy(c => Math.Abs(c - beats)).First();

        private SetlistSongItem NewSongItem(SetlistSong song) =>
            new(song, DescribeSong, ScheduleSave);

        private static string DescribeSong(SetlistSong song) =>
            SetlistHelper.Describe(song, song.DrumsMode ? DrumStyleLibrary.Find(song.StyleId)?.Name : null);

        private void RebuildSongs()
        {
            Songs.Clear();
            if (SelectedSetlist is { } list)
                foreach (var song in list.Model.Songs)
                    Songs.Add(NewSongItem(song));
            Renumber();
        }

        private void Renumber()
        {
            for (int i = 0; i < Songs.Count; i++) Songs[i].Number = i + 1;
        }

        // =====================================================
        //  Engine sync
        // =====================================================

        private void ApplyAllToEngine()
        {
            var engine = Engine;
            engine.Bpm = Bpm;
            engine.MasterVolume = (float)Volume;
            engine.Humanize = 0.08f;

            if (SelectedSound is not null) engine.ClickSound = SelectedSound.Sound;
            if (SelectedSubdivision is not null) engine.Subdivision = SelectedSubdivision.Value;
            if (SelectedKit is not null) _audio.SetKit(SelectedKit.Kind);

            engine.CountInEnabled = CountIn;
            engine.IntroEnabled = IntroFill;
            engine.AutoFillEveryBars = SelectedAutoFill?.Bars ?? 0;
            ApplyGap();

            engine.SetClickBeats(BeatsPerMeasure);
            engine.SetAccents(CurrentAccents());
            ApplyMode();
        }

        private void ApplyMode()
        {
            var engine = Engine;
            if (IsDrumsMode)
            {
                engine.SetStyle(SelectedStyle);
                engine.ClickEnabled = ClickWithDrums;
            }
            else
            {
                engine.SetStyle(null);
                engine.ClickEnabled = true;
                engine.SetClickBeats(BeatsPerMeasure);
            }
        }

        private void ApplyGap()
        {
            Engine.SetGap(GapEnabled ? (int)GapPlayBars : 0, GapEnabled ? (int)GapMuteBars : 0);
        }

        // =====================================================
        //  Property reactions
        // =====================================================

        partial void OnBpmChanged(int value)
        {
            if (value < MinBpm || value > MaxBpm)
            {
                Bpm = Math.Clamp(value, MinBpm, MaxBpm);
                return;
            }
            if (_sessionStart is not null && value > _sessionMaxBpm) _sessionMaxBpm = value;
            if (_initializing || IsDisposed) return;
            Engine.Bpm = value;
        }

        partial void OnSelectedLooperLengthChanged(LooperLengthChoice? value)
        {
            if (_initializing || IsDisposed || value is null || _looper is null) return;
            _looper.LengthBars = value.Bars;
        }

        partial void OnLooperVolumePercentChanged(double value)
        {
            double clamped = double.IsNaN(value) ? 100 : Math.Clamp(value, 0.0, 200.0);
            if (clamped != value) { LooperVolumePercent = clamped; return; }
            if (_initializing || IsDisposed || _looper is null) return;
            _looper.Volume = (float)(clamped / 100.0);
        }

        partial void OnLooperLatencyMsChanged(int value)
        {
            int clamped = Math.Clamp(value, 0, 400);
            if (clamped != value) { LooperLatencyMs = clamped; return; }
            if (_initializing || IsDisposed || _looper is null) return;
            _looper.LatencyMs = clamped;
        }

        partial void OnLooperAutoPlayChanged(bool value)
        {
            if (_initializing || IsDisposed || _looper is null) return;
            _looper.AutoPlay = value;
        }

        partial void OnLooperAutoLevelChanged(bool value)
        {
            if (_initializing || IsDisposed || _looper is null) return;
            _looper.AutoLevel = value;
            RefreshLooper(); // the gain text changes with it
        }

        partial void OnSelectedLooperCountInChanged(LooperCountInChoice? value)
        {
            if (_initializing || IsDisposed || value is null || _looper is null) return;
            _looper.CountInBars = value.Bars;
        }

        partial void OnLooperTrimStartMsChanged(double value)
        {
            if (!_trimSyncing) ApplyTrimFromUi();
        }

        partial void OnLooperTrimEndCutMsChanged(double value)
        {
            if (!_trimSyncing) ApplyTrimFromUi();
        }

        partial void OnSelectedLooperInputDeviceChanged(LooperDeviceChoice? value)
        {
            // null arrives while the ComboBox swaps its items; the device list refresh restores the selection.
            if (_initializing || _looperDevicesSwapping || IsDisposed || value is null || _looper is null) return;
            _looper.InputDeviceId = value.Id;
        }

        partial void OnSelectedSetlistChanged(SetlistItem? value)
        {
            if (_initializing || IsDisposed) return;
            IsRenamingSetlist = false;
            SelectedSong = null;
            RebuildSongs();
            ScheduleSave();
        }

        partial void OnSelectedSongChanged(SetlistSongItem? oldValue, SetlistSongItem? newValue)
        {
            if (oldValue is not null) oldValue.IsActive = false;
            if (newValue is not null) newValue.IsActive = true;
            ScheduleSave();
        }

        partial void OnVolumeChanged(double value)
        {
            if (_initializing || IsDisposed) return;
            Engine.MasterVolume = (float)Math.Clamp(value, 0.0, 1.0);
        }

        partial void OnIsDrumsModeChanged(bool value)
        {
            if (_initializing || IsDisposed) return;
            ApplyMode();
            RebuildBeatLights(CurrentLightCount());
            if (!IsPlaying) ShowPreview();
        }

        partial void OnSelectedSoundChanged(MetronomeSoundChoice? value)
        {
            if (_initializing || IsDisposed || value is null) return;
            Engine.ClickSound = value.Sound;
            // Preview the chosen sound so the user hears the difference immediately
            if (!IsPlaying) _audio.PreviewClick(value.Sound);
        }

        partial void OnBeatsPerMeasureChanged(int value)
        {
            if (_initializing || IsDisposed) return;
            Engine.SetClickBeats(value);
            if (IsClickMode) RebuildBeatLights(value);
        }

        partial void OnSelectedSubdivisionChanged(SubdivisionChoice? value)
        {
            if (_initializing || IsDisposed || value is null) return;
            Engine.Subdivision = value.Value;
        }

        partial void OnSelectedGenreChanged(string value)
        {
            if (_initializing || IsDisposed) return;
            RefillStyles();
        }

        partial void OnSelectedStyleChanged(DrumStyle? value)
        {
            if (_initializing || _suppressStyleChange || IsDisposed || value is null) return;
            ApplyStyleChoice(value);
        }

        partial void OnSelectedKitChanged(KitChoice? value)
        {
            if (_initializing || IsDisposed || value is null) return;
            _audio.SetKit(value.Kind);
        }

        partial void OnClickWithDrumsChanged(bool value)
        {
            if (_initializing || IsDisposed) return;
            if (IsDrumsMode) Engine.ClickEnabled = value;
        }

        partial void OnCountInChanged(bool value)
        {
            if (_initializing || IsDisposed) return;
            Engine.CountInEnabled = value;
        }

        partial void OnIntroFillChanged(bool value)
        {
            if (_initializing || IsDisposed) return;
            Engine.IntroEnabled = value;
        }

        partial void OnSelectedAutoFillChanged(AutoFillChoice? value)
        {
            if (_initializing || IsDisposed || value is null) return;
            Engine.AutoFillEveryBars = value.Bars;
        }

        partial void OnTrainerStepBpmChanged(decimal value) => ClampDecimal(value, 1, 50, v => TrainerStepBpm = v);
        partial void OnTrainerEveryBarsChanged(decimal value) => ClampDecimal(value, 1, 64, v => TrainerEveryBars = v);
        partial void OnTrainerTargetBpmChanged(decimal value) => ClampDecimal(value, MinBpm, MaxBpm, v => TrainerTargetBpm = v);

        partial void OnGapEnabledChanged(bool value)
        {
            if (_initializing || IsDisposed) return;
            ApplyGap();
        }

        partial void OnGapPlayBarsChanged(decimal value) => ClampDecimal(value, 1, 16, v => GapPlayBars = v, ApplyGapIfReady);
        partial void OnGapMuteBarsChanged(decimal value) => ClampDecimal(value, 1, 16, v => GapMuteBars = v, ApplyGapIfReady);

        private void ApplyGapIfReady()
        {
            if (_initializing || IsDisposed) return;
            ApplyGap();
        }

        /// <summary>Snaps a NumericUpDown value to a whole number inside [min, max]; re-assigns when it had to change.</summary>
        private static void ClampDecimal(decimal value, int min, int max, Action<decimal> assign, Action? onValid = null)
        {
            decimal clamped = Math.Clamp(Math.Round(value), min, max);
            if (clamped != value) { assign(clamped); return; }
            onValid?.Invoke();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName is not null && PersistedProperties.Contains(e.PropertyName))
                ScheduleSave();
        }

        // =====================================================
        //  Styles
        // =====================================================

        private void RefillStyles()
        {
            var genre = SelectedGenre;
            var list = (genre == "All"
                ? DrumStyleLibrary.All
                : DrumStyleLibrary.All.Where(s => s.Genre == genre)).ToList();

            var keep = SelectedStyle;
            DrumStyle? target;

            _suppressStyleChange = true;
            try
            {
                FilteredStyles = list;
                target = keep is not null && list.Contains(keep) ? keep : list.FirstOrDefault();
                SelectedStyle = target;
            }
            finally
            {
                _suppressStyleChange = false;
            }

            // The ComboBox may have pushed null into SelectedStyle while its items were swapped.
            OnPropertyChanged(nameof(SelectedStyle));

            if (target is not null && !ReferenceEquals(target, keep))
                ApplyStyleChoice(target);
        }

        /// <summary>The user picked (or the genre filter forced) a different style.</summary>
        private void ApplyStyleChoice(DrumStyle style)
        {
            Bpm = style.DefaultBpm;
            // Real drums for everything but the electronic styles.
            var suggested = style.SuggestedKit == DrumKitKind.Electro ? DrumKitKind.Electro : DrumKitKind.Acoustic;
            SelectedKit = KitChoices.FirstOrDefault(k => k.Kind == suggested) ?? SelectedKit;
            UpdatePartInfo(0);

            if (IsDrumsMode)
            {
                Engine.SetStyle(style);
                RebuildBeatLights(style.Beats);
            }

            if (!IsPlaying) ShowPreview();
        }

        private void UpdatePartInfo(int partIndex)
        {
            var parts = SelectedStyle?.Parts;
            if (parts is null || parts.Count < 2)
            {
                HasMultipleParts = false;
                NextPartName = "";
                return;
            }

            HasMultipleParts = true;
            NextPartName = parts[((partIndex % parts.Count) + 1) % parts.Count].Name;
        }

        // =====================================================
        //  Beat lights
        // =====================================================

        private int CurrentLightCount() =>
            Math.Max(1, IsDrumsMode ? SelectedStyle?.Beats ?? 4 : BeatsPerMeasure);

        private BeatAccent MemoryAccent(int index) =>
            index < _accentMemory.Count ? _accentMemory[index] : BeatAccent.Normal;

        private void SyncAccentMemory()
        {
            for (int i = 0; i < BeatLights.Count; i++)
            {
                while (_accentMemory.Count <= i) _accentMemory.Add(BeatAccent.Normal);
                _accentMemory[i] = BeatLights[i].Accent;
            }
        }

        private List<BeatAccent> CurrentAccents() => BeatLights.Select(l => l.Accent).ToList();

        private void RebuildBeatLights(int count)
        {
            count = Math.Max(1, count);
            SyncAccentMemory();
            _activeLight = null;
            BeatLights.Clear();
            for (int i = 0; i < count; i++)
                BeatLights.Add(new BeatLight(i + 1, MemoryAccent(i)));
            Engine.SetAccents(CurrentAccents());
        }

        private void SetActiveLight(int index)
        {
            if (_activeLight is not null)
            {
                _activeLight.IsActive = false;
                _activeLight = null;
            }
            if (index >= 0 && index < BeatLights.Count)
            {
                _activeLight = BeatLights[index];
                _activeLight.IsActive = true;
            }
        }

        // =====================================================
        //  Pattern grid
        // =====================================================

        private void ShowPreview()
        {
            var style = SelectedStyle;
            var bar = style?.Parts.FirstOrDefault()?.Main.FirstOrDefault();
            if (style is null || bar is null)
            {
                _shownPattern = null;
                PatternRows.Clear();
                StepMarkers.Clear();
                _prevMarker = -1;
                HasPattern = false;
                return;
            }

            if (ReferenceEquals(_shownPattern, bar) && StepMarkers.Count == bar.Steps)
                ClearPlayhead();
            else
                ShowPattern(bar, style.Beats);
        }

        private void ShowPattern(DrumBar bar, int beats)
        {
            _shownPattern = bar;
            PatternRows.Clear();
            StepMarkers.Clear();
            _prevMarker = -1;

            int steps = bar.Steps;
            int perBeat = Math.Max(1, steps / Math.Max(1, beats));

            // Size the cells so an 8-step bossa bar and a 24-step 12/8 bar both use the card's width
            // (each cell has a 3px gap, each beat a further 6px).
            double width = Math.Clamp(Math.Floor((GridWidth - beats * 6.0) / steps) - 3, 14, 36);

            for (int s = 0; s < steps; s++)
                StepMarkers.Add(new StepMarker(s % perBeat == 0, width));

            foreach (var voice in RowOrder)
            {
                if (!bar.Voices.Contains(voice)) continue;

                var cells = new PatternCell[steps];
                for (int s = 0; s < steps; s++)
                    cells[s] = new PatternCell(bar.VelocityAt(voice, s), s % perBeat == 0, width);

                PatternRows.Add(new PatternRow(VoiceLabel(voice), cells));
            }

            HasPattern = PatternRows.Count > 0;
        }

        private void SetPlayhead(int step)
        {
            if (step < 0 || step >= StepMarkers.Count || step == _prevMarker) return;
            ClearPlayhead();
            StepMarkers[step].IsCurrent = true;
            _prevMarker = step;
        }

        private void ClearPlayhead()
        {
            if (_prevMarker >= 0 && _prevMarker < StepMarkers.Count)
                StepMarkers[_prevMarker].IsCurrent = false;
            _prevMarker = -1;
        }

        // =====================================================
        //  Live position
        // =====================================================

        private void OnPosition(DrumEngineEvent e)
        {
            if (IsDisposed) return;

            if (e.Section == DrumSection.Stopped)
            {
                // Stop then Start in quick succession: the old run's Stopped event arrives after
                // the new run began (events are delivered when audible). It must not flip the UI back.
                if (!_audio.IsPlaying) ResetLiveState();
                return;
            }

            // Late events after the user pressed Stop are ignored until the Stopped event.
            if (!IsPlaying) return;

            // Speed trainer: raise the tempo at the start of every Nth bar.
            if (e.Section == DrumSection.Main && e.Step == 0 && e.Bar > 1
                && TrainerEnabled
                && (e.Bar - 1) % Math.Max(1, (int)TrainerEveryBars) == 0
                && Bpm < (int)TrainerTargetBpm)
            {
                Bpm = Math.Min((int)TrainerTargetBpm, Bpm + (int)TrainerStepBpm);
            }

            CurrentBeat = e.Beat;
            BarNumber = e.Bar;
            IsGapMuted = e.Muted;
            UpdatePartInfo(e.PartIndex);
            SectionLabel = BuildSectionLabel(e);

            if (e.BeatsInBar > 0 && e.BeatsInBar != BeatLights.Count)
                RebuildBeatLights(e.BeatsInBar);
            if (e.IsBeatStart)
                SetActiveLight(e.Beat - 1);

            if (e.Pattern is not null)
            {
                if (!ReferenceEquals(_shownPattern, e.Pattern) || StepMarkers.Count != e.Pattern.Steps)
                    ShowPattern(e.Pattern, e.BeatsInBar);
                SetPlayhead(e.Step);
            }
        }

        private string BuildSectionLabel(DrumEngineEvent e)
        {
            switch (e.Section)
            {
                case DrumSection.CountIn: return "COUNT-IN";
                case DrumSection.Intro:   return "INTRO";
            }

            if (e.Pattern is null) return "CLICK";

            switch (e.Section)
            {
                case DrumSection.Fill:   return "FILL";
                case DrumSection.Outro:  return "OUTRO";
                case DrumSection.Transition:
                    return NextPartName.Length > 0 ? "TO " + NextPartName.ToUpperInvariant() : "TRANSITION";
                default:
                    var parts = SelectedStyle?.Parts;
                    if (parts is not null && e.PartIndex >= 0 && e.PartIndex < parts.Count)
                        return parts[e.PartIndex].Name.ToUpperInvariant();
                    return "GROOVE";
            }
        }

        private void ResetLiveState()
        {
            EndSession();
            IsPlaying = false;
            CurrentBeat = 0;
            BarNumber = 0;
            SectionLabel = "";
            IsGapMuted = false;
            SetActiveLight(-1);
            ClearPlayhead();
            UpdatePartInfo(0);
            ShowPreview();
        }

        // =====================================================
        //  Practice log
        // =====================================================

        private static PracticeLogService? ResolvePracticeLog()
        {
            try { return App.Services.GetService(typeof(PracticeLogService)) as PracticeLogService; }
            catch { return null; } // no DI host (unit tests, tools/page-render)
        }

        private void BeginSession()
        {
            if (_sessionStart is not null) return;
            _sessionStart = DateTime.Now;
            _sessionClock = Stopwatch.StartNew();
            _sessionMaxBpm = Bpm;
        }

        /// <summary>Records the running session (if any) in the practice log. Safe to call repeatedly.</summary>
        private void EndSession()
        {
            if (_sessionStart is not { } start) return;
            var elapsed = _sessionClock?.Elapsed ?? TimeSpan.Zero;
            int maxBpm = Math.Max(_sessionMaxBpm, Bpm);
            _sessionStart = null;
            _sessionClock = null;

            if (_practiceLog is null) return;
            try
            {
                string activity = IsDrumsMode ? SelectedStyle?.Name ?? "Drums" : "Click";
                if (_practiceLog.RecordSession(start, elapsed, maxBpm, activity))
                    RefreshPracticeStats();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Couldn't record the practice session");
            }
        }

        private void RefreshPracticeStats()
        {
            var log = _practiceLog;
            if (log is null) return;

            TodayText = PracticeText.Duration(log.TodayTotal);
            WeekText = PracticeText.Duration(log.ThisWeekTotal);
            int streak = log.Streak;
            StreakText = streak == 1 ? "1 day" : $"{streak} days";
            int best = log.BestBpmLast30Days;
            BestBpmText = best > 0 ? $"{best} BPM" : "—";

            var days = log.Last14Days;
            double scale = Math.Max(10, days.Count == 0 ? 0 : days.Max(d => d.Minutes));
            PracticeBars.Clear();
            for (int i = 0; i < days.Count; i++)
                PracticeBars.Add(new PracticeBar(days[i].Date, days[i].Minutes, scale, isToday: i == days.Count - 1));
        }

        // =====================================================
        //  Persistence
        // =====================================================

        private void ScheduleSave()
        {
            if (_initializing || IsDisposed) return;

            if (_saveTimer is null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _saveTimer.Tick += (_, _) =>
                {
                    _saveTimer?.Stop();
                    SaveNow();
                };
            }

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            try
            {
                SyncAccentMemory();
                _settingsService.Save(new MetronomeSettings
                {
                    Bpm = Bpm,
                    Volume = Volume,
                    DrumsMode = IsDrumsMode,
                    ClickSound = SelectedSound?.Sound ?? MetronomeSound.Clock,
                    BeatsPerMeasure = BeatsPerMeasure,
                    Subdivision = SelectedSubdivision?.Value ?? ClickSubdivision.None,
                    Accents = _accentMemory.ToList(),
                    StyleId = SelectedStyle?.Id,
                    Kit = SelectedKit?.Kind ?? DrumKitKind.Acoustic,
                    RealDrumsOffered = true,
                    ClickWithDrums = ClickWithDrums,
                    CountIn = CountIn,
                    IntroFill = IntroFill,
                    AutoFillBars = SelectedAutoFill?.Bars ?? 0,
                    TrainerEnabled = TrainerEnabled,
                    TrainerStepBpm = (int)TrainerStepBpm,
                    TrainerEveryBars = (int)TrainerEveryBars,
                    TrainerTargetBpm = (int)TrainerTargetBpm,
                    GapEnabled = GapEnabled,
                    GapPlayBars = (int)GapPlayBars,
                    GapMuteBars = (int)GapMuteBars,
                    LooperLengthBars = SelectedLooperLength?.Bars ?? 4,
                    LooperVolume = Math.Clamp(LooperVolumePercent / 100.0, 0.0, 2.0),
                    LooperLatencyMs = Math.Clamp(LooperLatencyMs, 0, 400),
                    LooperAutoPlay = LooperAutoPlay,
                    LooperRecordStartsDrums = LooperStartsDrums,
                    LooperCountInBars = SelectedLooperCountIn?.Bars ?? 1,
                    LooperAutoLevel = LooperAutoLevel,
                    LooperInputDeviceId = SelectedLooperInputDevice?.Id,
                    Setlists = Setlists.Select(l => l.Model).ToList(),
                    ActiveSetlistId = SelectedSetlist?.Model.Id,
                    ActiveSongIndex = SelectedSong is null ? 0 : Math.Max(0, Songs.IndexOf(SelectedSong)),
                });
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Couldn't save metronome settings");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _audio.PositionChanged -= OnPosition;
                if (_looper is not null) _looper.Changed -= OnLooperChanged; // the service itself belongs to DI
                try { _calibrateCts?.Cancel(); } catch (ObjectDisposedException) { }
                _saveTimer?.Stop();
                SaveNow();
                _audio.Stop();
                EndSession();
            }
            base.Dispose(disposing);
        }
    }
}
