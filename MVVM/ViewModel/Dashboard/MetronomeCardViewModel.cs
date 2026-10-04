using System.Collections.Specialized;
using System.ComponentModel;
using CenterHubNew.MVVM.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>Big BPM, style / click label, play-stop, -/+ 5 BPM and the current setlist song with previous / next.</summary>
    public sealed partial class MetronomeCardViewModel : DashboardCardViewModel
    {
        [ObservableProperty] private string _modeText = "";
        [ObservableProperty] private string _songText = "";
        [ObservableProperty] private bool _hasSongs;
        [ObservableProperty] private string _playGlyph = "";
        [ObservableProperty] private string _playLabel = "Play";

        public MetronomeCardViewModel(DashboardCardInfo info, MetronomeViewModel metronome, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            Metronome = metronome;
            Metronome.PropertyChanged += OnMetronomeChanged;
            Metronome.Songs.CollectionChanged += OnSongsChanged;
            Refresh();
        }

        /// <summary>The metronome page's view-model (a singleton); the card binds its BPM, tempo name and commands.</summary>
        public MetronomeViewModel Metronome { get; }

        private void OnMetronomeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsDisposed) return;
            switch (e.PropertyName)
            {
                case nameof(MetronomeViewModel.IsPlaying):
                case nameof(MetronomeViewModel.IsDrumsMode):
                case nameof(MetronomeViewModel.SelectedStyle):
                case nameof(MetronomeViewModel.SelectedSong):
                case nameof(MetronomeViewModel.SelectedSetlist):
                    Refresh();
                    break;
            }
        }

        private void OnSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!IsDisposed) Refresh();
        }

        private void Refresh()
        {
            string? styleName = Metronome.SelectedStyle?.Name;
            ModeText = !Metronome.IsDrumsMode ? "Click"
                : string.IsNullOrEmpty(styleName) ? "Drums"
                : styleName;

            PlayGlyph = Metronome.IsPlaying ? "" : "";
            PlayLabel = Metronome.IsPlaying ? "Stop" : "Play";

            var songs = Metronome.Songs;
            HasSongs = songs.Count > 0;
            if (songs.Count == 0)
            {
                SongText = "";
            }
            else if (Metronome.SelectedSong is { } song)
            {
                SongText = $"{songs.IndexOf(song) + 1}/{songs.Count} · {song.DisplayName}";
            }
            else
            {
                SongText = $"{songs.Count} songs in the setlist";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Metronome.PropertyChanged -= OnMetronomeChanged;
                Metronome.Songs.CollectionChanged -= OnSongsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
