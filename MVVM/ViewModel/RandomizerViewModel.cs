using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class RandomizerOption : ObservableObject
    {
        [ObservableProperty] private string _label = "";
        [ObservableProperty] private double _weight = 1.0;
        [ObservableProperty] private bool   _isHighlighted;   // briefly during spin
        [ObservableProperty] private bool   _isWinner;        // sticks after spin completes
    }

    public sealed class RandomizerHistoryEntry
    {
        public string   Label { get; init; } = "";
        public DateTime At    { get; init; }
        public string   AtDisplay => At.ToString("HH:mm:ss");
    }

    public partial class RandomizerViewModel : BaseViewModel
    {
        private readonly Random _rng = new();
        private readonly RandomizerSoundService? _sound;

        // ── Audio ──
        [ObservableProperty] private bool _soundEnabled = true;

        // ── Editable options ──
        [ObservableProperty] private ObservableCollection<RandomizerOption> _options = new();
        [ObservableProperty] private string _newOptionName = "";

        // ── Mode ──
        [ObservableProperty] private bool _useWeights;
        [ObservableProperty] private bool _animate = true;

        // ── Live state ──
        [ObservableProperty] private bool   _isSpinning;
        [ObservableProperty] private string _resultLabel = "Add options and press Pick";
        [ObservableProperty] private bool   _hasResult;
        [ObservableProperty] private ObservableCollection<RandomizerHistoryEntry> _history = new();

        public RandomizerViewModel(
            RandomizerSoundService? sound = null,
            ILogger<RandomizerViewModel>? logger = null) : base(logger)
        {
            _sound = sound;
            // Sensible seed examples — the user can edit/replace
            Options.Add(new RandomizerOption { Label = "Pizza"  });
            Options.Add(new RandomizerOption { Label = "Burger" });
            Options.Add(new RandomizerOption { Label = "Sushi"  });
            Options.Add(new RandomizerOption { Label = "Pasta"  });
            Options.Add(new RandomizerOption { Label = "Tacos"  });
            Options.Add(new RandomizerOption { Label = "Salad"  });
        }

        // =====================================================
        //  Option editing
        // =====================================================

        private bool CanEditOptions() => !IsSpinning;

        partial void OnIsSpinningChanged(bool value)
        {
            AddOptionCommand.NotifyCanExecuteChanged();
            RemoveOptionCommand.NotifyCanExecuteChanged();
            ClearAllCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanEditOptions))]
        private void AddOption()
        {
            if (IsSpinning) return;
            var name = string.IsNullOrWhiteSpace(NewOptionName)
                ? $"Option {Options.Count + 1}"
                : NewOptionName.Trim();
            Options.Add(new RandomizerOption { Label = name });
            NewOptionName = "";
        }

        [RelayCommand(CanExecute = nameof(CanEditOptions))]
        private void RemoveOption(RandomizerOption? opt)
        {
            if (IsSpinning || opt is null) return;
            Options.Remove(opt);
        }

        [RelayCommand(CanExecute = nameof(CanEditOptions))]
        private void ClearAll()
        {
            if (IsSpinning) return;
            Options.Clear();
            History.Clear();
            ResultLabel = "Add options and press Pick";
            HasResult = false;
        }

        [RelayCommand]
        private void ClearHistory()
        {
            History.Clear();
        }

        // =====================================================
        //  Roll
        // =====================================================

        [RelayCommand]
        private async Task PickAsync()
        {
            if (IsSpinning) return;
            if (Options.Count == 0)
            {
                ToastService.Instance.Warning("Add at least one option");
                return;
            }
            if (Options.Count == 1)
            {
                var only = Options[0];
                foreach (var o in Options) { o.IsHighlighted = false; o.IsWinner = false; }
                ApplyWinner(only);
                return;
            }

            // Work on a snapshot so the animation can never index a mutated collection
            var snapshot = Options.ToList();
            RandomizerOption? highlighted = null;

            IsSpinning = true;
            try
            {
                HasResult = false;
                foreach (var o in snapshot) { o.IsHighlighted = false; o.IsWinner = false; }

                var winner = ChooseWinnerIndex(snapshot);

                if (Animate)
                {
                    // Cycle the highlight forward through the list, decelerating
                    // until we land on the chosen winner.
                    int count = snapshot.Count;
                    int startIdx = 0;
                    int currentIdx = startIdx;
                    int relativeWinnerSteps =
                        ((winner - startIdx) % count + count) % count;
                    int extraLaps = 3;
                    int totalSteps = relativeWinnerSteps + count * extraLaps;

                    for (int i = 1; i <= totalSteps; i++)
                    {
                        if (IsDisposed) return;

                        if (currentIdx >= 0 && currentIdx < count)
                            snapshot[currentIdx].IsHighlighted = false;
                        currentIdx = (currentIdx + 1) % count;
                        highlighted = snapshot[currentIdx];
                        highlighted.IsHighlighted = true;

                        // Soft low tick on each step while the wheel rolls
                        if (SoundEnabled) _sound?.PlayTick();

                        // Quadratic ease-out — fast at start, slower near the end
                        double t = (double)i / totalSteps;
                        int delay = (int)(35 + 230 * Math.Pow(t, 2.4));
                        await Task.Delay(delay);
                    }

                    if (highlighted is not null) highlighted.IsHighlighted = false;
                    highlighted = null;
                }

                if (IsDisposed) return;
                if (winner >= 0 && winner < snapshot.Count)
                    ApplyWinner(snapshot[winner]);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Randomizer pick failed");
            }
            finally
            {
                if (highlighted is not null) highlighted.IsHighlighted = false;
                foreach (var o in snapshot) o.IsHighlighted = false;
                IsSpinning = false;
            }
        }

        private void ApplyWinner(RandomizerOption opt)
        {
            opt.IsWinner = true;
            ResultLabel = opt.Label;
            HasResult = true;
            History.Insert(0, new RandomizerHistoryEntry { Label = opt.Label, At = DateTime.Now });
            while (History.Count > 20) History.RemoveAt(History.Count - 1);
            ToastService.Instance.Success($"Picked: {opt.Label}");
            if (SoundEnabled) _sound?.PlayWin();
        }

        private int ChooseWinnerIndex(IReadOnlyList<RandomizerOption> items)
        {
            if (!UseWeights || items.Count == 0)
                return _rng.Next(items.Count);

            // Weighted uniform: clamp each weight to a positive minimum so a 0-weight
            // option can still be picked (otherwise the user gets stuck wondering why)
            double total = 0;
            foreach (var o in items) total += Math.Max(0.05, o.Weight);

            double pick = _rng.NextDouble() * total;
            double cum  = 0;
            for (int i = 0; i < items.Count; i++)
            {
                cum += Math.Max(0.05, items[i].Weight);
                if (pick <= cum) return i;
            }
            return items.Count - 1;
        }

        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }
}
