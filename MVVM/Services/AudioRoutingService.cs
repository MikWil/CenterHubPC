using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Engine behind the Sound tab's flow board. Turns a <see cref="AudioRoutingPreset"/>
    /// (per-source "you hear" / "others hear" toggles) into Voicemeeter Banana routing
    /// plus the Windows default-device switch, and stores presets.
    ///
    /// Sources map to Banana strips: Mic=0, Guitar=1, Line-in=2, and the virtual app
    /// inputs VAIO=3 / AUX=4 / VAIO3=5 (VAIO3 is Potato only). "You hear" = the A1 bus;
    /// "Others hear" = the B1 bus (the Discord send).
    /// </summary>
    public sealed class AudioRoutingService
    {
        private const string VaioInputName = "Voicemeeter Input";       // Windows render → VAIO strip
        private const string AuxInputName = "Voicemeeter Aux Input";    // Windows render → AUX strip
        private const string Vaio3InputName = "Voicemeeter VAIO3 Input";// Windows render → VAIO3 strip (Potato)
        private const string SendRecordingName = "Voicemeeter Out B1";  // Windows recording ← B1 bus

        private readonly IVoicemeeterService _vm;
        private readonly IAudioDeviceService _audio;
        private readonly VoicemeeterSettingsService _store;
        private readonly PerAppAudioService _perApp;
        private readonly ILogger<AudioRoutingService>? _logger;

        public AudioRoutingService(
            IVoicemeeterService vm,
            IAudioDeviceService audio,
            VoicemeeterSettingsService store,
            PerAppAudioService perApp,
            ILogger<AudioRoutingService>? logger = null)
        {
            _vm = vm;
            _audio = audio;
            _store = store;
            _perApp = perApp;
            _logger = logger;

            // If Windows' main output ever lands on an app slot, put it back (see KeepDesktopOutputAsync).
            _audio.DefaultPlaybackChanged += OnDefaultPlaybackChanged;
        }

        // ─────────────────── Windows' main output must never be an app slot ───────────────────

        /// <summary>
        /// Raised (background thread) after CenterHub moved Windows' main output off an app slot.
        /// The argument names the slot it was on, e.g. "Voicemeeter AUX Input".
        /// </summary>
        public event Action<string>? DesktopOutputRestored;

        /// <summary>
        /// A slot input (AUX, VAIO3, VB-Cable) carries only the apps the user sent there, and may
        /// be routed to "Others". If Windows' MAIN output points at one, every app on the PC plays
        /// into that slot — a browser tab ends up in Discord without anyone having assigned it.
        /// </summary>
        public static bool IsSlotInput(string? deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            if (deviceName.IndexOf(VbCableRenderName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return IsVoicemeeterDevice(deviceName)
                && !deviceName.StartsWith(VaioInputName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// While Banana is routing, make sure Windows' main output is "Voicemeeter Input" (the
        /// Desktop &amp; Discord row, which never reaches Others) and not an app slot. Returns true
        /// when it had to be moved. Leaves a real device (headset, direct mode) alone.
        /// </summary>
        public async Task<bool> KeepDesktopOutputAsync(System.Threading.CancellationToken ct = default)
        {
            await _applyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_vm.IsInstalled || _vm.RefreshStatus() != VoicemeeterStatus.Running) return false;

                var stray = _audio.GetDefaultPlayback()?.Name;
                if (!IsSlotInput(stray)) return false;

                // Move the communications default too only if it has strayed the same way.
                bool commsToo = IsSlotInput(_audio.GetDefaultCommunicationsPlayback()?.Name);
                if (!await _audio.SetDefaultPlaybackByNameAsync(VaioInputName, commsToo).ConfigureAwait(false))
                {
                    _logger?.LogWarning("Windows' main output is the app slot '{Slot}' and could not be moved back", stray);
                    return false;
                }

                _logger?.LogWarning("Windows' main output was the app slot '{Slot}' — moved back to {Desktop}", stray, VaioInputName);
                try { DesktopOutputRestored?.Invoke(stray!); } catch { /* a listener's problem */ }
                return true;
            }
            finally { _applyGate.Release(); }
        }

        private int _defaultCheckQueued;

        private void OnDefaultPlaybackChanged()
        {
            // Collapse a burst of notifications (Windows sends one per role) into one check.
            if (System.Threading.Interlocked.Exchange(ref _defaultCheckQueued, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    System.Threading.Interlocked.Exchange(ref _defaultCheckQueued, 0);
                    await KeepDesktopOutputAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Threading.Interlocked.Exchange(ref _defaultCheckQueued, 0);
                    _logger?.LogWarning(ex, "Checking Windows' main output failed");
                }
            });
        }

        public bool IsInstalled => _vm.IsInstalled;
        public bool IsRunning => _vm.Status == VoicemeeterStatus.Running;

        /// <summary>
        /// How long Banana gets to (re)open a device after it is assigned, and the pause between
        /// retries while Windows' device list settles after a restart. Tests set it to zero.
        /// </summary>
        internal TimeSpan DeviceSettleDelay { get; set; } = TimeSpan.FromMilliseconds(700);

        /// <summary>Re-detect Voicemeeter install/running state; returns true when running.</summary>
        public bool RefreshStatus() => _vm.RefreshStatus() == VoicemeeterStatus.Running;
        public string? ActivePresetId => _store.Load().ActivePresetId;

        /// <summary>Push a single source's routing to Voicemeeter immediately (live board edits). No-op if not running.</summary>
        public void LiveSet(AudioSourceRoute route)
        {
            if (!_vm.IsInstalled) return;
            if (!_vm.IsConnected && !_vm.Connect()) return;
            var strip = StripFor(route.Kind);
            if (strip < 0) return;
            _vm.SetStripMute(strip, route.Muted);
            _vm.SetStripGain(strip, route.GainDb);
            _vm.SetRoute(strip, VoicemeeterBus.A1, route.ToYou);
            _vm.SetRoute(strip, VoicemeeterBus.B1, route.ToOthers && CanSendToOthers(route.Kind));
            _vm.SetRoute(strip, VoicemeeterBus.B2, false);
        }

        // ─────────────────── Sources ───────────────────

        /// <summary>The routable sources for the running edition (Banana = 2 app slots, Potato = 3).</summary>
        public IReadOnlyList<AudioSource> GetAvailableSources()
        {
            var kind = _vm.Kind == VoicemeeterKind.Unknown ? VoicemeeterKind.Banana : _vm.Kind;

            var list = new List<AudioSource>
            {
                new() { Kind = AudioSourceKind.Microphone, StripIndex = _vm.MicStripIndex, DefaultName = "Microphone", Icon = "ti-microphone" },
                new() { Kind = AudioSourceKind.Guitar,     StripIndex = _vm.GuitarStripIndex, DefaultName = "Guitar (Katana)", Icon = "ti-guitar-pick" },
                // The Windows-default sink: desktop sounds + Discord voices + any app not
                // peeled to a slot. Never routed to "Others" (would echo Discord back).
                new() { Kind = AudioSourceKind.AppVaio, StripIndex = _vm.VaioStripIndex, DefaultName = "Desktop & Discord", IsVirtualApp = false, CanSendToOthers = false, Icon = "ti-device-desktop" },
            };

            if (kind is VoicemeeterKind.Banana or VoicemeeterKind.Potato)
                list.Add(new() { Kind = AudioSourceKind.AppAux, StripIndex = _vm.AuxStripIndex, DefaultName = "Application slot 1", IsVirtualApp = true, WindowsPlaybackName = AuxInputName, Icon = "ti-app-window" });

            if (kind is VoicemeeterKind.Potato)
                list.Add(new() { Kind = AudioSourceKind.AppVaio3, StripIndex = _vm.Vaio3StripIndex, DefaultName = "Application slot 2", IsVirtualApp = true, WindowsPlaybackName = Vaio3InputName, Icon = "ti-app-window" });

            // Extra app slot via VB-Audio Virtual Cable on a spare physical strip (Banana has no 3rd virtual input).
            if (IsVbCableInstalled)
            {
                var n = kind is VoicemeeterKind.Potato ? 3 : 2;
                list.Add(new() { Kind = AudioSourceKind.AppCable, StripIndex = _vm.LineInStripIndex, DefaultName = $"Application slot {n}", IsVirtualApp = true, WindowsPlaybackName = VbCableRenderName, Icon = "ti-app-window" });
            }

            return list;
        }

        /// <summary>The Desktop/Discord sink must never reach B1 (would echo Discord back to Discord).</summary>
        private static bool CanSendToOthers(AudioSourceKind kind) => kind != AudioSourceKind.AppVaio;

        private int StripFor(AudioSourceKind kind) => kind switch
        {
            AudioSourceKind.Microphone => _vm.MicStripIndex,
            AudioSourceKind.Guitar => _vm.GuitarStripIndex,
            AudioSourceKind.LineIn => _vm.LineInStripIndex,
            AudioSourceKind.AppVaio => _vm.VaioStripIndex,
            AudioSourceKind.AppAux => _vm.AuxStripIndex,
            AudioSourceKind.AppVaio3 => _vm.Vaio3StripIndex,
            AudioSourceKind.AppCable => _vm.LineInStripIndex, // physical strip fed by VB-Cable
            _ => -1
        };

        // VB-Audio Virtual Cable endpoints (present only when the free driver is installed).
        private const string VbCableRenderName = "CABLE Input";    // apps target this playback device
        private const string VbCableCaptureName = "CABLE Output";  // Voicemeeter strip reads from this

        /// <summary>True when the VB-Audio Virtual Cable driver is installed (enables an extra app slot).</summary>
        public bool IsVbCableInstalled =>
            _audio.GetPlaybackDevices().Any(d => d.Name.IndexOf(VbCableRenderName, StringComparison.OrdinalIgnoreCase) >= 0) &&
            _audio.GetRecordingDevices().Any(d => d.Name.IndexOf(VbCableCaptureName, StringComparison.OrdinalIgnoreCase) >= 0);

        // ─────────────────── Presets ───────────────────

        public List<AudioRoutingPreset> LoadPresets()
        {
            var presets = _store.Load().Presets;
            if (presets == null || presets.Count == 0)
            {
                presets = BuildDefaultPresets();
                _store.SavePresets(presets, presets.First().Id);
            }
            return presets;
        }

        public void SavePresets(List<AudioRoutingPreset> presets, string? activeId) => _store.SavePresets(presets, activeId);

        public List<AudioRoutingPreset> BuildDefaultPresets()
        {
            AudioSourceRoute R(AudioSourceKind k, bool you, bool others) =>
                new() { Kind = k, ToYou = you, ToOthers = others, GainDb = 0f };

            return new List<AudioRoutingPreset>
            {
                new()
                {
                    Name = "Guitar + Discord", Icon = "ti-guitar-pick",
                    Routes =
                    {
                        R(AudioSourceKind.Microphone, false, true),
                        R(AudioSourceKind.Guitar,     true,  true),
                        R(AudioSourceKind.AppVaio,    true,  false),
                        R(AudioSourceKind.AppAux,     true,  true),
                        R(AudioSourceKind.AppCable,   true,  true),
                    }
                },
                new()
                {
                    Name = "Gaming", Icon = "ti-device-gamepad-2",
                    Routes =
                    {
                        R(AudioSourceKind.Microphone, false, true),
                        R(AudioSourceKind.Guitar,     false, false),
                        R(AudioSourceKind.AppVaio,    true,  false),
                        R(AudioSourceKind.AppAux,     true,  false),
                        R(AudioSourceKind.AppCable,   true,  false),
                    }
                },
                new()
                {
                    Name = "Chill", Icon = "ti-coffee",
                    Routes =
                    {
                        R(AudioSourceKind.Microphone, false, false),
                        R(AudioSourceKind.Guitar,     false, false),
                        R(AudioSourceKind.AppVaio,    true,  false),
                        R(AudioSourceKind.AppAux,     true,  false),
                        R(AudioSourceKind.AppCable,   true,  false),
                    }
                },
            };
        }

        // ─────────────────── Apply ───────────────────

        // One apply / re-sync / restart at a time. Two overlapping ones (hotkey + button, a double
        // click) fight over Banana and over Windows' default-device switch, which then never returns.
        private readonly System.Threading.SemaphoreSlim _applyGate = new(1, 1);

        private async Task<VoicemeeterModeResult> ExclusiveAsync(Func<Task<VoicemeeterModeResult>> work, System.Threading.CancellationToken ct)
        {
            await _applyGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await work().ConfigureAwait(false); }
            finally { _applyGate.Release(); }
        }

        public Task<VoicemeeterModeResult> ApplyPresetAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct = default)
            => ExclusiveAsync(() => ApplyPresetCoreAsync(preset, ct), ct);

        private async Task<VoicemeeterModeResult> ApplyPresetCoreAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct)
        {
            if (!_vm.IsInstalled)
                return VoicemeeterModeResult.Fail("Voicemeeter is not installed.");

            // Capture the pre-Voicemeeter audio state once per session (crash safety).
            var state = _store.Load();
            if (state.Snapshot is null || !state.Snapshot.SessionActive)
            {
                var snap = _audio.CaptureSnapshot();
                snap.SessionActive = true;
                state.Snapshot = snap;
                _store.Save(state);
            }

            var ready = await _vm.EnsureRunningAsync(ct).ConfigureAwait(false);
            if (!ready)
            {
                _store.SetSessionActive(false);
                return VoicemeeterModeResult.Fail(_vm.LastError ?? "Could not start Voicemeeter Banana.");
            }
            if (_vm.Kind == VoicemeeterKind.Standard)
                return VoicemeeterModeResult.Fail("This needs Voicemeeter Banana (or Potato).");

            // Hardware device assignments + monitor output (from the shared settings). Only assign
            // what Banana doesn't already have: every assignment makes it reopen the device, which
            // is an audible dropout on each preset switch.
            var settings = state.Settings;
            bool reassigned = false;
            reassigned |= EnsureInput(_vm.MicStripIndex, settings.MicrophoneDeviceName);
            reassigned |= EnsureInput(_vm.GuitarStripIndex, settings.GuitarDeviceName);
            reassigned |= EnsureMonitor(settings);

            // Feed the extra app slot: point the spare physical strip at the VB-Cable output.
            if (IsVbCableInstalled)
                reassigned |= EnsureInput(_vm.LineInStripIndex, VbCableCaptureName);

            // Per-source routing.
            foreach (var route in preset.Routes)
            {
                var strip = StripFor(route.Kind);
                if (strip < 0) continue;
                var others = route.ToOthers && CanSendToOthers(route.Kind);
                _vm.SetStripMute(strip, route.Muted);
                _vm.SetStripGain(strip, route.GainDb);
                _vm.SetRoute(strip, VoicemeeterBus.A1, route.ToYou);
                _vm.SetRoute(strip, VoicemeeterBus.B1, others);
                _vm.SetRoute(strip, VoicemeeterBus.B2, false);
            }

            // Switch Windows so Discord (on Default) follows. Right after a Banana restart Windows'
            // device list is still settling and a lookup can come back empty — retry before giving up.
            var playOk = await RetryAsync(() => _audio.SetDefaultPlaybackByNameAsync(VaioInputName, communicationsToo: true), ct).ConfigureAwait(false);
            var recOk = await RetryAsync(() => _audio.SetDefaultRecordingByNameAsync(SendRecordingName, communicationsToo: true), ct).ConfigureAwait(false);

            _store.SetActivePreset(preset.Id);

            // Assigning a device is only a request. Read back what Banana really opened on A1:
            // with no device there, nothing sent to "You" is audible however the board looks.
            if (reassigned) await Task.Delay(DeviceSettleDelay, ct).ConfigureAwait(false);
            var monitorProblem = await VerifyMonitorAsync(settings.MonitorDeviceName, settings.ShareMonitorDevice, ct).ConfigureAwait(false);

            if (!playOk || !recOk)
                return VoicemeeterModeResult.Fail(
                    "Routing applied, but the Voicemeeter virtual devices weren't found in Windows. Check the virtual driver is installed.");
            if (monitorProblem != null)
                return VoicemeeterModeResult.Fail(monitorProblem);

            return VoicemeeterModeResult.Ok($"Applied '{preset.Name}'.");
        }

        private bool EnsureInput(int strip, string? wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return false;
            if (SameDevice(_vm.GetHardwareInputName(strip), wanted)) return false;
            _vm.SetHardwareInput(strip, wanted);
            return true;
        }

        private bool EnsureMonitor(VoicemeeterSettings settings)
        {
            var wanted = settings.MonitorDeviceName;
            if (string.IsNullOrWhiteSpace(wanted)) return false;

            if (SameDevice(_vm.GetMonitorDeviceName(), wanted))
            {
                // Right device — but is it open the right way? Banana can't be asked which driver it
                // uses, so look at the effect: exclusive (WDM) locks the endpoint, shared (MME) doesn't.
                var locked = _audio.IsPlaybackDeviceLocked(new AudioEndpointRef { Id = settings.MonitorDeviceId, Name = wanted });
                if (locked is null || locked.Value != settings.ShareMonitorDevice) return false;
            }

            _vm.SetMonitorDevice(wanted, settings.ShareMonitorDevice);
            return true;
        }

        /// <summary>Returns null when A1 has the wanted device (or can't be read); otherwise what to tell the user.</summary>
        private async Task<string?> VerifyMonitorAsync(string? wanted, bool shared, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return null;

            const int attempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                var actual = _vm.GetMonitorDeviceName();
                if (actual is null || SameDevice(actual, wanted)) return null;

                if (attempt == attempts)
                {
                    _logger?.LogWarning("Banana A1 is '{Actual}', wanted '{Wanted}'", actual, wanted);
                    return actual.Length == 0
                        ? $"Routing applied, but Banana couldn't open your headphones \"{wanted}\" — you won't hear anything. Check they are on and not in use by another app, then apply again."
                        : $"Routing applied, but Banana is playing through \"{actual}\" instead of \"{wanted}\".";
                }

                _vm.SetMonitorDevice(wanted, shared);
                await Task.Delay(DeviceSettleDelay, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Voicemeeter may report a device name truncated, so a prefix match counts.</summary>
        private static bool SameDevice(string? actual, string wanted)
        {
            if (string.IsNullOrWhiteSpace(actual)) return false;
            actual = actual.Trim();
            wanted = wanted.Trim();
            return actual.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)
                || wanted.StartsWith(actual, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> RetryAsync(Func<Task<bool>> action, System.Threading.CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                if (await action().ConfigureAwait(false)) return true;
                if (attempt == 3) return false;
                await Task.Delay(DeviceSettleDelay, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Full re-sync: reconnect the Remote API, restart the audio engine, then
        /// reassign every device, route and Windows default from the given preset.
        /// Use when Voicemeeter has drifted after sitting idle.
        /// </summary>
        /// <summary>
        /// Fully restart the Voicemeeter application (recovers a hung/crazy Banana), then
        /// reassign every device, route and Windows default. Heavier than <see cref="ResyncAsync"/>.
        /// </summary>
        public Task<VoicemeeterModeResult> RestartVoicemeeterAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct = default)
            => ExclusiveAsync(async () =>
            {
                if (!_vm.IsInstalled)
                    return VoicemeeterModeResult.Fail("Voicemeeter is not installed.");

                var ok = await _vm.RestartApplicationAsync(ct).ConfigureAwait(false);
                if (!ok) return VoicemeeterModeResult.Fail(_vm.LastError ?? "Could not restart Voicemeeter.");

                var result = await ApplyPresetCoreAsync(preset, ct).ConfigureAwait(false);
                return result.Success
                    ? VoicemeeterModeResult.Ok("Voicemeeter restarted — routing reassigned.")
                    : result;
            }, ct);

        /// <summary>
        /// Direct mode: take Banana out of the picture. It holds the headphones in exclusive mode
        /// while it runs, so an app that talks to the headset itself (Teams at work) gets silence.
        /// This closes Banana and makes the configured headphones and microphone the Windows
        /// defaults. Applying any preset starts Banana again.
        /// </summary>
        public Task<VoicemeeterModeResult> BypassAsync(System.Threading.CancellationToken ct = default)
            => ExclusiveAsync(async () =>
            {
                var s = _store.Load().Settings;
                var headphones = new AudioEndpointRef { Id = s.MonitorDeviceId, Name = s.MonitorDeviceName };
                var microphone = new AudioEndpointRef { Id = s.MicrophoneDeviceId, Name = s.MicrophoneDeviceName };
                if (!headphones.HasValue)
                    return VoicemeeterModeResult.Fail("Choose your headphones in Setup first, so CenterHub knows what to switch to.");

                if (_vm.IsInstalled && !await _vm.ShutdownAsync(ct).ConfigureAwait(false))
                    return VoicemeeterModeResult.Fail(_vm.LastError ?? "Couldn't close Voicemeeter.");

                var playOk = await RetryAsync(() => _audio.SetDefaultPlaybackAsync(headphones, communicationsToo: true), ct).ConfigureAwait(false);
                var recOk = !microphone.HasValue
                    || await RetryAsync(() => _audio.SetDefaultRecordingAsync(microphone, communicationsToo: true), ct).ConfigureAwait(false);

                // Banana being closed is intended now: no "Voicemeeter isn't running" prompt at next launch.
                _store.SetSessionActive(false);

                if (!playOk)
                    return VoicemeeterModeResult.Fail($"Voicemeeter is closed, but \"{headphones.Name}\" couldn't be made the Windows output. Is it switched on?");
                if (!recOk)
                    return VoicemeeterModeResult.Fail($"Your headphones are the Windows output now, but \"{microphone.Name}\" couldn't be made the microphone.");

                return VoicemeeterModeResult.Ok("Direct mode — headset and mic go straight to Windows. Pick a preset to use Banana again.");
            }, ct);

        /// <summary>True when Banana is out of the picture: not running and Windows plays to a real device.</summary>
        public bool IsDirectMode()
        {
            if (_vm.IsInstalled && _vm.RefreshStatus() == VoicemeeterStatus.Running) return false;
            var play = _audio.GetDefaultPlayback()?.Name;
            return !string.IsNullOrEmpty(play) && !IsVoicemeeterDevice(play);
        }

        public Task<VoicemeeterModeResult> ResyncAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct = default)
            => ExclusiveAsync(async () =>
            {
                if (!_vm.IsInstalled)
                    return VoicemeeterModeResult.Fail("Voicemeeter is not installed.");

                _vm.Reconnect();

                var ready = await _vm.EnsureRunningAsync(ct).ConfigureAwait(false);
                if (!ready) return VoicemeeterModeResult.Fail(_vm.LastError ?? "Could not reach Voicemeeter Banana.");

                _vm.RestartAudioEngine();
                await Task.Delay(1500, ct).ConfigureAwait(false); // let the engine come back up

                var result = await ApplyPresetCoreAsync(preset, ct).ConfigureAwait(false);
                return result.Success
                    ? VoicemeeterModeResult.Ok("Re-synced — devices and routing reassigned.")
                    : result;
            }, ct);

        // ─────────────────── Setup health check ───────────────────

        /// <summary>
        /// Validate the whole audio chain (Voicemeeter install/engine, virtual driver,
        /// Windows defaults, configured devices, Discord) and return guidance for anything wrong.
        /// </summary>
        public List<AudioCheck> RunChecks()
        {
            var checks = new List<AudioCheck>();

            // 1) Voicemeeter installed?
            if (!_vm.IsInstalled)
            {
                checks.Add(new AudioCheck
                {
                    Title = "Voicemeeter Banana",
                    Status = AudioCheckStatus.Error,
                    Detail = "Not installed. The audio router needs Voicemeeter Banana (free).",
                    Fix = AudioCheckFix.InstallVoicemeeter, FixLabel = "Get Voicemeeter"
                });
                return checks; // nothing else is meaningful without it
            }

            // 2) Engine running + edition
            var status = _vm.RefreshStatus();
            if (status != VoicemeeterStatus.Running)
                checks.Add(new AudioCheck
                {
                    Title = "Voicemeeter engine",
                    Status = AudioCheckStatus.Warning,
                    Detail = "Banana isn't running. Start it (or apply a preset) to route audio.",
                    Fix = AudioCheckFix.StartBanana, FixLabel = "Start Banana"
                });
            else if (_vm.Kind == VoicemeeterKind.Standard)
                checks.Add(new AudioCheck
                {
                    Title = "Voicemeeter edition",
                    Status = AudioCheckStatus.Error,
                    Detail = "Standard edition is running — routing needs Banana or Potato.",
                });
            else
                checks.Add(new AudioCheck
                {
                    Title = "Voicemeeter engine",
                    Status = AudioCheckStatus.Ok,
                    Detail = $"{_vm.Kind} running. If audio is glitching or stuck, restart it.",
                    Fix = AudioCheckFix.RestartVoicemeeter, FixLabel = "Restart"
                });

            // 3) Virtual audio driver present in Windows
            var render = _audio.GetPlaybackDevices();
            var capture = _audio.GetRecordingDevices();
            bool hasVaio = render.Any(d => d.Name.IndexOf(VaioInputName, StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasB1 = capture.Any(d => d.Name.IndexOf(SendRecordingName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!hasVaio || !hasB1)
                checks.Add(new AudioCheck
                {
                    Title = "Virtual audio driver",
                    Status = AudioCheckStatus.Error,
                    Detail = "Voicemeeter's virtual devices (Input / Out B1) are missing. Reinstall Voicemeeter and reboot.",
                    Fix = AudioCheckFix.InstallVoicemeeter, FixLabel = "Get Voicemeeter"
                });
            else
                checks.Add(new AudioCheck
                {
                    Title = "Virtual audio devices",
                    Status = AudioCheckStatus.Ok,
                    Detail = "Voicemeeter Input and Out B1 are present."
                });

            // 4) Windows defaults routed through Voicemeeter
            var play = _audio.GetDefaultPlayback();
            bool playOk = play?.Name?.IndexOf(VaioInputName, StringComparison.OrdinalIgnoreCase) >= 0;
            var rec = _audio.GetDefaultRecording();
            bool recOk = rec?.Name?.IndexOf(SendRecordingName, StringComparison.OrdinalIgnoreCase) >= 0;
            if (playOk && recOk)
                checks.Add(new AudioCheck { Title = "Windows audio routing", Status = AudioCheckStatus.Ok, Detail = "Windows output and mic go through Voicemeeter." });
            else if (IsSlotInput(play?.Name))
                checks.Add(new AudioCheck
                {
                    Title = "Windows audio routing",
                    Status = AudioCheckStatus.Error,
                    Detail = $"Windows is playing EVERYTHING into an app slot (\"{play!.Name}\"). If that slot is sent to Others, they hear all your apps. Apply routing to move it back.",
                    Fix = AudioCheckFix.ReapplyRouting, FixLabel = "Apply routing"
                });
            else
                checks.Add(new AudioCheck
                {
                    Title = "Windows audio routing",
                    Status = AudioCheckStatus.Warning,
                    Detail = "Windows isn't routed through Voicemeeter yet. Apply a preset to set it up.",
                    Fix = AudioCheckFix.ReapplyRouting, FixLabel = "Apply routing"
                });

            // 5) Configured devices exist
            var s = _store.Load().Settings;
            CheckDevice(checks, "Microphone", s.MicrophoneDeviceName, capture);
            CheckDevice(checks, "Guitar (Katana)", s.GuitarDeviceName, capture);
            CheckDevice(checks, "Monitor output", s.MonitorDeviceName, render);

            // 5b) …and Banana really has the headphones open. This is the "everything looks right
            //     but I hear nothing" case: A1 empty, or pointed back at Voicemeeter itself.
            if (status == VoicemeeterStatus.Running && _vm.GetMonitorDeviceName() is { } a1)
            {
                bool configured = !string.IsNullOrWhiteSpace(s.MonitorDeviceName);
                var fix = configured ? AudioCheckFix.ReapplyRouting : AudioCheckFix.ConfigureDevices;
                var fixLabel = configured ? "Apply routing" : "Open Setup";

                if (a1.Length == 0)
                    checks.Add(new AudioCheck
                    {
                        Title = "Headphone output",
                        Status = AudioCheckStatus.Error,
                        Detail = "Banana has no output device, so nothing can be heard.",
                        Fix = fix, FixLabel = fixLabel
                    });
                else if (IsVoicemeeterDevice(a1))
                    checks.Add(new AudioCheck
                    {
                        Title = "Headphone output",
                        Status = AudioCheckStatus.Error,
                        Detail = $"Banana is sending its output back into itself (\"{a1}\") — you hear nothing and it can loop. Pick your real headphones in Setup.",
                        Fix = AudioCheckFix.ConfigureDevices, FixLabel = "Open Setup"
                    });
                else if (configured && !SameDevice(a1, s.MonitorDeviceName!))
                    checks.Add(new AudioCheck
                    {
                        Title = "Headphone output",
                        Status = AudioCheckStatus.Warning,
                        Detail = $"Banana is playing through \"{a1}\", not \"{s.MonitorDeviceName}\".",
                        Fix = fix, FixLabel = fixLabel
                    });
                else
                {
                    var locked = _audio.IsPlaybackDeviceLocked(new AudioEndpointRef { Id = s.MonitorDeviceId, Name = s.MonitorDeviceName ?? a1 });
                    checks.Add(new AudioCheck
                    {
                        Title = "Headphone output",
                        Status = AudioCheckStatus.Ok,
                        Detail = locked switch
                        {
                            true => $"Banana plays through {a1} and has it to itself — apps set to that device directly are silent. Turn on \"Other apps can use it too\" in Setup to share it.",
                            false => $"Banana plays through {a1}, shared with other apps.",
                            _ => $"Banana plays through {a1}.",
                        }
                    });
                }
            }

            // 6) Discord reminder (can't read Discord's settings, but nudge if it's open)
            bool discord = false;
            try { discord = System.Diagnostics.Process.GetProcessesByName("Discord").Length > 0; } catch { }
            if (discord)
                checks.Add(new AudioCheck
                {
                    Title = "Discord",
                    Status = AudioCheckStatus.Warning,
                    Detail = "Set Discord's Input AND Output to \"Default\" so it follows CenterHub's routing."
                });

            // 7) Optional extra slot
            checks.Add(new AudioCheck
            {
                Title = "Extra app slot",
                Status = AudioCheckStatus.Ok,
                Detail = IsVbCableInstalled
                    ? "VB-Cable installed — a second application slot is available."
                    : "Optional: install VB-Cable to add a second application slot.",
                Fix = IsVbCableInstalled ? AudioCheckFix.None : AudioCheckFix.InstallVbCable,
                FixLabel = IsVbCableInstalled ? null : "Get VB-Cable"
            });

            return checks;
        }

        /// <summary>
        /// True for Voicemeeter's own virtual endpoints. They must never be chosen as the mic,
        /// guitar or headphone device: that routes Voicemeeter into itself.
        /// </summary>
        public static bool IsVoicemeeterDevice(string? name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("Voicemeeter", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void CheckDevice(List<AudioCheck> checks, string label, string? name, IReadOnlyList<AudioDeviceInfo> devices)
        {
            if (string.IsNullOrWhiteSpace(name))
                checks.Add(new AudioCheck
                {
                    Title = label,
                    Status = AudioCheckStatus.Warning,
                    Detail = $"No {label.ToLowerInvariant()} chosen yet.",
                    Fix = AudioCheckFix.ConfigureDevices, FixLabel = "Open Setup"
                });
            else if (!devices.Any(d => d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                checks.Add(new AudioCheck
                {
                    Title = label,
                    Status = AudioCheckStatus.Warning,
                    Detail = $"\"{name}\" isn't connected right now.",
                    Fix = AudioCheckFix.ConfigureDevices, FixLabel = "Open Setup"
                });
            else
                checks.Add(new AudioCheck { Title = label, Status = AudioCheckStatus.Ok, Detail = name! });
        }

        // ─────────────────── Per-app output (handled in-app) ───────────────────

        public IReadOnlyList<AudioAppInfo> GetAudioApps() => _perApp.GetAudioApps();

        /// <summary>
        /// Point a running app's output at the Voicemeeter virtual input for the given slot.
        /// Re-resolves the app's PID by name first so a recycled/stale PID can't route the wrong app.
        /// </summary>
        public bool AssignAppToSlot(AudioAppInfo app, AudioSourceKind slotKind)
        {
            if (!CanAssignAppsInApp) return false;

            var src = GetAvailableSources().FirstOrDefault(s => s.Kind == slotKind);
            if (src?.WindowsPlaybackName is not { } name) return false;

            var dev = _audio.GetPlaybackDevices()
                .FirstOrDefault(d => d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (dev is null)
            {
                _logger?.LogWarning("No render device found matching '{Name}' for slot {Slot}", name, slotKind);
                return false;
            }

            var livePid = _perApp.ResolveLivePid(app.ProcessId, app.DisplayName);
            if (livePid is null)
            {
                _logger?.LogWarning("{App} has no live audio session to route", app.DisplayName);
                return false;
            }
            var mainBefore = _audio.GetDefaultPlayback();
            bool ok = _perApp.SetAppRenderDevice(livePid.Value, dev.Id);

            // Don't take the call's word for it. On Windows build 26200 it "succeeds" while moving
            // Windows' MAIN output to the slot — so every app on the PC plays into the slot and, if
            // the slot is sent to Others, into Discord. Detect that, undo it, and stop using the
            // call on this build.
            if (MainOutputMoved(mainBefore))
            {
                int build = WindowsBuild;
                _logger?.LogError("Per-app assignment moved Windows' main output on build {Build}; disabling it", build);
                _inAppAssignBroken = true;
                try { _store.SetInAppAssignBrokenOnBuild(build); } catch { /* still disabled for this session */ }

                var back = IsSlotInput(mainBefore!.Name) ? null : mainBefore;
                bool restored = back is not null
                    ? _audio.SetDefaultPlaybackAsync(back, communicationsToo: false).GetAwaiter().GetResult()
                    : _audio.SetDefaultPlaybackByNameAsync(VaioInputName, communicationsToo: false).GetAwaiter().GetResult();
                if (!restored) _logger?.LogWarning("Could not put Windows' main output back after the failed per-app assignment");
                return false;
            }

            return ok;
        }

        private bool? _inAppAssignBroken;

        /// <summary>
        /// False when moving an app from inside CenterHub is known not to work on this Windows build
        /// (it was caught moving Windows' main output instead). The app is then assigned in Windows.
        /// </summary>
        public bool CanAssignAppsInApp
        {
            get
            {
                _inAppAssignBroken ??= WindowsBuild >= FirstBuildWithMovedPolicyInterface
                                       || _store.Load().InAppAssignBrokenOnBuild == WindowsBuild;
                return !_inAppAssignBroken.Value;
            }
        }

        /// <summary>
        /// Measured on build 26200: the per-app call lands on a different method and moves Windows'
        /// main output. Later builds are assumed to have the same layout (not verified) — the safe
        /// assumption, since the Windows page always works and a wrong guess the other way leaks
        /// every app's audio into a slot. Earlier builds are still checked each time they are used.
        /// </summary>
        private const int FirstBuildWithMovedPolicyInterface = 26200;

        /// <summary>The running Windows build; tests set it.</summary>
        internal int WindowsBuild { get; set; } = Environment.OSVersion.Version.Build;

        /// <summary>Did Windows' main output change in the moments after the per-app call?</summary>
        private bool MainOutputMoved(AudioEndpointRef? before)
        {
            if (before is null) return false;
            for (int i = 0; i < 8; i++)
            {
                var now = _audio.GetDefaultPlayback();
                if (now is not null && !string.Equals(now.Id, before.Id, StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(now.Name, before.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (DeviceSettleDelay <= TimeSpan.Zero) break;   // tests: nothing to wait for
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }

        /// <summary>Revert an app to the Windows default output.</summary>
        public bool ClearApp(int processId) => CanAssignAppsInApp && _perApp.ClearApp(processId);

        /// <summary>Fallback: open Windows' per-app volume page (kept for troubleshooting).</summary>
        public void OpenAppVolumeSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Could not open Windows app volume settings"); }
        }
    }
}
