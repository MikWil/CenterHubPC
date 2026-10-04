using System.ComponentModel;
using CenterHubNew.MVVM.Navigation;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>Banana status, the active routing preset, preset chips and the "Direct (no Banana)" chip.</summary>
    public sealed partial class AudioCardViewModel : DashboardCardViewModel
    {
        public AudioCardViewModel(DashboardCardInfo info, SoundViewModel sound, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            Sound = sound;
            Routing = sound.Routing;
            if (Routing != null)
                Routing.PropertyChanged += OnRoutingChanged;
        }

        public SoundViewModel Sound { get; }

        /// <summary>The Sound board's routing view-model (null if audio routing is unavailable).</summary>
        public RoutingViewModel? Routing { get; }

        public bool HasRouting => Routing != null;

        public string StatusText => Routing?.StatusText ?? "Audio routing unavailable";

        public string PresetText =>
            Routing == null ? ""
            : Routing.IsDirect ? "Direct — headset and mic used as they are"
            : string.IsNullOrWhiteSpace(Routing.CurrentPresetName) ? "" : $"Preset: {Routing.CurrentPresetName}";

        private void OnRoutingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsDisposed) return;
            switch (e.PropertyName)
            {
                case nameof(RoutingViewModel.StatusText):
                    OnPropertyChanged(nameof(StatusText));
                    break;
                case nameof(RoutingViewModel.IsDirect):
                case nameof(RoutingViewModel.CurrentPresetName):
                    OnPropertyChanged(nameof(PresetText));
                    break;
            }
        }

        /// <summary>Applies a preset through the Sound board, so its UI stays in sync.</summary>
        [RelayCommand]
        private void ApplyPreset(PresetChip? chip)
        {
            if (IsDisposed || chip is null) return;
            Routing?.ApplyPresetCommand.Execute(chip);
        }

        [RelayCommand]
        private void GoDirect()
        {
            if (IsDisposed) return;
            Routing?.GoDirectCommand.Execute(null);
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing && Routing != null)
                Routing.PropertyChanged -= OnRoutingChanged;
            base.Dispose(disposing);
        }
    }
}
