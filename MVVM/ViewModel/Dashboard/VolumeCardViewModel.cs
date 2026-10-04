using Avalonia.Threading;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>Master volume slider (the Sound view-model's output volume) and the microphone mute.</summary>
    public sealed partial class VolumeCardViewModel : DashboardCardViewModel
    {
        private readonly MicrophoneService _mic;

        [ObservableProperty] private bool _micMuted;
        [ObservableProperty] private bool _hasMic = true;
        [ObservableProperty] private string _micText = "Microphone";
        [ObservableProperty] private string _micGlyph = "";

        public VolumeCardViewModel(DashboardCardInfo info, SoundViewModel sound, MicrophoneService mic, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            Sound = sound;
            _mic = mic;
            _mic.MuteChanged += OnMuteChanged;
        }

        public SoundViewModel Sound { get; }

        protected override void OnActiveChanged(bool active)
        {
            if (active) RefreshMic();
        }

        private void OnMuteChanged(bool muted)
        {
            // Raised on an audio thread.
            try { Dispatcher.UIThread.Post(RefreshMic); }
            catch (InvalidOperationException) { /* dispatcher shut down */ }
        }

        private void RefreshMic()
        {
            if (IsDisposed) return;
            var muted = _mic.IsMuted;
            HasMic = muted.HasValue;
            MicMuted = muted == true;
            MicText = muted switch
            {
                null => "No microphone",
                true => "Microphone muted",
                false => "Microphone live",
            };
            MicGlyph = muted == true ? "" : "";
        }

        [RelayCommand]
        private void ToggleMic()
        {
            if (IsDisposed) return;
            if (_mic.ToggleMute() is null)
                ToastService.Instance.Warning("No default microphone found");
            RefreshMic();
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
                _mic.MuteChanged -= OnMuteChanged;
            base.Dispose(disposing);
        }
    }
}
