using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>Which saved card list (in <see cref="UiSettings"/>) an editor works on.</summary>
    public sealed record CardListSlot(Func<UiSettings, List<string>> Get, Action<UiSettings, List<string>> Set)
    {
        public static CardListSlot Dashboard { get; } = new(s => s.DashboardCards, (s, v) => s.DashboardCards = v);
        public static CardListSlot Favorites { get; } = new(s => s.FavoritesCards, (s, v) => s.FavoritesCards = v);
    }

    /// <summary>A card that is not on the page yet, with its "+" command (the "Add card" list).</summary>
    public sealed class CardChoice
    {
        public CardChoice(DashboardCardInfo info, Action<string> add)
        {
            Key = info.Key;
            Title = info.Title;
            Glyph = info.Glyph;
            Description = info.Description;
            AddCommand = new RelayCommand(() => add(info.Key));
        }

        public string Key { get; }
        public string Title { get; }
        public string Glyph { get; }
        public string Description { get; }
        public ICommand AddCommand { get; }
    }

    /// <summary>
    /// The list of cards of one page (the Home dashboard or the Favorites window) and its editing:
    /// add / remove / move / reset, saved through <see cref="UiSettingsService"/>. The shared logic of
    /// both pages. Cards that stay on the page keep their view-model (and so their state) when the list
    /// changes; cards that leave are disposed.
    /// </summary>
    public sealed partial class CardListEditor : ObservableObject, IDisposable
    {
        private readonly UiSettingsService _settings;
        private readonly DashboardCardRegistry _registry;
        private readonly CardListSlot _slot;
        private readonly IReadOnlyList<string> _defaults;
        private readonly Func<DashboardCardInfo, DashboardCardViewModel?> _create;
        private readonly Action<Action>? _post;
        private readonly ILogger? _logger;

        private bool _updating;
        private bool _active;
        private bool _disposed;
        private string _availableSignature = "\0";

        /// <param name="settings">Where the list is saved.</param>
        /// <param name="registry">The cards that exist; other keys in the saved list are dropped.</param>
        /// <param name="slot">Which list of <see cref="UiSettings"/> this editor owns.</param>
        /// <param name="defaults">Shown while the saved list is empty.</param>
        /// <param name="create">Creates a card's view-model; null (or a throw) skips the card.</param>
        /// <param name="post">Runs an action on the UI thread, for settings changes made elsewhere. Null = run inline.</param>
        public CardListEditor(
            UiSettingsService settings,
            DashboardCardRegistry registry,
            CardListSlot slot,
            IReadOnlyList<string> defaults,
            Func<DashboardCardInfo, DashboardCardViewModel?> create,
            Action<Action>? post = null,
            ILogger? logger = null)
        {
            _settings = settings;
            _registry = registry;
            _slot = slot;
            _defaults = defaults;
            _create = create;
            _post = post;
            _logger = logger;

            _settings.Changed += OnSettingsChanged;
            Refresh();
        }

        /// <summary>The cards on the page, in order.</summary>
        public ObservableCollection<DashboardCardViewModel> Cards { get; } = new();

        /// <summary>Cards that exist but are not on the page.</summary>
        public ObservableCollection<CardChoice> Available { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditButtonText))]
        private bool _isEditing;

        [ObservableProperty] private bool _hasAvailable;

        public string EditButtonText => IsEditing ? "Done" : "Customize";

        /// <summary>The keys of the cards on the page, in order.</summary>
        public IReadOnlyList<string> Keys => Cards.Select(c => c.Key).ToList();

        // ── Pure list logic ──

        /// <summary>
        /// The card keys to show for a saved list: known keys only (canonical spelling), no duplicates,
        /// order kept; the defaults when nothing usable is left.
        /// </summary>
        public static List<string> Resolve(IEnumerable<string>? saved, DashboardCardRegistry registry, IReadOnlyList<string> defaults)
        {
            var result = Known(saved, registry);
            return result.Count > 0 ? result : Known(defaults, registry);
        }

        private static List<string> Known(IEnumerable<string>? keys, DashboardCardRegistry registry)
        {
            var list = new List<string>();
            foreach (var key in keys ?? Array.Empty<string>())
            {
                var info = registry.Find(key);
                if (info != null && !list.Contains(info.Key))
                    list.Add(info.Key);
            }
            return list;
        }

        // ── Editing (every change is saved, then the cards follow) ──

        public bool Add(string key) => Mutate(list =>
        {
            var info = _registry.Find(key);
            if (info == null || list.Contains(info.Key)) return false;
            list.Add(info.Key);
            return true;
        });

        /// <summary>Removes a card. The last card cannot be removed (an empty list means "the defaults").</summary>
        public bool Remove(string key) => Mutate(list => list.Count > 1 && list.Remove(key));

        public bool MoveUp(string key) => Mutate(list => Swap(list, key, -1));

        public bool MoveDown(string key) => Mutate(list => Swap(list, key, +1));

        /// <summary>Back to the default cards (the saved list is cleared, so future default changes apply too).</summary>
        public void Reset()
        {
            if (_disposed) return;
            if (_slot.Get(_settings.Current).Count > 0)
                Persist(new List<string>());
            else
                Refresh();
        }

        private static bool Swap(List<string> list, string key, int delta)
        {
            int from = list.IndexOf(key);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= list.Count) return false;
            (list[from], list[to]) = (list[to], list[from]);
            return true;
        }

        private bool Mutate(Func<List<string>, bool> change)
        {
            if (_disposed) return false;
            var list = Resolve(_slot.Get(_settings.Current), _registry, _defaults);
            if (!change(list)) return false;
            Persist(list);
            return true;
        }

        private void Persist(List<string> list)
        {
            _updating = true; // we refresh ourselves below; ignore the Changed echo
            try { _settings.Update(s => _slot.Set(s, list)); }
            finally { _updating = false; }
            Refresh();
        }

        [RelayCommand]
        private void ToggleEdit() => IsEditing = !IsEditing;

        [RelayCommand]
        private void ResetToDefault() => Reset();

        partial void OnIsEditingChanged(bool value) => UpdatePositions();

        // ── Keeping the cards in step with the saved list ──

        private void OnSettingsChanged(UiSettings _)
        {
            if (_disposed || _updating) return;
            if (_post != null) _post(Refresh);
            else Refresh();
        }

        /// <summary>Makes <see cref="Cards"/> match the saved list: removes and disposes what left, creates what is new, reorders. Cards that stay are untouched.</summary>
        public void Refresh()
        {
            if (_disposed) return;

            var desired = Resolve(_slot.Get(_settings.Current), _registry, _defaults);

            for (int i = Cards.Count - 1; i >= 0; i--)
            {
                if (desired.Contains(Cards[i].Key)) continue;
                var gone = Cards[i];
                Cards.RemoveAt(i);
                SafeDispose(gone);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                int at = IndexOfCard(desired[i]);
                if (at == i) continue;
                if (at > i)
                {
                    Cards.Move(at, i);
                    continue;
                }

                var card = TryCreate(desired[i]);
                if (card == null)
                {
                    desired.RemoveAt(i); // could not be created: leave it out of this page for now
                    i--;
                    continue;
                }
                card.Attach(this);
                card.SetActive(_active);
                Cards.Insert(i, card);
            }

            UpdatePositions();
            RebuildAvailable(desired);
        }

        private int IndexOfCard(string key)
        {
            for (int i = 0; i < Cards.Count; i++)
                if (Cards[i].Key == key) return i;
            return -1;
        }

        private DashboardCardViewModel? TryCreate(string key)
        {
            var info = _registry.Find(key);
            if (info == null) return null;
            try { return _create(info); }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Could not create the {Card} card", key);
                return null;
            }
        }

        private void UpdatePositions()
        {
            for (int i = 0; i < Cards.Count; i++)
                Cards[i].SetPosition(i, Cards.Count, IsEditing);
        }

        private void RebuildAvailable(List<string> shown)
        {
            var missing = _registry.All.Where(c => !shown.Contains(c.Key)).ToList();
            var signature = string.Join("|", missing.Select(c => c.Key));
            if (signature == _availableSignature) return;
            _availableSignature = signature;

            Available.Clear();
            foreach (var info in missing)
                Available.Add(new CardChoice(info, key => Add(key)));
            HasAvailable = Available.Count > 0;
        }

        /// <summary>Tells every card whether its page is on screen (timers run only then).</summary>
        public void SetActive(bool active)
        {
            if (_disposed) return;
            _active = active;
            foreach (var card in Cards.ToList())
                card.SetActive(active);
        }

        private void SafeDispose(DashboardCardViewModel card)
        {
            try { card.Dispose(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Disposing the {Card} card failed", card.Key); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settings.Changed -= OnSettingsChanged;

            var cards = Cards.ToList();
            Cards.Clear();
            foreach (var card in cards)
                SafeDispose(card);
        }
    }
}
