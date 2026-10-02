using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    public readonly record struct VoicemeeterModeResult(bool Success, string Message)
    {
        public static VoicemeeterModeResult Ok(string message) => new(true, message);
        public static VoicemeeterModeResult Fail(string message) => new(false, message);
    }

    /// <summary>
    /// Orchestrates the "Guitar + Discord" mode: snapshot the current Windows audio,
    /// start &amp; configure Voicemeeter Banana, switch Windows defaults to the virtual
    /// devices, and restore everything exactly on disable (or after a crash).
    ///
    /// Routing goal:
    ///   Mic    -> B1 (to Discord), A1 optional (monitor mic)
    ///   Guitar -> B1 (to Discord), A1 optional (monitor guitar)
    ///   Desktop/Discord playback (VAIO) -> A1 only, NEVER B1 (no echo to Discord)
    /// Windows playback+comms -> Voicemeeter Input (VAIO); recording+comms -> Voicemeeter Out B1.
    /// </summary>
    public sealed class VoicemeeterModeService
    {
        private const string VaioInputName = "Voicemeeter Input"; // VAIO render endpoint

        private readonly IVoicemeeterService _voicemeeter;
        private readonly IAudioDeviceService _audio;
        private readonly VoicemeeterSettingsService _settingsService;
        private readonly ILogger<VoicemeeterModeService>? _logger;

        public VoicemeeterModeService(
            IVoicemeeterService voicemeeter,
            IAudioDeviceService audio,
            VoicemeeterSettingsService settingsService,
            ILogger<VoicemeeterModeService>? logger = null)
        {
            _voicemeeter = voicemeeter;
            _audio = audio;
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>True while a session is active (persisted, so it survives across VM instances).</summary>
        public bool IsActive => _settingsService.Load().Snapshot?.SessionActive == true;

        public async Task<VoicemeeterModeResult> EnableAsync(VoicemeeterSettings settings, CancellationToken ct = default)
        {
            if (!_voicemeeter.IsInstalled)
                return VoicemeeterModeResult.Fail("Voicemeeter is not installed.");

            // 1) Capture & persist the current audio state BEFORE touching anything (crash safety).
            var snapshot = _audio.CaptureSnapshot();
            snapshot.SessionActive = true;
            var state = _settingsService.Load();
            state.Settings = settings;
            state.Snapshot = snapshot;
            _settingsService.Save(state);
            _logger?.LogInformation("Captured audio snapshot: play={Play} rec={Rec}", snapshot.DefaultPlayback, snapshot.DefaultRecording);

            // 2) Start Banana and wait until the engine is ready.
            var ready = await _voicemeeter.EnsureRunningAsync(ct).ConfigureAwait(false);
            if (!ready)
            {
                _settingsService.SetSessionActive(false);
                return VoicemeeterModeResult.Fail(_voicemeeter.LastError ?? "Could not start Voicemeeter Banana.");
            }
            if (_voicemeeter.Kind == VoicemeeterKind.Standard)
            {
                _settingsService.SetSessionActive(false);
                return VoicemeeterModeResult.Fail("This mode needs Voicemeeter Banana (or Potato). Standard edition is running.");
            }

            // 3) Configure Banana routing.
            ConfigureRouting(settings);

            // 4) Switch Windows defaults so Discord (set to "Default") follows automatically.
            var sendBusName = settings.PreferredSendBus == VoicemeeterBBus.B2 ? "Voicemeeter Out B2" : "Voicemeeter Out B1";

            var playOk = await _audio.SetDefaultPlaybackByNameAsync(VaioInputName, communicationsToo: true).ConfigureAwait(false);
            var recOk = await _audio.SetDefaultRecordingByNameAsync(sendBusName, communicationsToo: true).ConfigureAwait(false);

            if (!playOk || !recOk)
            {
                _logger?.LogWarning("Voicemeeter virtual devices not fully switched (play={Play}, rec={Rec}).", playOk, recOk);
                return VoicemeeterModeResult.Fail(
                    "Banana is configured, but the Voicemeeter virtual audio devices weren't found in Windows. " +
                    "Check that Voicemeeter's virtual driver is installed.");
            }

            return VoicemeeterModeResult.Ok("Guitar + Discord mode enabled.");
        }

        private void ConfigureRouting(VoicemeeterSettings settings)
        {
            var sendBus = settings.PreferredSendBus == VoicemeeterBBus.B2 ? VoicemeeterBus.B2 : VoicemeeterBus.B1;
            var otherBus = sendBus == VoicemeeterBus.B1 ? VoicemeeterBus.B2 : VoicemeeterBus.B1;

            // ── Microphone strip ──
            if (!string.IsNullOrWhiteSpace(settings.MicrophoneDeviceName))
                _voicemeeter.SetHardwareInput(_voicemeeter.MicStripIndex, settings.MicrophoneDeviceName!);
            _voicemeeter.SetStripGain(_voicemeeter.MicStripIndex, settings.MicGainDb);
            _voicemeeter.SetStripMute(_voicemeeter.MicStripIndex, false);
            _voicemeeter.SetRoute(_voicemeeter.MicStripIndex, sendBus, true);       // -> Discord
            _voicemeeter.SetRoute(_voicemeeter.MicStripIndex, otherBus, false);
            _voicemeeter.SetMonitorRouting(_voicemeeter.MicStripIndex, settings.MonitorMicrophone); // A1

            // ── Guitar strip ──
            if (!string.IsNullOrWhiteSpace(settings.GuitarDeviceName))
                _voicemeeter.SetHardwareInput(_voicemeeter.GuitarStripIndex, settings.GuitarDeviceName!);
            _voicemeeter.SetStripGain(_voicemeeter.GuitarStripIndex, settings.GuitarGainDb);
            _voicemeeter.SetStripMute(_voicemeeter.GuitarStripIndex, false);
            _voicemeeter.SetRoute(_voicemeeter.GuitarStripIndex, sendBus, true);    // -> Discord
            _voicemeeter.SetRoute(_voicemeeter.GuitarStripIndex, otherBus, false);
            _voicemeeter.SetMonitorRouting(_voicemeeter.GuitarStripIndex, settings.MonitorGuitar); // A1

            // ── Desktop / Discord playback (VAIO virtual input) ──
            // Hear it on the monitor (A1) but NEVER send back to Discord (feedback guard).
            _voicemeeter.SetMonitorRouting(_voicemeeter.VaioStripIndex, true);      // A1 ON
            _voicemeeter.SetRoute(_voicemeeter.VaioStripIndex, VoicemeeterBus.B1, false);
            _voicemeeter.SetRoute(_voicemeeter.VaioStripIndex, VoicemeeterBus.B2, false);

            // ── Monitor output device (A1 hardware out) ──
            if (!string.IsNullOrWhiteSpace(settings.MonitorDeviceName))
                _voicemeeter.SetMonitorDevice(settings.MonitorDeviceName!, settings.ShareMonitorDevice);
        }

        public async Task<VoicemeeterModeResult> DisableAsync(CancellationToken ct = default)
        {
            var state = _settingsService.Load();
            var snapshot = state.Snapshot;

            if (snapshot is null || !snapshot.HasAnything())
            {
                _settingsService.SetSessionActive(false);
                return VoicemeeterModeResult.Fail("No saved audio configuration to restore.");
            }

            await _audio.RestoreSnapshotAsync(snapshot).ConfigureAwait(false);

            // Session done — mark inactive (keep the snapshot for reference / next diff).
            snapshot.SessionActive = false;
            state.Snapshot = snapshot;
            _settingsService.Save(state);

            // Leave Banana running; just release our Remote API connection.
            _voicemeeter.Disconnect();

            return VoicemeeterModeResult.Ok("Restored your previous audio devices.");
        }

        // ─────────────────── Crash recovery ───────────────────

        /// <summary>True if a previous session was left active (e.g. after a crash).</summary>
        public bool HasInterruptedSession(out AudioDeviceSnapshot? snapshot)
        {
            snapshot = _settingsService.Load().Snapshot;
            return snapshot?.SessionActive == true;
        }

        public bool AutoRestoreEnabled => _settingsService.Load().Settings.AutoRestoreOnStartup;

        /// <summary>Clear the interrupted-session flag WITHOUT changing any audio (user declined restore).</summary>
        public void ClearInterruptedSession() => _settingsService.SetSessionActive(false);

        /// <summary>Restore the interrupted session's audio (skips endpoints that no longer exist).</summary>
        public async Task<VoicemeeterModeResult> RestoreInterruptedAsync(CancellationToken ct = default)
        {
            return await DisableAsync(ct).ConfigureAwait(false);
        }
    }
}
