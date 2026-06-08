using System;
using System.Collections.ObjectModel;
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

        [RelayCommand]
        private void AddOption()
        {
            var name = string.IsNullOrWhiteSpace(NewOptionName)
                ? $"Option {Options.Count + 1}"
                : NewOptionName.Trim();
            Options.Add(new RandomizerOption { Label = name });
            NewOptionName = "";
        }

        [RelayCommand]
        private void RemoveOption(RandomizerOption? opt)
        {
            if (opt is null) return;
            Options.Remove(opt);
        }

        [RelayCommand]
        private void ClearAll()
        {
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
                ApplyWinner(0);
                return;
            }

            IsSpinning = true;
            HasResult = false;
            foreach (var o in Options) { o.IsHighlighted = false; o.IsWinner = false; }

            var winner = ChooseWinnerIndex();

            if (Animate)
            {
                // Cycle the highlight forward through the list, decelerating
                // until we land on the chosen winner.
                int startIdx = 0;
                int currentIdx = startIdx;
                int relativeWinnerSteps =
                    ((winner - startIdx) % Options.Count + Options.Count) % Options.Count;
                int extraLaps = 3;
                int totalSteps = relativeWinnerSteps + Options.Count * extraLaps;

                for (int i = 1; i <= totalSteps; i++)
                {
                    if (IsDisposed) return;

                    Options[currentIdx].IsHighlighted = false;
                    currentIdx = (currentIdx + 1) % Options.Count;
                    Options[currentIdx].IsHighlighted = true;

                    // Soft low tick on each step while the wheel rolls
                    if (SoundEnabled) _sound?.PlayTick();

                    // Quadratic ease-out — fast at start, slower near the end
                    double t = (double)i / totalSteps;
                    int delay = (int)(35 + 230 * Math.Pow(t, 2.4));
                    await Task.Delay(delay);
                }

                Options[currentIdx].IsHighlighted = false;
            }

            ApplyWinner(winner);
        }

        private void ApplyWinner(int idx)
        {
            Options[idx].IsWinner = true;
            ResultLabel = Options[idx].Label;
            HasResult = true;
            History.Insert(0, new RandomizerHistoryEntry { Label = Options[idx].Label, At = DateTime.Now });
            while (History.Count > 20) History.RemoveAt(History.Count - 1);
            ToastService.Instance.Success($"Picked: {Options[idx].Label}");
            if (SoundEnabled) _sound?.PlayWin();
            IsSpinning = false;
        }

        private int ChooseWinnerIndex()
        {
            if (!UseWeights || Options.Count == 0)
                return _rng.Next(Options.Count);

            // Weighted uniform: clamp each weight to a positive minimum so a 0-weight
            // option can still be picked (otherwise the user gets stuck wondering why)
            double total = 0;
            foreach (var o in Options) total += Math.Max(0.05, o.Weight);

            double pick = _rng.NextDouble() * total;
            double cum  = 0;
            for (int i = 0; i < Options.Count; i++)
            {
                cum += Math.Max(0.05, Options[i].Weight);
                if (pick <= cum) return i;
            }
            return Options.Count - 1;
        }

        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }
}
