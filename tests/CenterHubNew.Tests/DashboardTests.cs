using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;
using CenterHubNew.MVVM.ViewModel.Dashboard;
using Xunit;

namespace CenterHubNew.Tests;

public class DashboardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubDash-" + Guid.NewGuid().ToString("N"));

    public DashboardTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    // ── Test doubles ──

    private sealed class TestCard : DashboardCardViewModel
    {
        public TestCard(DashboardCardInfo info) : base(info, shell: null) { }
        public bool WasDisposed => IsDisposed;
    }

    private static DashboardCardInfo Info(string key) =>
        new(key, key.ToUpperInvariant(), "", key + " card", "home", (i, _) => new TestCard(i));

    private static readonly string[] Keys = { "a", "b", "c", "d" };

    private static DashboardCardRegistry TestRegistry() => new(Keys.Select(Info));

    private (UiSettingsService Settings, CardListEditor Editor, List<TestCard> Created) NewEditor(
        string[]? defaults = null, Func<DashboardCardInfo, DashboardCardViewModel?>? create = null)
    {
        var settings = new UiSettingsService(null, _folder);
        var created = new List<TestCard>();
        var editor = new CardListEditor(
            settings, TestRegistry(), CardListSlot.Dashboard, defaults ?? new[] { "a", "b", "c" },
            create ?? (info => { var c = new TestCard(info); created.Add(c); return c; }));
        return (settings, editor, created);
    }

    private static string[] KeysOf(CardListEditor editor) => editor.Cards.Select(c => c.Key).ToArray();

    // ── Resolve (pure) ──

    [Fact]
    public void Resolve_empty_list_gives_the_defaults()
    {
        var result = CardListEditor.Resolve(new List<string>(), TestRegistry(), new[] { "b", "a" });
        Assert.Equal(new[] { "b", "a" }, result);
    }

    [Fact]
    public void Resolve_drops_unknown_keys_and_duplicates_and_keeps_order()
    {
        var result = CardListEditor.Resolve(new[] { "c", "nope", "a", "C", "a", "" }, TestRegistry(), new[] { "b" });
        Assert.Equal(new[] { "c", "a" }, result);
    }

    [Fact]
    public void Resolve_with_only_unknown_keys_falls_back_to_the_defaults()
    {
        var result = CardListEditor.Resolve(new[] { "x", "y" }, TestRegistry(), new[] { "d" });
        Assert.Equal(new[] { "d" }, result);
    }

    // ── Editor ──

    [Fact]
    public void A_fresh_editor_shows_the_defaults_and_offers_the_rest()
    {
        var (_, editor, _) = NewEditor();

        Assert.Equal(new[] { "a", "b", "c" }, KeysOf(editor));
        Assert.Equal(new[] { "d" }, editor.Available.Select(a => a.Key).ToArray());
        Assert.True(editor.HasAvailable);
    }

    [Fact]
    public void Add_appends_saves_and_keeps_the_existing_cards()
    {
        var (_, editor, _) = NewEditor();
        var a = editor.Cards[0];

        Assert.True(editor.Add("d"));

        Assert.Equal(new[] { "a", "b", "c", "d" }, KeysOf(editor));
        Assert.Same(a, editor.Cards[0]);                       // state of cards that stay is kept
        Assert.Empty(editor.Available);
        Assert.False(editor.HasAvailable);
        Assert.Equal(new[] { "a", "b", "c", "d" }, new UiSettingsService(null, _folder).Current.DashboardCards);
    }

    [Fact]
    public void Add_ignores_unknown_and_already_present_keys()
    {
        var (_, editor, _) = NewEditor();

        Assert.False(editor.Add("nope"));
        Assert.False(editor.Add("a"));
        Assert.Equal(new[] { "a", "b", "c" }, KeysOf(editor));
    }

    [Fact]
    public void Remove_disposes_the_card_and_it_becomes_available_again()
    {
        var (_, editor, _) = NewEditor();
        var b = (TestCard)editor.Cards[1];

        Assert.True(editor.Remove("b"));

        Assert.Equal(new[] { "a", "c" }, KeysOf(editor));
        Assert.True(b.WasDisposed);
        Assert.Contains("b", editor.Available.Select(x => x.Key));
        Assert.Equal(new[] { "a", "c" }, new UiSettingsService(null, _folder).Current.DashboardCards);
    }

    [Fact]
    public void The_last_card_cannot_be_removed()
    {
        var (_, editor, _) = NewEditor(defaults: new[] { "a" });

        Assert.False(editor.Remove("a"));
        Assert.Equal(new[] { "a" }, KeysOf(editor));
        Assert.False(editor.Cards[0].CanRemove);
    }

    [Fact]
    public void Move_up_and_down_reorder_and_stop_at_the_ends()
    {
        var (_, editor, _) = NewEditor();
        var c = editor.Cards[2];

        Assert.True(editor.MoveUp("c"));
        Assert.Equal(new[] { "a", "c", "b" }, KeysOf(editor));
        Assert.Same(c, editor.Cards[1]);

        Assert.True(editor.MoveDown("a"));
        Assert.Equal(new[] { "c", "a", "b" }, KeysOf(editor));

        Assert.False(editor.MoveUp("c"));     // already first
        Assert.False(editor.MoveDown("b"));   // already last
        Assert.Equal(new[] { "c", "a", "b" }, new UiSettingsService(null, _folder).Current.DashboardCards);
    }

    [Fact]
    public void Cards_know_whether_they_can_move()
    {
        var (_, editor, _) = NewEditor();

        Assert.False(editor.Cards[0].CanMoveUp);
        Assert.True(editor.Cards[0].CanMoveDown);
        Assert.True(editor.Cards[2].CanMoveUp);
        Assert.False(editor.Cards[2].CanMoveDown);
        Assert.All(editor.Cards, c => Assert.True(c.CanRemove));
    }

    [Fact]
    public void Card_commands_edit_the_list()
    {
        var (_, editor, _) = NewEditor();

        editor.Cards[1].MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "b", "a", "c" }, KeysOf(editor));

        editor.Cards[2].RemoveCommand.Execute(null);
        Assert.Equal(new[] { "b", "a" }, KeysOf(editor));

        editor.Available.First(x => x.Key == "c").AddCommand.Execute(null);
        Assert.Equal(new[] { "b", "a", "c" }, KeysOf(editor));
    }

    [Fact]
    public void Edit_mode_is_pushed_to_the_cards()
    {
        var (_, editor, _) = NewEditor();
        Assert.All(editor.Cards, c => Assert.False(c.IsEditing));
        Assert.Equal("Customize", editor.EditButtonText);

        editor.ToggleEditCommand.Execute(null);

        Assert.True(editor.IsEditing);
        Assert.All(editor.Cards, c => Assert.True(c.IsEditing));
        Assert.Equal("Done", editor.EditButtonText);

        editor.Add("d");                                      // a card added while editing is in edit mode too
        Assert.True(editor.Cards[3].IsEditing);
    }

    [Fact]
    public void Reset_goes_back_to_the_defaults()
    {
        var (_, editor, _) = NewEditor();
        editor.Add("d");
        editor.Remove("a");
        Assert.Equal(new[] { "b", "c", "d" }, KeysOf(editor));

        editor.Reset();

        Assert.Equal(new[] { "a", "b", "c" }, KeysOf(editor));
        Assert.Empty(new UiSettingsService(null, _folder).Current.DashboardCards);   // "empty" = follow the defaults
    }

    [Fact]
    public void A_saved_list_is_loaded_and_unknown_keys_are_dropped_on_the_first_edit()
    {
        new UiSettingsService(null, _folder).Update(s => s.DashboardCards = new List<string> { "c", "ghost", "a", "c" });
        var (_, editor, _) = NewEditor();

        Assert.Equal(new[] { "c", "a" }, KeysOf(editor));

        editor.Add("b");

        Assert.Equal(new[] { "c", "a", "b" }, new UiSettingsService(null, _folder).Current.DashboardCards);
    }

    [Fact]
    public void Dashboard_and_favorites_lists_are_independent()
    {
        var settings = new UiSettingsService(null, _folder);
        var dashboard = new CardListEditor(settings, TestRegistry(), CardListSlot.Dashboard, new[] { "a" }, i => new TestCard(i));
        var favorites = new CardListEditor(settings, TestRegistry(), CardListSlot.Favorites, new[] { "a" }, i => new TestCard(i));

        favorites.Add("b");

        Assert.Equal(new[] { "a" }, KeysOf(dashboard));
        Assert.Equal(new[] { "a", "b" }, KeysOf(favorites));
        Assert.Equal(new[] { "a", "b" }, settings.Current.FavoritesCards);
        Assert.Empty(settings.Current.DashboardCards);
    }

    [Fact]
    public void Changes_made_elsewhere_are_picked_up_and_stable_cards_survive()
    {
        var (settings, editor, _) = NewEditor();
        var a = editor.Cards[0];

        settings.Update(s => s.DashboardCards = new List<string> { "d", "a" });

        Assert.Equal(new[] { "d", "a" }, KeysOf(editor));
        Assert.Same(a, editor.Cards[1]);
    }

    [Fact]
    public void A_card_that_cannot_be_created_is_skipped()
    {
        var (_, editor, _) = NewEditor(create: info => info.Key == "b" ? throw new InvalidOperationException("boom") : new TestCard(info));

        Assert.Equal(new[] { "a", "c" }, KeysOf(editor));
    }

    [Fact]
    public void Dispose_disposes_all_cards_and_stops_listening()
    {
        var (settings, editor, created) = NewEditor();

        editor.Dispose();

        Assert.All(created, c => Assert.True(c.WasDisposed));
        Assert.Empty(editor.Cards);
        settings.Update(s => s.DashboardCards = new List<string> { "d" });
        Assert.Empty(editor.Cards);
    }

    [Fact]
    public void Cards_follow_the_active_state()
    {
        var (_, editor, _) = NewEditor();
        Assert.All(editor.Cards, c => Assert.False(c.IsActive));

        editor.SetActive(true);
        Assert.All(editor.Cards, c => Assert.True(c.IsActive));

        editor.Add("d");                                       // new cards join the current state
        Assert.True(editor.Cards[3].IsActive);

        editor.SetActive(false);
        Assert.All(editor.Cards, c => Assert.False(c.IsActive));
    }

    // ── Registry ──

    [Fact]
    public void Registry_finds_keys_ignoring_case_and_ignores_duplicates()
    {
        var registry = new DashboardCardRegistry(new[] { Info("a"), Info("A"), Info("b") });

        Assert.Equal(2, registry.All.Count);
        Assert.Equal("a", registry.Find("A")!.Key);
        Assert.Null(registry.Find("zzz"));
        Assert.Null(registry.Find(null));
        Assert.True(registry.IsKnown("B"));
    }

    [Fact]
    public void Default_registry_lists_the_eight_cards_with_complete_metadata()
    {
        var all = DashboardCardRegistry.Default.All;

        Assert.Equal(
            new[] { "audio", "volume", "metronome", "practice", "system", "notes", "clipboard", "standing" }.OrderBy(k => k),
            all.Select(c => c.Key).OrderBy(k => k));
        Assert.Equal(all.Count, all.Select(c => c.Key).Distinct().Count());

        foreach (var card in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Title), card.Key);
            Assert.False(string.IsNullOrWhiteSpace(card.Glyph), card.Key);
            Assert.False(string.IsNullOrWhiteSpace(card.Description), card.Key);
            Assert.NotNull(PageRegistry.Find(card.OpenPageKey));           // the header opens a real page
        }
    }

    [Fact]
    public void Default_card_sets_only_use_known_cards()
    {
        var registry = DashboardCardRegistry.Default;

        Assert.Equal(new[] { "audio", "metronome", "system", "practice", "volume", "notes" }, DashboardCardRegistry.DefaultDashboard);
        Assert.Equal(new[] { "system", "volume", "metronome" }, DashboardCardRegistry.DefaultFavorites);
        Assert.All(DashboardCardRegistry.DefaultDashboard, k => Assert.True(registry.IsKnown(k)));
        Assert.All(DashboardCardRegistry.DefaultFavorites, k => Assert.True(registry.IsKnown(k)));
    }

    // ── Small pure helpers ──

    [Theory]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(17, "Good afternoon")]
    [InlineData(18, "Good evening")]
    [InlineData(22, "Good evening")]
    [InlineData(23, "Good night")]
    [InlineData(2, "Good night")]
    public void Greeting_follows_the_hour(int hour, string expected) =>
        Assert.Equal(expected, DashboardViewModel.Greeting(new DateTime(2026, 10, 4, hour, 0, 0)));

    [Fact]
    public void Subtitle_has_greeting_and_date()
    {
        Assert.Equal("Good evening · Sunday 4 October", DashboardViewModel.Describe(new DateTime(2026, 10, 4, 19, 30, 0)));
    }

    [Fact]
    public void Notes_preview_is_the_first_three_nonempty_lines()
    {
        Assert.Equal("one\ntwo\nthree", NotesCardViewModel.Preview("\r\n one \r\n\r\ntwo\n  \nthree\nfour"));
        Assert.Equal("", NotesCardViewModel.Preview(null));
        Assert.Equal("", NotesCardViewModel.Preview("  \n "));
    }

    [Fact]
    public void Clipboard_entries_are_one_trimmed_line()
    {
        Assert.Equal("hello world again", ClipboardCardViewModel.OneLine("  hello\r\nworld\t again \n"));
        var long120 = ClipboardCardViewModel.OneLine(new string('x', 300));
        Assert.Equal(121, long120.Length);
        Assert.EndsWith("…", long120);
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(0, "0 days")]
    [InlineData(5, "5 days")]
    public void Streak_label(int days, string expected) => Assert.Equal(expected, PracticeCardViewModel.StreakLabel(days));

    [Theory]
    [InlineData(58.4f, "58°C")]
    [InlineData(0f, "0°C")]
    [InlineData(-1f, "")]
    public void Temperature_text_hides_missing_sensors(float celsius, string expected) =>
        Assert.Equal(expected, SystemCardViewModel.TemperatureText(celsius));
}
