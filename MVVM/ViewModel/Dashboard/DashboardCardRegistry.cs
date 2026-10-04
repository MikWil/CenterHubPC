using System;
using System.Collections.Generic;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>One kind of card: how it is listed ("Add card"), and how to create its view-model.</summary>
    /// <param name="Key">Stable id, persisted in ui.json (<c>DashboardCards</c> / <c>FavoritesCards</c>). Never rename.</param>
    /// <param name="Title">Card title and name in the "Add card" list.</param>
    /// <param name="Glyph">Segoe Fluent Icons character.</param>
    /// <param name="Description">One line shown in the "Add card" list.</param>
    /// <param name="OpenPageKey">Page (see <c>PageRegistry</c>) the card's header opens.</param>
    /// <param name="Factory">Creates the card's view-model (the info itself and the DI container are passed in).</param>
    public sealed record DashboardCardInfo(
        string Key,
        string Title,
        string Glyph,
        string Description,
        string OpenPageKey,
        Func<DashboardCardInfo, IServiceProvider, DashboardCardViewModel> Factory);

    /// <summary>Every card the dashboard and the Favorites window can show.</summary>
    public sealed class DashboardCardRegistry
    {
        /// <summary>Cards on the Home page when the user has not customised it.</summary>
        public static IReadOnlyList<string> DefaultDashboard { get; } =
            new[] { "audio", "metronome", "system", "practice", "volume", "notes" };

        /// <summary>Cards in the Favorites window when the user has not customised it.</summary>
        public static IReadOnlyList<string> DefaultFavorites { get; } =
            new[] { "system", "volume", "metronome" };

        /// <summary>The app's cards.</summary>
        public static DashboardCardRegistry Default { get; } = CreateDefault();

        private readonly List<DashboardCardInfo> _cards;
        private readonly Dictionary<string, DashboardCardInfo> _byKey = new(StringComparer.OrdinalIgnoreCase);

        public DashboardCardRegistry(IEnumerable<DashboardCardInfo> cards)
        {
            _cards = new List<DashboardCardInfo>();
            foreach (var card in cards)
            {
                if (_byKey.ContainsKey(card.Key)) continue;
                _byKey[card.Key] = card;
                _cards.Add(card);
            }
        }

        /// <summary>All cards, in the order the "Add card" list shows them.</summary>
        public IReadOnlyList<DashboardCardInfo> All => _cards;

        public DashboardCardInfo? Find(string? key) =>
            !string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key, out var info) ? info : null;

        public bool IsKnown(string? key) => Find(key) != null;

        /// <summary>Creates the view-model of a card; null for an unknown key.</summary>
        public DashboardCardViewModel? Create(string key, IServiceProvider services)
        {
            var info = Find(key);
            return info?.Factory(info, services);
        }

        // ── The app's cards ──

        private static DashboardCardRegistry CreateDefault() => new(new[]
        {
            new DashboardCardInfo("audio", "Audio", "",
                "Voicemeeter Banana status and your routing presets",
                "sound",
                (i, sp) => new AudioCardViewModel(i, sp.GetRequiredService<SoundViewModel>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("volume", "Volume & mic", "",
                "Master volume and the microphone mute",
                "sound",
                (i, sp) => new VolumeCardViewModel(i, sp.GetRequiredService<SoundViewModel>(), sp.GetRequiredService<MicrophoneService>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("metronome", "Metronome", PageGlyph("metronome", ""),
                "Tempo, start/stop and your setlist songs",
                "metronome",
                (i, sp) => new MetronomeCardViewModel(i, sp.GetRequiredService<MetronomeViewModel>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("practice", "Practice", "",
                "Practice time today, this week and your streak",
                "metronome",
                (i, sp) => new PracticeCardViewModel(i, sp.GetRequiredService<PracticeLogService>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("system", "System", PageGlyph("monitoring", ""),
                "CPU, GPU and memory load",
                "monitoring",
                (i, sp) => new SystemCardViewModel(i, sp.GetRequiredService<ISystemMonitorService>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("notes", "Notes", PageGlyph("notes", ""),
                "Your latest note",
                "notes",
                (i, sp) => new NotesCardViewModel(i, sp.GetRequiredService<QuickNotesService>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("clipboard", "Clipboard", PageGlyph("clipboard", ""),
                "Your last three copies — click one to copy it again",
                "clipboard",
                (i, sp) => new ClipboardCardViewModel(i, sp.GetRequiredService<ClipboardViewModel>(), Shell(sp), Log(sp, i))),

            new DashboardCardInfo("standing", "Standing", PageGlyph("standing", ""),
                "The sit/stand timer",
                "standing",
                (i, sp) => new StandingCardViewModel(i, sp.GetRequiredService<StandingViewModel>(), Shell(sp), Log(sp, i))),
        });

        private static string PageGlyph(string pageKey, string fallback) =>
            PageRegistry.Find(pageKey)?.Glyph is { Length: > 0 } glyph ? glyph : fallback;

        private static ShellService? Shell(IServiceProvider sp) => sp.GetService<ShellService>();

        private static ILogger? Log(IServiceProvider sp, DashboardCardInfo info) =>
            sp.GetService<ILoggerFactory>()?.CreateLogger("Dashboard." + info.Key);
    }
}
