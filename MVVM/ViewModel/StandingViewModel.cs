using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace CenterHubNew.MVVM.ViewModel
{
    public enum StandingPhase
    {
        Idle,
        Sitting,
        Standing,
    }

    public sealed class StandingPreset
    {
        public string Name { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public int SitMinutes { get; init; }
        public int StandMinutes { get; init; }
    }

    public partial class StandingViewModel : BaseViewModel
    {
        // ---- User config (strings to keep TextBox-binding tolerant) ----
        [ObservableProperty] private string _sittingMinutes  = "30";
        [ObservableProperty] private string _standingMinutes = "10";

        // ---- Live state ----
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isStartButtonEnabled = true;
        [ObservableProperty] private StandingPhase _phase = StandingPhase.Idle;
        [ObservableProperty] private string _phaseLabel = "READY";
        [ObservableProperty] private bool _isCurrentlyStanding;        // for color binding
        [ObservableProperty] private string _timeRemainingDisplay = "--:--";
        [ObservableProperty] private double _phaseProgress;            // 0..1 of current phase
        [ObservableProperty] private string _nextSwitchAtDisplay = "";
        [ObservableProperty] private string _phaseGuidance = "Configure your intervals and press Start.";

        // ---- Session stats ----
        [ObservableProperty] private int _cyclesCompleted;            // a "cycle" = sit + stand
        [ObservableProperty] private int _sitPhasesCompleted;
        [ObservableProperty] private int _standPhasesCompleted;
        [ObservableProperty] private string _sessionElapsedDisplay = "00:00:00";
        [ObservableProperty] private string _totalStandingDisplay = "00:00";
        [ObservableProperty] private string _totalSittingDisplay = "00:00";

        // ---- Presets ----
        public IReadOnlyList<StandingPreset> Presets { get; } = new[]
        {
            new StandingPreset { Name = "Pomodoro",  Subtitle = "25 sit · 5 stand",   SitMinutes = 25, StandMinutes = 5  },
            new StandingPreset { Name = "Office",    Subtitle = "45 sit · 15 stand",  SitMinutes = 45, StandMinutes = 15 },
            new StandingPreset { Name = "Balanced",  Subtitle = "30 sit · 30 stand",  SitMinutes = 30, StandMinutes = 30 },
            new StandingPreset { Name = "Endurance", Subtitle = "50 sit · 20 stand",  SitMinutes = 50, StandMinutes = 20 },
        };

        // ---- Internal ticking ----
        private DispatcherTimer? _tick;
        private DateTime _phaseStartedAt;
        private DateTime _sessionStartedAt;
        private int _phaseTotalSeconds;
        private int _phaseRemainingSeconds;
        private int _sitMinutes;
        private int _standMinutes;
        private TimeSpan _totalStanding;
        private TimeSpan _totalSitting;

        public StandingViewModel(ILogger<StandingViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("StandingViewModel initialized");
        }

        // =====================================================
        //  Commands
        // =====================================================

        [RelayCommand]
        public void StartTimers()
        {
            if (IsDisposed || IsRunning) return;

            if (!int.TryParse(SittingMinutes, out _sitMinutes) || _sitMinutes <= 0 ||
                !int.TryParse(StandingMinutes, out _standMinutes) || _standMinutes <= 0)
            {
                ToastService.Instance.Warning("Enter positive numbers for both sit and stand minutes");
                return;
            }

            _sessionStartedAt = DateTime.Now;
            _totalStanding = TimeSpan.Zero;
            _totalSitting = TimeSpan.Zero;
            CyclesCompleted = 0;
            SitPhasesCompleted = 0;
            StandPhasesCompleted = 0;
            IsRunning = true;
            IsStartButtonEnabled = false;

            BeginPhase(StandingPhase.Sitting);
            EnsureTick();

            ToastService.Instance.Success(
                $"Started — sit {_sitMinutes} min, stand {_standMinutes} min");
            Logger?.LogInformation("Standing timer started sit={Sit} stand={Stand}", _sitMinutes, _standMinutes);
        }

        [RelayCommand]
        public void StopTimers()
        {
            if (IsDisposed || !IsRunning) return;

            _tick?.Stop();
            _tick = null;
            IsRunning = false;
            IsStartButtonEnabled = true;
            Phase = StandingPhase.Idle;
            IsCurrentlyStanding = false;
            PhaseLabel = "STOPPED";
            PhaseGuidance = $"Session: {CyclesCompleted} cycles · {SessionElapsedDisplay}";
            TimeRemainingDisplay = "--:--";
            NextSwitchAtDisplay = "";
            PhaseProgress = 0;

            ToastService.Instance.Info("Standing timer stopped");
            Logger?.LogInformation("Standing timer stopped after {Cycles} cycles", CyclesCompleted);
        }

        public bool IsStartButtonReady => IsStartButtonEnabled && !IsRunning;

        [RelayCommand]
        private void SkipPhase()
        {
            if (!IsRunning || IsDisposed) return;
            SwitchPhase(notify: true);
            ToastService.Instance.Info($"Skipped to {(IsCurrentlyStanding ? "Standing" : "Sitting")}");
        }

        [RelayCommand]
        private void ApplyPreset(StandingPreset? preset)
        {
            if (preset is null) return;
            SittingMinutes = preset.SitMinutes.ToString();
            StandingMinutes = preset.StandMinutes.ToString();
            ToastService.Instance.Info($"Preset: {preset.Name}");
        }

        // =====================================================
        //  Phase machinery
        // =====================================================

        private void BeginPhase(StandingPhase newPhase)
        {
            Phase = newPhase;
            IsCurrentlyStanding = newPhase == StandingPhase.Standing;
            PhaseLabel = newPhase == StandingPhase.Standing ? "STANDING" : "SITTING";
            PhaseGuidance = newPhase == StandingPhase.Standing
                ? "Stand tall — shift your weight, stretch your back"
                : "Sit down, get focused work done";

            _phaseTotalSeconds = (newPhase == StandingPhase.Standing ? _standMinutes : _sitMinutes) * 60;
            _phaseRemainingSeconds = _phaseTotalSeconds;
            _phaseStartedAt = DateTime.Now;

            UpdateDisplayedTime();
            UpdateNextSwitchAt();
        }

        private void EnsureTick()
        {
            _tick?.Stop();
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += OnTick;
            _tick.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsRunning) { _tick?.Stop(); return; }

            _phaseRemainingSeconds--;

            // Accumulate stats
            if (Phase == StandingPhase.Standing) _totalStanding += TimeSpan.FromSeconds(1);
            else if (Phase == StandingPhase.Sitting) _totalSitting += TimeSpan.FromSeconds(1);

            UpdateDisplayedTime();
            UpdateStats();

            if (_phaseRemainingSeconds <= 0)
            {
                SwitchPhase(notify: true);
            }
        }

        private void SwitchPhase(bool notify)
        {
            // increment counters for the phase we're LEAVING
            if (Phase == StandingPhase.Standing) StandPhasesCompleted++;
            else if (Phase == StandingPhase.Sitting) SitPhasesCompleted++;
            // a full cycle = one sit + one stand
            CyclesCompleted = Math.Min(SitPhasesCompleted, StandPhasesCompleted);

            var next = Phase == StandingPhase.Sitting ? StandingPhase.Standing : StandingPhase.Sitting;
            BeginPhase(next);

            if (notify)
            {
                if (next == StandingPhase.Standing)
                    ToastService.Instance.Success("Stand up! Time to stretch your back.");
                else
                    ToastService.Instance.Info("Sit down — focus time.");
            }
        }

        // =====================================================
        //  Display helpers
        // =====================================================

        private void UpdateDisplayedTime()
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, _phaseRemainingSeconds));
            TimeRemainingDisplay = $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
            PhaseProgress = _phaseTotalSeconds > 0
                ? 1.0 - ((double)_phaseRemainingSeconds / _phaseTotalSeconds)
                : 0;
        }

        private void UpdateNextSwitchAt()
        {
            var switchAt = _phaseStartedAt.AddSeconds(_phaseTotalSeconds);
            NextSwitchAtDisplay = switchAt.ToString("HH:mm");
        }

        private void UpdateStats()
        {
            var session = DateTime.Now - _sessionStartedAt;
            SessionElapsedDisplay = $"{(int)session.TotalHours:D2}:{session.Minutes:D2}:{session.Seconds:D2}";
            TotalStandingDisplay = $"{(int)_totalStanding.TotalMinutes:D2}:{_totalStanding.Seconds:D2}";
            TotalSittingDisplay  = $"{(int)_totalSitting.TotalMinutes:D2}:{_totalSitting.Seconds:D2}";
        }

        // =====================================================
        //  Cleanup
        // =====================================================
        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try { _tick?.Stop(); } catch { }
                _tick = null;
                Logger?.LogInformation("StandingViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
