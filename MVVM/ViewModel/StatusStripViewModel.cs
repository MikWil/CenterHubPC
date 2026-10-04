using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>What the audio segment of the status strip shows.</summary>
    public enum StripAudioState
    {
        /// <summary>Voicemeeter isn't installed: the segment is hidden.</summary>
        Hidden,
        /// <summary>Banana runs and routes the audio.</summary>
        Running,
        /// <summary>Banana is off on purpose; the headset and mic go straight to Windows.</summary>
        Direct,
        /// <summary>Installed but not running.</summary>
        Stopped,
    }

    /// <summary>
    /// The thin strip at the bottom of the window: audio routing state, microphone mute and the
    /// metronome, always one click from the page that controls them. DI singleton.
    /// </summary>
    public partial class StatusStripViewModel : BaseViewModel
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

        private readonly AudioRoutingService? _routing;
        private readonly MicrophoneService? _mic;
        private readonly ShellService? _shell;

        private DispatcherTimer? _timer;
        private MetronomeViewModel? _metronome;
        private int _refreshing;
        private string? _micDevice;

        // ─── Audio ───
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAudioVisible))]
        [NotifyPropertyChangedFor(nameof(IsAudioRunning))]
        [NotifyPropertyChangedFor(nameof(IsAudioDirect))]
        [NotifyPropertyChangedFor(nameof(IsAudioStopped))]
        private StripAudioState _audioState = StripAudioState.Hidden;

        [ObservableProperty] private string _audioText = "";

        public bool IsAudioVisible => AudioState != StripAudioState.Hidden;
        public bool IsAudioRunning => AudioState == StripAudioState.Running;
        public bool IsAudioDirect => AudioState == StripAudioState.Direct;
        public bool IsAudioStopped => AudioState == StripAudioState.Stopped;

        // ─── Microphone ───
        [ObservableProperty] private bool _hasMic;
        [ObservableProperty] private bool _isMicMuted;
        [ObservableProperty] private string _micText = "Mic on";
        [ObservableProperty] private string _micGlyph = "";
        [ObservableProperty] private string _micTooltip = "Microphone";

        // ─── Metronome ───
        [ObservableProperty] private bool _isMetronomeVisible;
        [ObservableProperty] private bool _isMetroPlaying;
        [ObservableProperty] private string _metroBpmText = "";
        [ObservableProperty] private string _metroDetail = "";
        [ObservableProperty] private bool _hasMetroDetail;
        [ObservableProperty] private string _metroPlayGlyph = "";
        [ObservableProperty] private string _metroPlayTooltip = "Start metronome";

        public StatusStripViewModel(
            AudioRoutingService? routing = null,
            MicrophoneService? mic = null,
            ShellService? shell = null,
            ILogger<StatusStripViewModel>? logger = null) : base(logger)
        {
            _routing = routing ?? Resolve<AudioRoutingService>();
            _mic = mic ?? Resolve<MicrophoneService>();
            _shell = shell ?? Resolve<ShellService>();

            if (_mic is not null)
                _mic.MuteChanged += OnMicMuteChanged;

            // The timer only marshals; the work happens on a thread-pool thread.
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            RefreshInBackground();

            // The metronome is a heavy singleton; resolve it once startup has settled.
            Dispatcher.UIThread.Post(AttachMetronome, DispatcherPriority.Background);
        }

        private static T? Resolve<T>() where T : class
        {
            try { return App.Services?.GetService(typeof(T)) as T; }
            catch (Exception) { return null; } // no service host (unit tests, shutdown)
        }

        // ─── Refresh ───

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            RefreshInBackground();
        }

        private void RefreshInBackground()
        {
            if (IsDisposed) return;
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return; // previous read still running

            _ = Task.Run(() =>
            {
                try
                {
                    var (state, text) = ReadAudio();
                    bool? muted = null;
                    string? device = null;
                    if (_mic is not null)
                    {
                        muted = _mic.IsMuted;
                        device = _mic.DeviceName;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed) return;
                        AudioState = state;
                        AudioText = text;
                        ApplyMic(muted, device);
                    });
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug(ex, "Status strip refresh failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }

        /// <summary>Runs off the UI thread: RefreshStatus can take a moment.</summary>
        private (StripAudioState State, string Text) ReadAudio()
        {
            var routing = _routing;
            if (routing is null || !routing.IsInstalled)
                return (StripAudioState.Hidden, "");

            if (routing.RefreshStatus())
            {
                string name = "Voicemeeter";
                try
                {
                    var id = routing.ActivePresetId;
                    var preset = routing.LoadPresets().FirstOrDefault(p => p.Id == id);
                    if (preset is not null && !string.IsNullOrWhiteSpace(preset.Name)) name = preset.Name;
                }
                catch (Exception ex) { Logger?.LogDebug(ex, "Could not read the active preset"); }
                return (StripAudioState.Running, name);
            }

            return routing.IsDirectMode()
                ? (StripAudioState.Direct, "Direct — Banana off")
                : (StripAudioState.Stopped, "Banana stopped");
        }

        // ─── Microphone ───

        private void OnMicMuteChanged(bool muted)
        {
            // Raised on an audio thread.
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed) return;
                ApplyMic(muted, _micDevice);
            });
        }

        private void ApplyMic(bool? muted, string? device)
        {
            _micDevice = device;
            HasMic = muted.HasValue;
            IsMicMuted = muted == true;
            MicText = muted switch
            {
                null => "No mic",
                true => "Mic muted",
                _ => "Mic on",
            };
            MicGlyph = muted == true ? "" : "";
            string action = muted == true ? "Click to unmute" : "Click to mute";
            MicTooltip = string.IsNullOrWhiteSpace(device)
                ? (muted.HasValue ? action : "No microphone found")
                : $"{device} — {action}";
        }

        [RelayCommand]
        private async Task ToggleMic()
        {
            if (IsDisposed || _mic is null) return;

            bool? muted = await Task.Run(() => _mic.ToggleMute());
            if (IsDisposed) return;

            if (muted is null)
            {
                ToastService.Instance.Warning("No microphone found.");
                return;
            }
            ApplyMic(muted, _micDevice);
        }

        // ─── Metronome ───

        private void AttachMetronome()
        {
            if (IsDisposed || _metronome is not null) return;
            var metronome = Resolve<MetronomeViewModel>();
            if (metronome is null) return;

            _metronome = metronome;
            metronome.PropertyChanged += OnMetronomePropertyChanged;
            UpdateMetronome();
        }

        private void OnMetronomePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsDisposed) return;
            switch (e.PropertyName)
            {
                case nameof(MetronomeViewModel.Bpm):
                case nameof(MetronomeViewModel.IsPlaying):
                case nameof(MetronomeViewModel.SectionLabel):
                case nameof(MetronomeViewModel.SelectedStyle):
                case nameof(MetronomeViewModel.IsDrumsMode):
                case null:
                case "":
                    if (Dispatcher.UIThread.CheckAccess()) UpdateMetronome();
                    else Dispatcher.UIThread.Post(UpdateMetronome);
                    break;
            }
        }

        private void UpdateMetronome()
        {
            var m = _metronome;
            if (m is null || IsDisposed) return;

            bool playing = m.IsPlaying;
            IsMetronomeVisible = true;
            IsMetroPlaying = playing;
            MetroBpmText = $"{m.Bpm} BPM";
            MetroPlayGlyph = playing ? "" : "";
            MetroPlayTooltip = playing ? "Stop metronome" : "Start metronome";

            string detail = "";
            if (playing)
            {
                string activity = m.IsDrumsMode ? (m.SelectedStyle?.Name ?? "Drums") : "Click";
                string section = m.IsDrumsMode ? m.SectionLabel : "";
                detail = string.IsNullOrWhiteSpace(section) ? activity : $"{activity} · {section}";
            }
            MetroDetail = detail;
            HasMetroDetail = detail.Length > 0;
        }

        [RelayCommand]
        private void ToggleMetronome()
        {
            if (IsDisposed) return;
            AttachMetronome();
            _metronome?.TogglePlayCommand.Execute(null);
        }

        // ─── Navigation ───

        [RelayCommand]
        private void OpenSound()
        {
            if (IsDisposed) return;
            _shell?.NavigateTo("sound");
        }

        [RelayCommand]
        private void OpenMetronome()
        {
            if (IsDisposed) return;
            _shell?.NavigateTo("metronome");
        }

        [RelayCommand]
        private void ShowPalette()
        {
            if (IsDisposed) return;
            _shell?.ShowCommandPalette();
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                if (_timer is not null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnTimerTick;
                    _timer = null;
                }
                if (_mic is not null)
                    _mic.MuteChanged -= OnMicMuteChanged;
                if (_metronome is not null)
                {
                    _metronome.PropertyChanged -= OnMetronomePropertyChanged;
                    _metronome = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
