using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>One dot of the step indicator.</summary>
    public sealed class WizardStepDot
    {
        public WizardStepDot(bool isCurrent, bool isDone) { IsCurrent = isCurrent; IsDone = isDone; }
        public bool IsCurrent { get; }
        public bool IsDone { get; }
    }

    /// <summary>A routing preset shown as a chip on the Routing step.</summary>
    public sealed class WizardPresetChip
    {
        public WizardPresetChip(AudioRoutingPreset preset, bool isActive) { Preset = preset; IsActive = isActive; }
        public AudioRoutingPreset Preset { get; }
        public string Name => Preset.Name;
        public bool IsActive { get; }
    }

    /// <summary>One line of a setup check: ✓ / ! / ✕, title, detail and (optionally) what fixes it.</summary>
    public sealed class WizardCheckRow
    {
        public WizardCheckRow(AudioCheck check)
        {
            Icon = check.Status switch
            {
                AudioCheckStatus.Ok => "✓",
                AudioCheckStatus.Warning => "!",
                _ => "✕",
            };
            IconColor = check.StatusColor;
            Title = check.Title;
            Detail = check.Detail;
            FixText = check.HasFix ? $"Suggested fix: {check.FixLabel}" : "";
        }

        public string Icon { get; }
        public string IconColor { get; }
        public string Title { get; }
        public string Detail { get; }
        public string FixText { get; }
        public bool HasFix => FixText.Length > 0;
    }

    /// <summary>
    /// First-run audio setup wizard (shown as the shell's full-window overlay): Voicemeeter present →
    /// devices → routing preset → Discord → test sound. Every call that can block (device
    /// enumeration, status checks, applying routing) runs off the UI thread; results come back
    /// through <c>runOnUi</c> (the UI dispatcher in the app, inline in tests).
    /// </summary>
    public partial class SetupWizardViewModel : BaseViewModel, IOverlayViewModel
    {
        public const int StepCount = 5;
        private const string VoicemeeterUrl = "https://vb-audio.com/Voicemeeter/banana.htm";

        private readonly AudioRoutingService _routing;
        private readonly IAudioDeviceService _audio;
        private readonly VoicemeeterSettingsService _store;
        private readonly UiSettingsService _ui;
        private readonly Action<Action> _runOnUi;

        private int _busy;
        private List<AudioRoutingPreset> _presets = new();

        private readonly object _soundGate = new();
        private CancellationTokenSource? _soundCts;

        public SetupWizardViewModel(
            AudioRoutingService routing,
            IAudioDeviceService audio,
            VoicemeeterSettingsService store,
            UiSettingsService ui,
            ILogger<SetupWizardViewModel>? logger = null,
            Action<Action>? runOnUi = null) : base(logger)
        {
            _routing = routing;
            _audio = audio;
            _store = store;
            _ui = ui;
            _runOnUi = runOnUi ?? (a => Dispatcher.UIThread.Post(a));
        }

        // ─────────────────── IOverlayViewModel ───────────────────

        public event Action? CloseRequested;

        /// <summary>A stray click must not throw away a half-done setup.</summary>
        public bool CloseOnBackdropClick => false;

        /// <summary>The load started by the last <see cref="OnShown"/> (tests await it).</summary>
        internal Task LoadTask { get; private set; } = Task.CompletedTask;

        public void OnShown()
        {
            if (IsDisposed) return;
            StopTestSound();
            CurrentStep = 1;
            VoicemeeterMessage = "";
            RoutingResultText = "";
            RoutingSucceeded = false;
            CheckRows = new ObservableCollection<WizardCheckRow>();
            DiscordConfirmed = false;
            TestStatusText = "";
            TestHeard = false;
            TestNotHeard = false;
            ProblemRows = new ObservableCollection<WizardCheckRow>();
            LoadTask = ReloadAsync();
        }

        public void OnClosed() => StopTestSound();

        // ─────────────────── State ───────────────────

        [ObservableProperty] private int currentStep = 1;
        [ObservableProperty] private bool isBusy;

        // Step 1 — Voicemeeter
        [ObservableProperty] private bool voicemeeterInstalled;
        [ObservableProperty] private bool voicemeeterRunning;
        [ObservableProperty] private string voicemeeterMessage = "";

        // Step 2 — devices
        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> headphoneDevices = new();
        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> microphoneDevices = new();
        [ObservableProperty] private ObservableCollection<AudioDeviceInfo> guitarDevices = new();
        [ObservableProperty] private AudioDeviceInfo? selectedHeadphones;
        [ObservableProperty] private AudioDeviceInfo? selectedMicrophone;
        [ObservableProperty] private AudioDeviceInfo? selectedGuitar;
        [ObservableProperty] private bool shareHeadset = true;

        // Step 3 — routing
        [ObservableProperty] private ObservableCollection<WizardPresetChip> presetChips = new();
        [ObservableProperty] private string routingResultText = "";
        [ObservableProperty] private bool routingSucceeded;
        [ObservableProperty] private ObservableCollection<WizardCheckRow> checkRows = new();

        // Step 4 — Discord
        [ObservableProperty] private bool discordInstalled;
        [ObservableProperty] private bool discordConfirmed;

        // Step 5 — test
        [ObservableProperty] private bool isPlayingTest;
        [ObservableProperty] private string testStatusText = "";
        [ObservableProperty] private bool testHeard;
        [ObservableProperty] private bool testNotHeard;
        [ObservableProperty] private ObservableCollection<WizardCheckRow> problemRows = new();

        /// <summary>The routing preset that "Apply routing" will apply.</summary>
        public AudioRoutingPreset? SelectedPreset { get; private set; }

        // ─────────────────── Derived (view-facing) ───────────────────

        public string StepLabel => $"Step {CurrentStep} of {StepCount}";

        public IReadOnlyList<WizardStepDot> StepDots =>
            Enumerable.Range(1, StepCount).Select(i => new WizardStepDot(i == CurrentStep, i < CurrentStep)).ToList();

        public string Subtitle => CurrentStep switch
        {
            1 => "First, let's check Voicemeeter — the free mixer CenterHub uses to route your audio.",
            2 => "Tell CenterHub which headphones, microphone and instrument you use.",
            3 => "Pick a starting layout and let CenterHub wire everything up.",
            4 => "One quick setting in Discord, so it follows the routing.",
            _ => "Play a test sound — you should hear it in your headphones.",
        };

        public bool IsStep1 => CurrentStep == 1;
        public bool IsStep2 => CurrentStep == 2;
        public bool IsStep3 => CurrentStep == 3;
        public bool IsStep4 => CurrentStep == 4;
        public bool IsStep5 => CurrentStep == 5;
        public bool IsLastStep => CurrentStep == StepCount;
        public string NextLabel => IsLastStep ? "Finish" : "Next";

        public bool VoicemeeterMissing => !VoicemeeterInstalled;
        public bool VoicemeeterNeedsStart => VoicemeeterInstalled && !VoicemeeterRunning;
        public bool VoicemeeterReady => VoicemeeterInstalled && VoicemeeterRunning;
        public bool HasVoicemeeterMessage => VoicemeeterMessage.Length > 0;

        public bool HasGuitar => SelectedGuitar != null;
        public bool NeedsDeviceChoice => SelectedHeadphones == null || SelectedMicrophone == null;
        public bool HasRoutingResult => RoutingResultText.Length > 0;
        public bool HasCheckRows => CheckRows.Count > 0;
        public bool HasProblemRows => ProblemRows.Count > 0;
        public bool HasTestStatus => TestStatusText.Length > 0;
        public bool TestNothingWrongFound => TestNotHeard && ProblemRows.Count == 0;

        /// <summary>Whether the current step has what it needs to move on.</summary>
        private bool StepIsComplete => CurrentStep switch
        {
            1 => VoicemeeterInstalled,
            2 => SelectedHeadphones != null && SelectedMicrophone != null,
            _ => true,
        };

        public bool CanGoBack => CurrentStep > 1 && !IsBusy;
        public bool CanGoNext => !IsBusy && StepIsComplete;
        public bool CanSkip => !IsBusy;
        public bool NotBusy => !IsBusy;
        public bool CanStartVoicemeeter => !IsBusy && VoicemeeterNeedsStart;
        public bool CanApplyRouting => !IsBusy && VoicemeeterInstalled && SelectedPreset != null;
        public bool CanPlayTest => !IsBusy && !IsPlayingTest;

        private void NotifyDerived()
        {
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(StepDots));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(IsStep5));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(NextLabel));
            OnPropertyChanged(nameof(VoicemeeterMissing));
            OnPropertyChanged(nameof(VoicemeeterNeedsStart));
            OnPropertyChanged(nameof(VoicemeeterReady));
            OnPropertyChanged(nameof(HasVoicemeeterMessage));
            OnPropertyChanged(nameof(HasGuitar));
            OnPropertyChanged(nameof(NeedsDeviceChoice));
            OnPropertyChanged(nameof(HasRoutingResult));
            OnPropertyChanged(nameof(HasCheckRows));
            OnPropertyChanged(nameof(HasProblemRows));
            OnPropertyChanged(nameof(HasTestStatus));
            OnPropertyChanged(nameof(TestNothingWrongFound));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanSkip));
            OnPropertyChanged(nameof(NotBusy));
            OnPropertyChanged(nameof(CanStartVoicemeeter));
            OnPropertyChanged(nameof(CanApplyRouting));
            OnPropertyChanged(nameof(CanPlayTest));
        }

        partial void OnCurrentStepChanged(int value) => NotifyDerived();
        partial void OnIsBusyChanged(bool value) => NotifyDerived();
        partial void OnVoicemeeterInstalledChanged(bool value) => NotifyDerived();
        partial void OnVoicemeeterRunningChanged(bool value) => NotifyDerived();
        partial void OnVoicemeeterMessageChanged(string value) => NotifyDerived();
        partial void OnSelectedHeadphonesChanged(AudioDeviceInfo? value) => NotifyDerived();
        partial void OnSelectedMicrophoneChanged(AudioDeviceInfo? value) => NotifyDerived();
        partial void OnSelectedGuitarChanged(AudioDeviceInfo? value) => NotifyDerived();
        partial void OnRoutingResultTextChanged(string value) => NotifyDerived();
        partial void OnCheckRowsChanged(ObservableCollection<WizardCheckRow> value) => NotifyDerived();
        partial void OnProblemRowsChanged(ObservableCollection<WizardCheckRow> value) => NotifyDerived();
        partial void OnIsPlayingTestChanged(bool value) => NotifyDerived();
        partial void OnTestStatusTextChanged(string value) => NotifyDerived();
        partial void OnTestNotHeardChanged(bool value) => NotifyDerived();

        // ─────────────────── Threading helpers ───────────────────

        /// <summary>Run <paramref name="action"/> on the UI thread; completes once it has run.</summary>
        private Task OnUiAsync(Action action)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _runOnUi(() =>
                {
                    try
                    {
                        if (!IsDisposed) action();
                        tcs.TrySetResult();
                    }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });
            }
            catch (InvalidOperationException) { tcs.TrySetResult(); /* dispatcher gone */ }
            return tcs.Task;
        }

        private void BeginBusy()
        {
            if (Interlocked.Increment(ref _busy) == 1) IsBusy = true;
        }

        private Task EndBusyAsync() => OnUiAsync(() =>
        {
            if (Interlocked.Decrement(ref _busy) <= 0)
            {
                _busy = 0;
                IsBusy = false;
            }
        });

        // ─────────────────── Loading ───────────────────

        private sealed record Loaded(
            bool Installed,
            bool Running,
            List<AudioDeviceInfo> Playback,
            List<AudioDeviceInfo> Recording,
            VoicemeeterSettings Settings,
            AudioEndpointRef? DefaultPlayback,
            AudioEndpointRef? DefaultRecording,
            List<AudioRoutingPreset> Presets,
            string? ActivePresetId,
            bool DiscordInstalled);

        /// <summary>Re-read Voicemeeter status, devices, saved settings and presets.</summary>
        public async Task ReloadAsync()
        {
            if (IsDisposed) return;
            BeginBusy();
            try
            {
                var loaded = await Task.Run(LoadSnapshot).ConfigureAwait(false);
                await OnUiAsync(() => Apply(loaded)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Setup wizard: loading devices failed");
            }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        private Loaded LoadSnapshot()
        {
            bool installed = _routing.IsInstalled;
            bool running = installed && _routing.RefreshStatus();

            // Never offer Voicemeeter's own virtual endpoints: picking one as the headphones (or the
            // mic) routes Voicemeeter into itself — silence at best, a feedback loop at worst.
            var playback = _audio.GetPlaybackDevices().Where(d => !AudioRoutingService.IsVoicemeeterDevice(d.Name)).ToList();
            var recording = _audio.GetRecordingDevices().Where(d => !AudioRoutingService.IsVoicemeeterDevice(d.Name)).ToList();

            var settings = _store.Load().Settings;
            var presets = installed ? _routing.LoadPresets() : new List<AudioRoutingPreset>();
            string? active = installed ? _routing.ActivePresetId : null;

            bool discord = false;
            try
            {
                discord = Directory.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord"));
            }
            catch { /* unknown = not installed */ }

            return new Loaded(installed, running, playback, recording, settings,
                _audio.GetDefaultPlayback(), _audio.GetDefaultRecording(), presets, active, discord);
        }

        private void Apply(Loaded l)
        {
            VoicemeeterInstalled = l.Installed;
            VoicemeeterRunning = l.Running;

            HeadphoneDevices = new ObservableCollection<AudioDeviceInfo>(l.Playback);
            MicrophoneDevices = new ObservableCollection<AudioDeviceInfo>(l.Recording);
            GuitarDevices = new ObservableCollection<AudioDeviceInfo>(l.Recording);

            // Saved choice first; otherwise whatever Windows uses now (never a Voicemeeter endpoint —
            // those aren't in the lists). The guitar has no Windows default, so it stays empty.
            var s = l.Settings;
            SelectedHeadphones = Match(HeadphoneDevices, s.MonitorDeviceId, s.MonitorDeviceName)
                                 ?? Match(HeadphoneDevices, l.DefaultPlayback?.Id, l.DefaultPlayback?.Name);
            SelectedMicrophone = Match(MicrophoneDevices, s.MicrophoneDeviceId, s.MicrophoneDeviceName)
                                 ?? Match(MicrophoneDevices, l.DefaultRecording?.Id, l.DefaultRecording?.Name);
            SelectedGuitar = Match(GuitarDevices, s.GuitarDeviceId, s.GuitarDeviceName);
            ShareHeadset = s.ShareMonitorDevice;

            _presets = l.Presets;
            SelectedPreset = _presets.FirstOrDefault(p => p.Id == l.ActivePresetId) ?? _presets.FirstOrDefault();
            RebuildChips();

            DiscordInstalled = l.DiscordInstalled;
            NotifyDerived();
        }

        private static AudioDeviceInfo? Match(IEnumerable<AudioDeviceInfo> list, string? id, string? name)
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

        private void RebuildChips() =>
            PresetChips = new ObservableCollection<WizardPresetChip>(
                _presets.Select(p => new WizardPresetChip(p, ReferenceEquals(p, SelectedPreset))));

        // ─────────────────── Navigation ───────────────────

        [RelayCommand]
        private void Back()
        {
            if (IsDisposed || IsBusy || CurrentStep <= 1) return;
            CurrentStep--;
        }

        [RelayCommand]
        private async Task NextAsync()
        {
            if (IsDisposed || !CanGoNext) return;

            if (IsLastStep)
            {
                Complete();
                return;
            }

            BeginBusy();
            try
            {
                if (CurrentStep == 2)
                {
                    var headphones = SelectedHeadphones;
                    var microphone = SelectedMicrophone;
                    var guitar = SelectedGuitar;
                    var share = ShareHeadset;
                    await Task.Run(() => SaveDevices(headphones, microphone, guitar, share)).ConfigureAwait(false);
                }
                await OnUiAsync(() => { if (CurrentStep < StepCount) CurrentStep++; }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Setup wizard: leaving step {Step} failed", CurrentStep);
            }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        [RelayCommand]
        private void Skip()
        {
            if (IsDisposed || IsBusy) return;
            Complete();
        }

        [RelayCommand]
        private void FinishWithoutRouting()
        {
            if (IsDisposed || IsBusy) return;
            Complete();
        }

        private void Complete()
        {
            StopTestSound();
            try { _ui.Update(s => s.AudioSetupCompleted = true); }
            catch (Exception ex) { Logger?.LogError(ex, "Setup wizard: saving the completed flag failed"); }
            CloseRequested?.Invoke();
        }

        // ─────────────────── Step 1: Voicemeeter ───────────────────

        [RelayCommand]
        private void GetVoicemeeter() => OpenShell(VoicemeeterUrl);

        [RelayCommand]
        private async Task CheckAgainAsync()
        {
            if (IsDisposed || IsBusy) return;
            BeginBusy();
            try
            {
                var (installed, running) = await Task.Run(() =>
                {
                    bool inst = _routing.IsInstalled;
                    return (inst, inst && _routing.RefreshStatus());
                }).ConfigureAwait(false);
                await OnUiAsync(() =>
                {
                    VoicemeeterInstalled = installed;
                    VoicemeeterRunning = running;
                    VoicemeeterMessage = "";
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Logger?.LogWarning(ex, "Setup wizard: status check failed"); }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        [RelayCommand]
        private async Task StartVoicemeeterAsync()
        {
            if (IsDisposed || IsBusy || !VoicemeeterInstalled) return;
            BeginBusy();
            try
            {
                var (ok, error) = await Task.Run(async () =>
                {
                    bool started = await _routing.StartVoicemeeterAsync().ConfigureAwait(false);
                    return (started, started ? null : _routing.LastError);
                }).ConfigureAwait(false);
                await OnUiAsync(() =>
                {
                    VoicemeeterRunning = ok;
                    VoicemeeterMessage = ok
                        ? "Voicemeeter is running."
                        : (string.IsNullOrWhiteSpace(error) ? "Couldn't start Voicemeeter." : error);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Setup wizard: starting Voicemeeter failed");
                await OnUiAsync(() => VoicemeeterMessage = "Couldn't start Voicemeeter: " + ex.Message).ConfigureAwait(false);
            }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        // ─────────────────── Step 2: devices ───────────────────

        [RelayCommand]
        private void ClearGuitar() => SelectedGuitar = null;

        /// <summary>Write the chosen devices, keeping every other saved setting.</summary>
        private void SaveDevices(AudioDeviceInfo? headphones, AudioDeviceInfo? microphone, AudioDeviceInfo? guitar, bool share)
        {
            var settings = _store.Load().Settings;
            settings.MonitorDeviceId = headphones?.Id;
            settings.MonitorDeviceName = headphones?.Name;
            settings.MicrophoneDeviceId = microphone?.Id;
            settings.MicrophoneDeviceName = microphone?.Name;
            settings.GuitarDeviceId = guitar?.Id;
            settings.GuitarDeviceName = guitar?.Name;
            settings.ShareMonitorDevice = share;
            _store.SaveSettings(settings);
        }

        // ─────────────────── Step 3: routing ───────────────────

        [RelayCommand]
        private void SelectPreset(WizardPresetChip? chip)
        {
            if (IsDisposed || IsBusy || chip is null) return;
            SelectedPreset = chip.Preset;
            RebuildChips();
            NotifyDerived();
        }

        [RelayCommand]
        private async Task ApplyRoutingAsync()
        {
            var preset = SelectedPreset;
            if (IsDisposed || IsBusy || preset is null) return;
            BeginBusy();
            try
            {
                VoicemeeterModeResult result;
                List<AudioCheck> checks;
                bool running;
                try
                {
                    result = await Task.Run(() => _routing.ApplyPresetAsync(preset)).ConfigureAwait(false);
                    checks = await Task.Run(() => _routing.RunChecks()).ConfigureAwait(false);
                    running = await Task.Run(() => _routing.RefreshStatus()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Setup wizard: applying routing failed");
                    await OnUiAsync(() =>
                    {
                        RoutingSucceeded = false;
                        RoutingResultText = "Couldn't apply the routing: " + ex.Message;
                    }).ConfigureAwait(false);
                    return;
                }

                await OnUiAsync(() =>
                {
                    VoicemeeterRunning = running;
                    RoutingSucceeded = result.Success;
                    RoutingResultText = string.IsNullOrWhiteSpace(result.Message)
                        ? (result.Success ? "Routing applied." : "Couldn't apply the routing.")
                        : result.Message;
                    CheckRows = new ObservableCollection<WizardCheckRow>(checks.Select(c => new WizardCheckRow(c)));
                }).ConfigureAwait(false);
            }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        // ─────────────────── Step 4: Discord ───────────────────

        [RelayCommand]
        private void OpenDiscord() => OpenShell("discord://");

        // ─────────────────── Step 5: test sound ───────────────────

        [RelayCommand]
        private async Task PlayTestSoundAsync()
        {
            if (IsDisposed || IsBusy || IsPlayingTest) return;

            var cts = new CancellationTokenSource();
            lock (_soundGate) _soundCts = cts;

            IsPlayingTest = true;
            TestStatusText = "Playing…";
            TestHeard = false;
            TestNotHeard = false;
            ProblemRows = new ObservableCollection<WizardCheckRow>();

            string? error = null;
            try { error = await Task.Run(() => PlayChime(cts.Token)).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Setup wizard: test sound failed");
                error = "Couldn't play the test sound.";
            }
            finally
            {
                lock (_soundGate) { if (ReferenceEquals(_soundCts, cts)) _soundCts = null; }
                cts.Dispose();
            }

            await OnUiAsync(() =>
            {
                IsPlayingTest = false;
                TestStatusText = error ?? "Did you hear a soft two-note chime in your headphones?";
            }).ConfigureAwait(false);
        }

        [RelayCommand]
        private void HeardIt()
        {
            if (IsDisposed) return;
            TestHeard = true;
            TestNotHeard = false;
            ProblemRows = new ObservableCollection<WizardCheckRow>();
        }

        [RelayCommand]
        private async Task DidNotHearAsync()
        {
            if (IsDisposed || IsBusy) return;
            BeginBusy();
            try
            {
                var checks = await Task.Run(() => _routing.RunChecks()).ConfigureAwait(false);
                // Errors first, then warnings; the three most likely culprits.
                var worst = checks
                    .Where(c => c.Status != AudioCheckStatus.Ok)
                    .OrderBy(c => c.Status == AudioCheckStatus.Error ? 0 : 1)
                    .Take(3)
                    .Select(c => new WizardCheckRow(c))
                    .ToList();
                await OnUiAsync(() =>
                {
                    TestHeard = false;
                    ProblemRows = new ObservableCollection<WizardCheckRow>(worst);
                    TestNotHeard = true;
                    NotifyDerived();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { Logger?.LogWarning(ex, "Setup wizard: setup check failed"); }
            finally { await EndBusyAsync().ConfigureAwait(false); }
        }

        private void StopTestSound()
        {
            CancellationTokenSource? cts;
            lock (_soundGate) cts = _soundCts;
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { /* already finished */ }
        }

        /// <summary>
        /// Plays the chime on the Windows default playback device (shared WASAPI) and waits for it
        /// to finish or be cancelled. Returns an error message for the user, or null on success.
        /// Never throws: no device just means no sound.
        /// </summary>
        private string? PlayChime(CancellationToken ct)
        {
            WasapiOut? output = null;
            try
            {
                const int rate = 48000;
                var source = new ChimeSource(GenerateChime(rate), rate);
                var done = new ManualResetEventSlim(false);

                output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 100);
                output.PlaybackStopped += (_, _) => done.Set();
                output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(source));
                if (ct.IsCancellationRequested) return null;
                output.Play();
                done.Wait(2500, ct);
                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Setup wizard: no audio output for the test sound");
                return "Couldn't play the test sound — Windows has no usable audio output right now.";
            }
            finally
            {
                try { output?.Stop(); } catch { /* already stopped */ }
                try { output?.Dispose(); } catch { /* device gone */ }
            }
        }

        /// <summary>Peak level of the chime: -12 dBFS.</summary>
        public const float ChimePeak = 0.25118864f;

        /// <summary>
        /// A short, quiet, pleasant two-tone chime (E5 then A5, 0.6 s), mono, with a fade in/out and
        /// its peak at exactly <see cref="ChimePeak"/> (-12 dBFS). Pure: same input, same samples.
        /// </summary>
        public static float[] GenerateChime(int sampleRate = 48000)
        {
            int length = (int)Math.Round(0.6 * sampleRate);
            var samples = new float[length];
            var mix = new double[length];

            AddNote(mix, sampleRate, frequency: 659.25, startSeconds: 0.0, lengthSeconds: 0.36);
            AddNote(mix, sampleRate, frequency: 880.00, startSeconds: 0.20, lengthSeconds: 0.40);

            // Fade in over 10 ms, out over the last 120 ms (so it never clicks), then scale the peak.
            int fadeIn = Math.Max(1, (int)(0.010 * sampleRate));
            int fadeOut = Math.Max(1, (int)(0.120 * sampleRate));
            double peak = 0;
            for (int i = 0; i < length; i++)
            {
                double gain = 1.0;
                if (i < fadeIn) gain = (double)i / fadeIn;
                int fromEnd = length - 1 - i;
                if (fromEnd < fadeOut) gain = Math.Min(gain, (double)fromEnd / fadeOut);
                mix[i] *= gain;
                peak = Math.Max(peak, Math.Abs(mix[i]));
            }

            double scale = peak > 0 ? ChimePeak / peak : 0;
            for (int i = 0; i < length; i++) samples[i] = (float)(mix[i] * scale);
            return samples;
        }

        private static void AddNote(double[] mix, int sampleRate, double frequency, double startSeconds, double lengthSeconds)
        {
            int start = (int)(startSeconds * sampleRate);
            int count = Math.Min((int)(lengthSeconds * sampleRate), mix.Length - start);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / sampleRate;
                double attack = Math.Min(1.0, t / 0.008);
                double envelope = attack * Math.Exp(-t * 7.0);
                double tone = Math.Sin(2 * Math.PI * frequency * t)
                              + 0.2 * Math.Sin(2 * Math.PI * frequency * 2 * t);
                mix[start + i] += tone * envelope;
            }
        }

        /// <summary>Mono samples as a stereo float stream; ends when the chime does.</summary>
        private sealed class ChimeSource : ISampleProvider
        {
            private readonly float[] _mono;
            private int _position;

            public ChimeSource(float[] mono, int sampleRate)
            {
                _mono = mono;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = Math.Min(count / 2, _mono.Length - _position);
                for (int i = 0; i < frames; i++)
                {
                    float s = _mono[_position++];
                    buffer[offset++] = s;
                    buffer[offset++] = s;
                }
                return frames * 2;
            }
        }

        // ─────────────────── Shell ───────────────────

        private void OpenShell(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Logger?.LogInformation(ex, "Setup wizard: could not open {Target}", target); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopTestSound();
            base.Dispose(disposing);
        }
    }
}
