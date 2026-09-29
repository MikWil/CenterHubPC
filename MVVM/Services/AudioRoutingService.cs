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
        }

        public bool IsInstalled => _vm.IsInstalled;
        public bool IsRunning => _vm.Status == VoicemeeterStatus.Running;

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

        public async Task<VoicemeeterModeResult> ApplyPresetAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct = default)
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
                return VoicemeeterModeResult.Fail("Could not start Voicemeeter Banana.");
            }
            if (_vm.Kind == VoicemeeterKind.Standard)
                return VoicemeeterModeResult.Fail("This needs Voicemeeter Banana (or Potato).");

            // Hardware device assignments + monitor output (from the shared settings).
            var settings = state.Settings;
            if (!string.IsNullOrWhiteSpace(settings.MicrophoneDeviceName))
                _vm.SetHardwareInput(_vm.MicStripIndex, settings.MicrophoneDeviceName!);
            if (!string.IsNullOrWhiteSpace(settings.GuitarDeviceName))
                _vm.SetHardwareInput(_vm.GuitarStripIndex, settings.GuitarDeviceName!);
            if (!string.IsNullOrWhiteSpace(settings.MonitorDeviceName))
                _vm.SetMonitorDevice(settings.MonitorDeviceName!);

            // Feed the extra app slot: point the spare physical strip at the VB-Cable output.
            if (IsVbCableInstalled)
                _vm.SetHardwareInput(_vm.LineInStripIndex, VbCableCaptureName);

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

            // Switch Windows so Discord (on Default) follows.
            var playOk = await _audio.SetDefaultPlaybackByNameAsync(VaioInputName, communicationsToo: true).ConfigureAwait(false);
            var recOk = await _audio.SetDefaultRecordingByNameAsync(SendRecordingName, communicationsToo: true).ConfigureAwait(false);

            _store.SetActivePreset(preset.Id);

            if (!playOk || !recOk)
                return VoicemeeterModeResult.Fail(
                    "Routing applied, but the Voicemeeter virtual devices weren't found in Windows. Check the virtual driver is installed.");

            return VoicemeeterModeResult.Ok($"Applied '{preset.Name}'.");
        }

        /// <summary>
        /// Full re-sync: reconnect the Remote API, restart the audio engine, then
        /// reassign every device, route and Windows default from the given preset.
        /// Use when Voicemeeter has drifted after sitting idle.
        /// </summary>
        public async Task<VoicemeeterModeResult> ResyncAsync(AudioRoutingPreset preset, System.Threading.CancellationToken ct = default)
        {
            if (!_vm.IsInstalled)
                return VoicemeeterModeResult.Fail("Voicemeeter is not installed.");

            _vm.Reconnect();

            var ready = await _vm.EnsureRunningAsync(ct).ConfigureAwait(false);
            if (!ready) return VoicemeeterModeResult.Fail("Could not reach Voicemeeter Banana.");

            _vm.RestartAudioEngine();
            await Task.Delay(1500, ct).ConfigureAwait(false); // let the engine come back up

            var result = await ApplyPresetAsync(preset, ct).ConfigureAwait(false);
            return result.Success
                ? VoicemeeterModeResult.Ok("Re-synced — devices and routing reassigned.")
                : result;
        }

        // ─────────────────── Per-app output (handled in-app) ───────────────────

        public IReadOnlyList<AudioAppInfo> GetAudioApps() => _perApp.GetAudioApps();

        /// <summary>Point a running app's output at the Voicemeeter virtual input for the given slot.</summary>
        public bool AssignAppToSlot(int processId, AudioSourceKind slotKind)
        {
            var src = GetAvailableSources().FirstOrDefault(s => s.Kind == slotKind);
            if (src?.WindowsPlaybackName is not { } name) return false;

            var dev = _audio.GetPlaybackDevices()
                .FirstOrDefault(d => d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (dev is null)
            {
                _logger?.LogWarning("No render device found matching '{Name}' for slot {Slot}", name, slotKind);
                return false;
            }
            return _perApp.SetAppRenderDevice(processId, dev.Id);
        }

        /// <summary>Revert an app to the Windows default output.</summary>
        public bool ClearApp(int processId) => _perApp.ClearApp(processId);

        /// <summary>Fallback: open Windows' per-app volume page (kept for troubleshooting).</summary>
        public void OpenAppVolumeSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Could not open Windows app volume settings"); }
        }
    }
}
