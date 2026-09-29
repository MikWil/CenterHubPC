using System;
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
    /// <summary>
    /// Backs the "Voicemeeter" card on the Sound page. Owns device selection, gains
    /// and monitoring, and the Enable/Disable/Open commands. Profile-driven changes
    /// go straight through <see cref="VoicemeeterModeService"/>; this VM just mirrors
    /// state via <see cref="RefreshState"/>.
    /// </summary>
    public partial class VoicemeeterViewModel : BaseViewModel
    {
        private readonly IVoicemeeterService _voicemeeter;
        private readonly VoicemeeterModeService _mode;
        private readonly IAudioDeviceService _audio;
        private readonly VoicemeeterSettingsService _settingsService;

        private bool _isLoading;

        [ObservableProperty] private bool isInstalled;
        [ObservableProperty] private string statusText = "Checking…";
        [ObservableProperty] private bool isModeEnabled;
        [ObservableProperty] private bool isBusy;

        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> microphoneDevices = new();
        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> guitarDevices = new();
        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> monitorDevices = new();

        [ObservableProperty] private AudioDeviceInfo? selectedMicrophone;
        [ObservableProperty] private AudioDeviceInfo? selectedGuitar;
        [ObservableProperty] private AudioDeviceInfo? selectedMonitor;

        [ObservableProperty] private double micGainDb;
        [ObservableProperty] private double guitarGainDb;

        [ObservableProperty] private bool monitorMicrophone;
        [ObservableProperty] private bool monitorGuitar;

        [ObservableProperty] private bool guitarUnavailable;
        [ObservableProperty] private string guitarUnavailableText = "";

        public string EnableButtonText => IsModeEnabled ? "Disable / Restore Audio" : "Enable Guitar + Discord";

        public VoicemeeterViewModel(
            IVoicemeeterService voicemeeter,
            VoicemeeterModeService mode,
            IAudioDeviceService audio,
            VoicemeeterSettingsService settingsService,
            ILogger<VoicemeeterViewModel>? logger = null) : base(logger)
        {
            _voicemeeter = voicemeeter;
            _mode = mode;
            _audio = audio;
            _settingsService = settingsService;

            LoadSettingsAndDevices();
            RefreshState();
        }

        // ─────────────────── Loading ───────────────────

        private void LoadSettingsAndDevices()
        {
            _ = Task.Run(() =>
            {
                var settings = _settingsService.Load().Settings;
                var mics = _audio.GetRecordingDevices();
                var monitors = _audio.GetPlaybackDevices();

                try
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed) return;
                        _isLoading = true;
                        try
                        {
                            MicrophoneDevices = new ObservableCollection<AudioDeviceInfo>(mics);
                            GuitarDevices = new ObservableCollection<AudioDeviceInfo>(mics); // guitar is also a capture device
                            MonitorDevices = new ObservableCollection<AudioDeviceInfo>(monitors);

                            SelectedMicrophone = Match(MicrophoneDevices, settings.MicrophoneDeviceId, settings.MicrophoneDeviceName);
                            SelectedGuitar = Match(GuitarDevices, settings.GuitarDeviceId, settings.GuitarDeviceName);
                            SelectedMonitor = Match(MonitorDevices, settings.MonitorDeviceId, settings.MonitorDeviceName);

                            MicGainDb = settings.MicGainDb;
                            GuitarGainDb = settings.GuitarGainDb;
                            MonitorMicrophone = settings.MonitorMicrophone;
                            MonitorGuitar = settings.MonitorGuitar;

                            // "Boss Katana is unavailable" when a saved guitar can't be found.
                            if (SelectedGuitar is null && !string.IsNullOrWhiteSpace(settings.GuitarDeviceName))
                            {
                                GuitarUnavailable = true;
                                GuitarUnavailableText = $"{settings.GuitarDeviceName} is unavailable";
                            }
                            else
                            {
                                GuitarUnavailable = false;
                                GuitarUnavailableText = "";
                            }
                        }
                        finally { _isLoading = false; }
                    });
                }
                catch (InvalidOperationException) { /* UI gone */ }
            });
        }

        private static AudioDeviceInfo? Match(ObservableCollection<AudioDeviceInfo> list, string? id, string? name)
        {
            if (!string.IsNullOrEmpty(id))
            {
                var byId = list.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }
            if (!string.IsNullOrEmpty(name))
                return list.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            return null;
        }

        /// <summary>Re-read install/running/active state from the services and update the UI.</summary>
        public void RefreshState()
        {
            _ = Task.Run(() =>
            {
                var status = _voicemeeter.RefreshStatus();
                var kind = _voicemeeter.Kind;
                var installed = _voicemeeter.IsInstalled;
                var active = _mode.IsActive;

                try
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDisposed) return;
                        IsInstalled = installed;
                        IsModeEnabled = active;
                        StatusText = !installed
                            ? "Banana: Not installed"
                            : status switch
                            {
                                VoicemeeterStatus.Running => $"Banana: Running ({kind})",
                                VoicemeeterStatus.Stopped => "Banana: Stopped",
                                _ => "Banana: Not installed"
                            };
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        // ─────────────────── Persistence ───────────────────

        private VoicemeeterSettings BuildSettings()
        {
            var existing = _settingsService.Load().Settings;
            return new VoicemeeterSettings
            {
                MicrophoneDeviceId = SelectedMicrophone?.Id,
                MicrophoneDeviceName = SelectedMicrophone?.Name,
                GuitarDeviceId = SelectedGuitar?.Id,
                GuitarDeviceName = SelectedGuitar?.Name,
                MonitorDeviceId = SelectedMonitor?.Id,
                MonitorDeviceName = SelectedMonitor?.Name,
                MicGainDb = (float)MicGainDb,
                GuitarGainDb = (float)GuitarGainDb,
                MonitorMicrophone = MonitorMicrophone,
                MonitorGuitar = MonitorGuitar,
                PreferredSendBus = existing.PreferredSendBus,
                AutoRestoreOnStartup = existing.AutoRestoreOnStartup,
            };
        }

        private void Persist()
        {
            if (_isLoading || IsDisposed) return;
            try { _settingsService.SaveSettings(BuildSettings()); }
            catch (Exception ex) { Logger?.LogError(ex, "Failed to persist Voicemeeter settings"); }
        }

        partial void OnSelectedMicrophoneChanged(AudioDeviceInfo? value) => Persist();
        partial void OnSelectedGuitarChanged(AudioDeviceInfo? value)
        {
            if (value != null) { GuitarUnavailable = false; GuitarUnavailableText = ""; }
            Persist();
        }
        partial void OnSelectedMonitorChanged(AudioDeviceInfo? value) => Persist();
        partial void OnMicGainDbChanged(double value) => Persist();
        partial void OnGuitarGainDbChanged(double value) => Persist();
        partial void OnMonitorMicrophoneChanged(bool value) => Persist();
        partial void OnMonitorGuitarChanged(bool value) => Persist();
        partial void OnIsModeEnabledChanged(bool value) => OnPropertyChanged(nameof(EnableButtonText));

        // ─────────────────── Commands ───────────────────

        [RelayCommand]
        private async Task ToggleModeAsync()
        {
            if (IsBusy || IsDisposed) return;

            if (!IsInstalled)
            {
                ToastService.Instance.Warning("Voicemeeter is not installed.");
                return;
            }

            IsBusy = true;
            try
            {
                Persist();
                var result = IsModeEnabled
                    ? await _mode.DisableAsync().ConfigureAwait(true)
                    : await _mode.EnableAsync(BuildSettings()).ConfigureAwait(true);

                if (result.Success) ToastService.Instance.Success(result.Message);
                else ToastService.Instance.Error(result.Message);

                RefreshState();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Voicemeeter toggle failed");
                ToastService.Instance.Error($"Voicemeeter error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void OpenVoicemeeter()
        {
            if (!IsInstalled) { ToastService.Instance.Warning("Voicemeeter is not installed."); return; }
            try { _voicemeeter.OpenUi(); RefreshState(); }
            catch (Exception ex) { Logger?.LogError(ex, "Failed to open Voicemeeter"); }
        }

        [RelayCommand]
        private void RefreshDevices()
        {
            LoadSettingsAndDevices();
            RefreshState();
        }
    }
}
