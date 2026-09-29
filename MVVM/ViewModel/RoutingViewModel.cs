using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>A preset pill in the board's top bar.</summary>
    public partial class PresetChip : ObservableObject
    {
        public string Id { get; }
        public string Icon { get; }
        [ObservableProperty] private string name;
        [ObservableProperty] private bool isActive;

        public PresetChip(AudioRoutingPreset p)
        {
            Id = p.Id;
            Icon = string.IsNullOrEmpty(p.Icon) ? "ti-adjustments" : p.Icon;
            name = p.Name;
        }
    }

    /// <summary>One source card on the board: its two routing toggles, level and mute.</summary>
    public partial class RoutingSourceRow : ObservableObject
    {
        public AudioSourceKind Kind { get; }
        public string Icon { get; }
        public bool IsVirtualApp { get; }
        public bool CanSendToOthers { get; }
        public string? WindowsPlaybackName { get; }

        [ObservableProperty] private string displayName;
        [ObservableProperty] private bool toYou;
        [ObservableProperty] private bool toOthers;
        [ObservableProperty] private double gainDb;
        [ObservableProperty] private bool muted;

        // App-slot in-app assignment (virtual sources only).
        public ObservableCollection<AudioAppInfo> Apps { get; }
        [ObservableProperty] private AudioAppInfo? selectedApp;

        private readonly Action<RoutingSourceRow>? _onChanged;
        private readonly Func<AudioSourceKind, AudioAppInfo, bool>? _assignApp;

        public RoutingSourceRow(
            AudioSource src, AudioSourceRoute? route, Action<RoutingSourceRow>? onChanged,
            System.Collections.Generic.IReadOnlyList<AudioAppInfo>? apps = null,
            Func<AudioSourceKind, AudioAppInfo, bool>? assignApp = null)
        {
            Kind = src.Kind;
            Icon = src.Icon;
            IsVirtualApp = src.IsVirtualApp;
            CanSendToOthers = src.CanSendToOthers;
            WindowsPlaybackName = src.WindowsPlaybackName;
            // Only app slots carry a user-editable name; fixed sources always show their proper label.
            displayName = (src.IsVirtualApp && !string.IsNullOrWhiteSpace(route?.DisplayName))
                ? route!.DisplayName!
                : src.DefaultName;
            toYou = route?.ToYou ?? false;
            toOthers = route?.ToOthers ?? false;
            gainDb = route?.GainDb ?? 0f;
            muted = route?.Muted ?? false;
            _onChanged = onChanged;
            _assignApp = assignApp;
            Apps = new ObservableCollection<AudioAppInfo>(apps ?? System.Array.Empty<AudioAppInfo>());
        }

        partial void OnDisplayNameChanged(string value) => _onChanged?.Invoke(this);
        partial void OnToYouChanged(bool value) => _onChanged?.Invoke(this);
        partial void OnToOthersChanged(bool value) => _onChanged?.Invoke(this);
        partial void OnGainDbChanged(double value) => _onChanged?.Invoke(this);
        partial void OnMutedChanged(bool value) => _onChanged?.Invoke(this);

        partial void OnSelectedAppChanged(AudioAppInfo? value)
        {
            if (value is null || _assignApp is null) return;
            if (_assignApp(Kind, value))
                ToastService.Instance.Success($"{value.DisplayName} → {DisplayName}");
            else
                ToastService.Instance.Warning($"Couldn't set {value.DisplayName} automatically on this Windows build — use \"Assign app in Windows…\" instead.");
        }

        public AudioSourceRoute ToRoute() => new()
        {
            Kind = Kind,
            DisplayName = DisplayName,
            ToYou = ToYou,
            ToOthers = ToOthers,
            GainDb = (float)GainDb,
            Muted = Muted
        };
    }

    /// <summary>
    /// Backs the flow-routing board on the Sound tab. Presets on top; each source row
    /// has "You"/"Others" toggles and a level. Editing a row updates the selected preset
    /// (persisted) and, when Voicemeeter is running and that preset is applied, pushes the
    /// change live. Selecting a preset applies it fully (starts/configures Banana).
    /// </summary>
    public partial class RoutingViewModel : BaseViewModel
    {
        private readonly AudioRoutingService _routing;

        /// <summary>Device selection (mic/guitar/monitor) folded into the board's setup area.</summary>
        public VoicemeeterViewModel? Devices { get; }

        private List<AudioRoutingPreset> _presets = new();
        private AudioRoutingPreset _current = new();
        private List<AudioAppInfo> _apps = new();
        private bool _loading;

        [ObservableProperty] private ObservableCollection<PresetChip> presetChips = new();
        [ObservableProperty] private ObservableCollection<RoutingSourceRow> sources = new();

        [ObservableProperty] private string youHearSummary = "";
        [ObservableProperty] private string othersHearSummary = "";
        [ObservableProperty] private bool isInstalled;
        [ObservableProperty] private string statusText = "Checking…";
        [ObservableProperty] private bool isBusy;

        public RoutingViewModel(
            AudioRoutingService routing,
            VoicemeeterViewModel? devices = null,
            ILogger<RoutingViewModel>? logger = null) : base(logger)
        {
            _routing = routing;
            Devices = devices ?? App.Services?.GetService(typeof(VoicemeeterViewModel)) as VoicemeeterViewModel;

            _presets = _routing.LoadPresets();
            var activeId = _routing.ActivePresetId;
            _current = _presets.FirstOrDefault(p => p.Id == activeId) ?? _presets.First();

            LoadApps();
            RebuildChips();
            LoadSourcesFor(_current);          // populate board, but do NOT auto-apply on startup
            RecomputeSummaries();
            RefreshState();
        }

        private void RebuildChips()
        {
            PresetChips = new ObservableCollection<PresetChip>(_presets.Select(p => new PresetChip(p) { IsActive = p.Id == _current.Id }));
        }

        private void LoadSourcesFor(AudioRoutingPreset preset)
        {
            _loading = true;
            try
            {
                CurrentPresetName = preset.Name;
                var available = _routing.GetAvailableSources();
                var rows = available.Select(src =>
                {
                    var route = preset.Routes.FirstOrDefault(r => r.Kind == src.Kind);
                    return src.IsVirtualApp
                        ? new RoutingSourceRow(src, route, OnRowChanged, _apps, AssignAppToSlot)
                        : new RoutingSourceRow(src, route, OnRowChanged);
                });
                Sources = new ObservableCollection<RoutingSourceRow>(rows);
            }
            finally { _loading = false; }
        }

        private void LoadApps()
        {
            try { _apps = _routing.GetAudioApps().ToList(); }
            catch { _apps = new List<AudioAppInfo>(); }
        }

        private bool AssignAppToSlot(AudioSourceKind kind, AudioAppInfo app)
            => _routing.AssignAppToSlot(app, kind);

        [RelayCommand]
        private void RefreshApps()
        {
            LoadApps();
            LoadSourcesFor(_current);
            RecomputeSummaries();
        }

        private void OnRowChanged(RoutingSourceRow row)
        {
            if (_loading || IsDisposed) return;

            // Fold the board back into the current preset and persist.
            _current.Routes = Sources.Select(s => s.ToRoute()).ToList();
            _routing.SavePresets(_presets, _current.Id);
            RecomputeSummaries();

            // Live-push this one source whenever Banana is running — the board is the
            // live routing state, so an edit takes effect immediately.
            if (_routing.IsRunning)
                _routing.LiveSet(row.ToRoute());
        }

        private void RecomputeSummaries()
        {
            string Join(Func<RoutingSourceRow, bool> pred)
            {
                var names = Sources.Where(s => pred(s) && !s.Muted).Select(s => s.DisplayName).ToList();
                return names.Count == 0 ? "Nothing routed here" : string.Join(" · ", names);
            }
            YouHearSummary = Join(s => s.ToYou);
            OthersHearSummary = Join(s => s.ToOthers);
        }

        public void RefreshState()
        {
            _ = Task.Run(() =>
            {
                var installed = _routing.IsInstalled;
                var running = installed && _routing.RefreshStatus();
                try
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed) return;
                        IsInstalled = installed;
                        StatusText = !installed ? "Banana: Not installed"
                                   : running ? "Banana: Running"
                                   : "Banana: Stopped";
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        [RelayCommand]
        private async Task ApplyPreset(PresetChip? chip)
        {
            if (chip is null || IsBusy || IsDisposed) return;
            var preset = _presets.FirstOrDefault(p => p.Id == chip.Id);
            if (preset is null) return;

            _current = preset;
            foreach (var c in PresetChips) c.IsActive = c.Id == preset.Id;
            LoadSourcesFor(preset);
            RecomputeSummaries();

            if (!_routing.IsInstalled)
            {
                ToastService.Instance.Warning("Voicemeeter is not installed.");
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _routing.ApplyPresetAsync(preset).ConfigureAwait(true);
                if (result.Success) ToastService.Instance.Success(result.Message);
                else ToastService.Instance.Error(result.Message);
                RefreshState();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Apply preset failed");
                ToastService.Instance.Error($"Routing error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task Resync()
        {
            if (IsBusy || IsDisposed) return;
            if (!_routing.IsInstalled)
            {
                ToastService.Instance.Warning("Voicemeeter is not installed.");
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _routing.ResyncAsync(_current).ConfigureAwait(true);
                if (result.Success) ToastService.Instance.Success(result.Message);
                else ToastService.Instance.Error(result.Message);

                LoadSourcesFor(_current);   // re-enumerate sources for the running edition
                RecomputeSummaries();
                RefreshState();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Voicemeeter re-sync failed");
                ToastService.Instance.Error($"Re-sync error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void NewPreset()
        {
            // Duplicate the current board as a new, editable preset.
            var preset = new AudioRoutingPreset
            {
                Name = "New preset",
                Icon = "ti-adjustments",
                Routes = Sources.Select(s => s.ToRoute()).ToList()
            };
            _presets.Add(preset);
            _current = preset;
            _routing.SavePresets(_presets, _current.Id);
            RebuildChips();
            ToastService.Instance.Success("Preset created — rename it in the box.");
        }

        [RelayCommand]
        private void DeleteCurrent()
        {
            if (_presets.Count <= 1) return; // keep at least one
            _presets.Remove(_current);
            _current = _presets.First();
            LoadSourcesFor(_current);
            RecomputeSummaries();
            _routing.SavePresets(_presets, _current.Id);
            RebuildChips();
        }

        /// <summary>Rename the currently-selected preset (bound to an editable text box).</summary>
        [ObservableProperty] private string currentPresetName = "";

        partial void OnCurrentPresetNameChanged(string value)
        {
            if (_loading || IsDisposed || _current is null) return;
            if (string.IsNullOrWhiteSpace(value)) return;
            _current.Name = value;
            var chip = PresetChips.FirstOrDefault(c => c.Id == _current.Id);
            if (chip != null) chip.Name = value;
            _routing.SavePresets(_presets, _current.Id);
        }

        [RelayCommand]
        private void OpenAppVolume(RoutingSourceRow? row)
        {
            _routing.OpenAppVolumeSettings();
            if (row?.WindowsPlaybackName is { } name)
                ToastService.Instance.Info($"Windows opened — under the app, set Output to \"{name}\", then toggle You/Others here.");
        }

        [RelayCommand]
        private void Refresh()
        {
            RefreshState();
            LoadApps();
            LoadSourcesFor(_current);
            RecomputeSummaries();
            Devices?.RefreshDevicesCommand.Execute(null);
            ToastService.Instance.Info("Refreshed apps, devices and Voicemeeter status.");
        }
    }
}
