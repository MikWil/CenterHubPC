using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>One thing the palette can do.</summary>
    /// <param name="Id">Stable id (used for the "recent" list).</param>
    /// <param name="Glyph">Segoe Fluent Icons character.</param>
    /// <param name="Keywords">Extra words the search matches (not shown).</param>
    /// <param name="Shortcut">Optional keyboard shortcut shown on the row.</param>
    /// <param name="Run">What to do. Called on the UI thread after the palette has closed.</param>
    public sealed record PaletteCommand(
        string Id,
        string Title,
        string Category,
        string Glyph,
        string Keywords,
        string? Shortcut,
        Func<Task> Run);

    /// <summary>A row in the palette's result list.</summary>
    public sealed partial class PaletteResult : ObservableObject
    {
        public PaletteResult(PaletteCommand command, Func<PaletteCommand, Task> run)
        {
            Command = command;
            RunCommand = new AsyncRelayCommand(() => run(command));
        }

        public PaletteCommand Command { get; }
        public string Title => Command.Title;
        public string Category => Command.Category;
        public string Glyph => Command.Glyph;
        public string Shortcut => Command.Shortcut ?? "";
        public bool HasShortcut => !string.IsNullOrEmpty(Command.Shortcut);

        /// <summary>Click on the row.</summary>
        public IAsyncRelayCommand RunCommand { get; }

        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>
    /// Ctrl+K: type to jump to a page or run an action (apply an audio preset, start the
    /// metronome, set a tempo, zoom…). Shown in the shell's overlay layer. DI singleton.
    /// </summary>
    public partial class CommandPaletteViewModel : BaseViewModel, IOverlayViewModel
    {
        public const int MaxRecents = 5;
        public const int MinTempo = 30;
        public const int MaxTempo = 280;

        private const string PagesCategory = "Pages";

        private readonly ShellService? _shell;
        private readonly UiSettingsService? _ui;
        private readonly MicrophoneService? _mic;
        private readonly List<string> _recent = new(); // most recent first
        private IReadOnlyList<PaletteCommand> _commands = Array.Empty<PaletteCommand>();

        [ObservableProperty] private string _query = "";
        [ObservableProperty] private IReadOnlyList<PaletteResult> _results = Array.Empty<PaletteResult>();
        [ObservableProperty] private int _selectedIndex = -1;
        [ObservableProperty] private bool _hasResults;

        public CommandPaletteViewModel(
            ShellService? shell = null,
            UiSettingsService? ui = null,
            MicrophoneService? mic = null,
            ILogger<CommandPaletteViewModel>? logger = null) : base(logger)
        {
            _shell = shell ?? Resolve<ShellService>();
            _ui = ui ?? Resolve<UiSettingsService>();
            _mic = mic ?? Resolve<MicrophoneService>();
        }

        // ─── IOverlayViewModel ───

        public event Action? CloseRequested;

        /// <summary>The view should put the cursor in the search box.</summary>
        public event Action? FocusRequested;

        /// <summary>The view should scroll the row at this index into view.</summary>
        public event Action<int>? ScrollToRequested;

        public bool CloseOnBackdropClick => true;

        public void OnShown()
        {
            if (IsDisposed) return;
            _commands = BuildCommands();
            Query = "";
            Refresh();
            FocusRequested?.Invoke();
        }

        public void OnClosed()
        {
        }

        // ─── Selection ───

        /// <summary>Up / Down: move the highlight, wrapping around.</summary>
        public void MoveSelection(int delta)
        {
            int count = Results.Count;
            if (IsDisposed || count == 0) return;
            int next = SelectedIndex < 0 ? (delta >= 0 ? 0 : count - 1) : ((SelectedIndex + delta) % count + count) % count;
            SetSelectedIndex(next);
        }

        /// <summary>Enter: run the highlighted row.</summary>
        public Task RunSelectedAsync()
        {
            if (IsDisposed || SelectedIndex < 0 || SelectedIndex >= Results.Count) return Task.CompletedTask;
            return RunAsync(Results[SelectedIndex].Command);
        }

        private void SetSelectedIndex(int index)
        {
            SelectedIndex = index;
            var results = Results;
            for (int i = 0; i < results.Count; i++)
                results[i].IsSelected = i == index;
            if (index >= 0) ScrollToRequested?.Invoke(index);
        }

        partial void OnQueryChanged(string value) => Refresh();

        private void Refresh()
        {
            if (IsDisposed) return;

            var list = new List<PaletteCommand>();
            if (ParseTempo(Query) is int bpm)
                list.Add(MakeTempoCommand(bpm));
            list.AddRange(Filter(_commands, Query, _recent, MaxRecents));

            Results = list.Select(c => new PaletteResult(c, RunAsync)).ToList();
            HasResults = Results.Count > 0;
            SetSelectedIndex(Results.Count > 0 ? 0 : -1);
        }

        // ─── Running ───

        /// <summary>Close the palette first, then run the command on the UI thread; failures become a toast.</summary>
        public async Task RunAsync(PaletteCommand command)
        {
            if (IsDisposed) return;

            Remember(command.Id);
            CloseRequested?.Invoke();

            try
            {
                await command.Run();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Palette command {Id} failed", command.Id);
                ToastService.Instance.Error($"{command.Title}: {ex.Message}");
            }
        }

        private void Remember(string id)
        {
            _recent.Remove(id);
            _recent.Insert(0, id);
            if (_recent.Count > 20) _recent.RemoveRange(20, _recent.Count - 20);
        }

        // ─── Matching (pure, unit-tested) ───

        /// <summary>
        /// A tempo typed into the search box: a standalone number from 30 to 280, optionally with a
        /// "bpm" suffix ("120", "bpm 95", "tempo 140", "100bpm"). Digits inside other words
        /// ("rock 8ths") don't count. Null when there is none.
        /// </summary>
        public static int? ParseTempo(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            foreach (var raw in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim().ToLowerInvariant();
                if (token.EndsWith("bpm", StringComparison.Ordinal)) token = token[..^3];
                if (token.Length is 0 or > 3) continue;
                if (!token.All(char.IsAsciiDigit)) continue;
                if (int.TryParse(token, out int value) && value is >= MinTempo and <= MaxTempo)
                    return value;
            }
            return null;
        }

        /// <summary>
        /// Commands matching <paramref name="query"/>, best first. Every word of the query must occur in
        /// title + category + keywords (case-insensitive). Ranking: the title starts with the query,
        /// then a title word starts with a query word, then anything else; ties keep the input order.
        /// An empty query lists the most recently run commands (newest first, at most
        /// <paramref name="recentCount"/>) followed by the pages.
        /// </summary>
        public static IReadOnlyList<PaletteCommand> Filter(
            IReadOnlyList<PaletteCommand> commands,
            string? query,
            IReadOnlyList<string>? recentIds = null,
            int recentCount = MaxRecents)
        {
            var words = (query ?? "")
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
            {
                var result = new List<PaletteCommand>();
                var seen = new HashSet<string>();
                foreach (var id in recentIds ?? Array.Empty<string>())
                {
                    if (result.Count >= recentCount) break;
                    var cmd = commands.FirstOrDefault(c => c.Id == id);
                    if (cmd is not null && seen.Add(cmd.Id)) result.Add(cmd);
                }
                foreach (var cmd in commands)
                {
                    if (cmd.Category == PagesCategory && seen.Add(cmd.Id)) result.Add(cmd);
                }
                return result;
            }

            string phrase = string.Join(' ', words);
            var ranked = new List<(PaletteCommand Command, int Rank)>();
            foreach (var cmd in commands)
            {
                string title = cmd.Title.ToLowerInvariant();
                string haystack = $"{title} {cmd.Category} {cmd.Keywords}".ToLowerInvariant();
                if (!words.All(w => haystack.Contains(w, StringComparison.Ordinal))) continue;

                int rank;
                if (title.StartsWith(phrase, StringComparison.Ordinal)) rank = 0;
                else
                {
                    var titleWords = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    rank = words.Any(w => titleWords.Any(t => t.StartsWith(w, StringComparison.Ordinal))) ? 1 : 2;
                }
                ranked.Add((cmd, rank));
            }

            // OrderBy is stable, so equal ranks keep the order of the command list.
            return ranked.OrderBy(r => r.Rank).Select(r => r.Command).ToList();
        }

        // ─── The command list ───

        /// <summary>Every command, rebuilt each time the palette opens so presets are current.</summary>
        public IReadOnlyList<PaletteCommand> BuildCommands()
        {
            var list = new List<PaletteCommand>();

            // Pages
            foreach (var page in PageRegistry.All)
            {
                var key = page.Key;
                list.Add(new PaletteCommand(
                    "page:" + key, "Go to " + page.Title, PagesCategory, page.Glyph, page.Keywords, null,
                    () => Sync(() => _shell?.NavigateTo(key))));
            }

            // Audio
            foreach (var (id, name) in ReadPresets())
            {
                var presetId = id;
                list.Add(new PaletteCommand(
                    "preset:" + presetId, "Apply preset: " + name, "Audio", "",
                    "audio voicemeeter banana routing", null,
                    () => ApplyPresetAsync(presetId)));
            }
            list.Add(new PaletteCommand("audio:direct", "Direct mode (no Banana)", "Audio", "",
                "audio headset bypass voicemeeter banana off teams", null, RunDirectAsync));
            list.Add(new PaletteCommand("audio:restart", "Restart Voicemeeter", "Audio", "",
                "audio banana reboot fix", null, RunRestartAsync));
            list.Add(new PaletteCommand("audio:resync", "Re-sync audio", "Audio", "",
                "audio banana reconnect fix routing", null, RunResyncAsync));
            list.Add(new PaletteCommand("audio:setup", "Run audio setup", "Audio", "",
                "audio wizard devices headphones microphone configure", null,
                () => Sync(() => _shell?.ShowSetupWizard())));
            list.Add(new PaletteCommand("audio:mic", "Mute / unmute microphone", "Audio", "",
                "audio mic microphone mute unmute toggle", null, ToggleMicAsync));

            // Metronome
            list.Add(new PaletteCommand("metro:toggle", "Start / stop metronome", "Metronome", "",
                "play pause drums click practice", null, () => WithMetronome(m => m.TogglePlayCommand.Execute(null))));
            list.Add(new PaletteCommand("metro:tap", "Tap tempo", "Metronome", "",
                "bpm beat", null, () => WithMetronome(m => m.TapTempoCommand.Execute(null))));
            list.Add(new PaletteCommand("metro:fill", "Drum fill", "Metronome", "",
                "drums", null, () => WithMetronome(m =>
                {
                    if (!m.IsPlaying || !m.IsDrumsMode) ToastService.Instance.Info("Start the drums first, then play a fill.");
                    else m.FillCommand.Execute(null);
                })));
            list.Add(new PaletteCommand("metro:nextpart", "Next song part", "Metronome", "",
                "drums verse chorus bridge section", null, () => WithMetronome(m =>
                {
                    if (!m.IsPlaying || !m.IsDrumsMode) ToastService.Instance.Info("Start the drums first.");
                    else m.NextPartCommand.Execute(null);
                })));
            list.Add(new PaletteCommand("metro:nextsong", "Next song", "Metronome", "",
                "setlist practice", null, () => WithMetronome(m => m.NextSong())));
            list.Add(new PaletteCommand("metro:prevsong", "Previous song", "Metronome", "",
                "setlist practice back", null, () => WithMetronome(m => m.PreviousSong())));

            // App
            list.Add(new PaletteCommand("app:zoomin", "Zoom in", "App", "", "bigger larger scale", "Ctrl +",
                () => Sync(() => _shell?.ZoomBy(0.1))));
            list.Add(new PaletteCommand("app:zoomout", "Zoom out", "App", "", "smaller scale", "Ctrl -",
                () => Sync(() => _shell?.ZoomBy(-0.1))));
            list.Add(new PaletteCommand("app:zoomreset", "Reset zoom", "App", "", "100 percent scale", "Ctrl 0",
                () => Sync(() => _shell?.ResetZoom())));
            list.Add(new PaletteCommand("app:sidebar", "Toggle sidebar", "App", "", "menu navigation collapse expand rail", null,
                () => Sync(() => _shell?.ToggleSidebar())));
            list.Add(new PaletteCommand("app:settings", "Settings", "App", "", "preferences options theme", null,
                () => Sync(() => _shell?.NavigateTo("settings"))));
            list.Add(new PaletteCommand("app:compact", "Toggle compact layout", "App", "", "density spacing tight", null,
                () => Sync(ToggleCompact)));
            list.Add(new PaletteCommand("app:strip", "Show / hide status strip", "App", "", "bottom bar footer", null,
                () => Sync(ToggleStatusStrip)));

            return list;
        }

        private PaletteCommand MakeTempoCommand(int bpm) =>
            new("tempo:set", $"Set tempo to {bpm} BPM", "Metronome", "", "tempo bpm", null,
                () => WithMetronome(m =>
                {
                    m.Bpm = bpm;
                    ToastService.Instance.Info($"Tempo {bpm} BPM");
                }));

        // ─── Command implementations ───

        private static Task Sync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        private Task WithMetronome(Action<MetronomeViewModel> action)
        {
            var metronome = Resolve<MetronomeViewModel>();
            if (metronome is null)
            {
                ToastService.Instance.Warning("The metronome isn't available.");
                return Task.CompletedTask;
            }
            action(metronome);
            return Task.CompletedTask;
        }

        private IReadOnlyList<(string Id, string Name)> ReadPresets()
        {
            try
            {
                var routing = Resolve<AudioRoutingService>();
                if (routing is null) return Array.Empty<(string, string)>();
                return routing.LoadPresets()
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => (p.Id, p.Name))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "Could not read the audio presets");
                return Array.Empty<(string, string)>();
            }
        }

        /// <summary>The Sound board, so the page stays in sync with what the palette did.</summary>
        private RoutingViewModel? Board() => Resolve<SoundViewModel>()?.Routing;

        private async Task ApplyPresetAsync(string id)
        {
            var board = Board();
            if (board is null)
            {
                ToastService.Instance.Warning("The Sound board isn't available.");
                return;
            }
            await board.ApplyPresetByIdAsync(id);
        }

        private async Task RunDirectAsync()
        {
            var board = Board();
            if (board is null) { ToastService.Instance.Warning("The Sound board isn't available."); return; }
            await board.GoDirectCommand.ExecuteAsync(null);
        }

        private async Task RunRestartAsync()
        {
            var board = Board();
            if (board is null) { ToastService.Instance.Warning("The Sound board isn't available."); return; }
            await board.RestartVoicemeeterCommand.ExecuteAsync(null);
        }

        private async Task RunResyncAsync()
        {
            var board = Board();
            if (board is null) { ToastService.Instance.Warning("The Sound board isn't available."); return; }
            await board.ResyncCommand.ExecuteAsync(null);
        }

        private async Task ToggleMicAsync()
        {
            if (_mic is null) { ToastService.Instance.Warning("No microphone service."); return; }
            bool? muted = await Task.Run(() => _mic.ToggleMute());
            if (muted is null) ToastService.Instance.Warning("No microphone found.");
            else ToastService.Instance.Info(muted == true ? "Microphone muted" : "Microphone on");
        }

        private void ToggleCompact()
        {
            if (_ui is null) return;
            UiDensity next = _ui.Current.Density == UiDensity.Compact ? UiDensity.Comfortable : UiDensity.Compact;
            _ui.Update(s => s.Density = next);
            ToastService.Instance.Info(next == UiDensity.Compact ? "Compact layout" : "Comfortable layout");
        }

        private void ToggleStatusStrip()
        {
            if (_ui is null) return;
            bool show = !_ui.Current.ShowStatusStrip;
            _ui.Update(s => s.ShowStatusStrip = show);
            ToastService.Instance.Info(show ? "Status strip shown" : "Status strip hidden");
        }

        private static T? Resolve<T>() where T : class
        {
            try { return App.Services?.GetService(typeof(T)) as T; }
            catch (Exception) { return null; } // no service host (unit tests, shutdown)
        }
    }
}
