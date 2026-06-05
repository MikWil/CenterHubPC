using System;
using System.Collections.Generic;
using Avalonia.Threading;
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

    public partial class MetronomeViewModel : BaseViewModel
    {
        private readonly MetronomeService _audio;
        private DispatcherTimer? _tick;

        // ── User config ──
        [ObservableProperty] private int  _bpm = 120;
        [ObservableProperty] private int  _beatsPerMeasure = 4;
        [ObservableProperty] private bool _accentEnabled  = true;

        // ── Live state ──
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private int  _currentBeat;          // 1..N during play, 0 when idle
        [ObservableProperty] private bool _pulse;                // toggled each beat for the visual ring
        [ObservableProperty] private string _intervalDisplay = ""; // "500 ms" — debug-ish but useful

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

        // Time-signature choices for the picker
        public IReadOnlyList<int> BeatChoices { get; } = new[] { 2, 3, 4, 5, 6, 7, 8 };

        // ── Tap-tempo state ──
        private readonly List<DateTime> _tapHistory = new();

        public MetronomeViewModel(
            MetronomeService audio,
            ILogger<MetronomeViewModel>? logger = null) : base(logger)
        {
            _audio = audio;
            UpdateIntervalDisplay();
        }

        // =====================================================
        //  Commands
        // =====================================================

        [RelayCommand]
        private void TogglePlay()
        {
            if (IsPlaying) Stop();
            else Start();
        }

        [RelayCommand]
        private void ApplyPreset(MetronomePreset? preset)
        {
            if (preset is null) return;
            Bpm = preset.Bpm;
            ToastService.Instance.Info($"{preset.Name} · {preset.Bpm} BPM");
        }

        [RelayCommand]
        private void TapTempo()
        {
            var now = DateTime.Now;
            // Keep only taps from the last 3 seconds — older taps don't help
            _tapHistory.RemoveAll(t => (now - t).TotalSeconds > 3);
            _tapHistory.Add(now);

            if (_tapHistory.Count < 2) return;

            // Average the gap between the most recent taps
            var first = _tapHistory[0];
            var last  = _tapHistory[^1];
            var avg   = (last - first).TotalMilliseconds / (_tapHistory.Count - 1);
            if (avg <= 0) return;

            var detectedBpm = (int)Math.Round(60_000.0 / avg);
            if (detectedBpm is < 40 or > 220) return;
            Bpm = detectedBpm;
        }

        [RelayCommand]
        private void BpmDecrease() { Bpm = Math.Max(40, Bpm - 1); }
        [RelayCommand]
        private void BpmIncrease() { Bpm = Math.Min(220, Bpm + 1); }

        // =====================================================
        //  Engine
        // =====================================================

        private void Start()
        {
            if (Bpm < 40) Bpm = 40;
            if (Bpm > 220) Bpm = 220;

            _audio.Prime();
            IsPlaying = true;
            CurrentBeat = 0;

            // Fire the first beat immediately so there's no awkward delay
            FireBeat();
            StartTimer();
        }

        private void Stop()
        {
            _tick?.Stop();
            _tick = null;
            IsPlaying = false;
            CurrentBeat = 0;
            Pulse = false;
        }

        private void StartTimer()
        {
            _tick?.Stop();
            _tick = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60_000.0 / Bpm),
            };
            _tick.Tick += (_, _) => FireBeat();
            _tick.Start();
        }

        private void FireBeat()
        {
            if (IsDisposed) { Stop(); return; }

            CurrentBeat = CurrentBeat % BeatsPerMeasure + 1; // 1..N rolling
            var accent  = AccentEnabled && CurrentBeat == 1;

            _audio.Tick(accent);
            Pulse = !Pulse; // toggle so the visual element can re-animate
        }

        // =====================================================
        //  Property reactions
        // =====================================================

        partial void OnBpmChanged(int value)
        {
            UpdateIntervalDisplay();
            if (IsPlaying) StartTimer(); // re-arm at new interval mid-play
        }

        partial void OnBeatsPerMeasureChanged(int value)
        {
            // Resets the beat counter so the accent lands right
            CurrentBeat = 0;
        }

        private void UpdateIntervalDisplay()
        {
            var ms = 60_000.0 / Math.Max(1, Bpm);
            IntervalDisplay = $"{(int)ms} ms / beat";
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Stop();
            }
            base.Dispose(disposing);
        }
    }
}
