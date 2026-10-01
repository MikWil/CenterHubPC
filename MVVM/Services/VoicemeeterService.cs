using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    /// <inheritdoc cref="IVoicemeeterService"/>
    public sealed class VoicemeeterService : IVoicemeeterService
    {
        // ── Banana channel map ──
        // Physical input strips 0,1,2; virtual inputs 3 (Voicemeeter VAIO) and 4 (AUX).
        // Buses: A1=0, A2=1, A3=2, B1=3, B2=4.
        public int MicStripIndex => 0;
        public int GuitarStripIndex => 1;
        public int LineInStripIndex => 2;
        public int VaioStripIndex => 3;
        public int AuxStripIndex => 4;
        public int Vaio3StripIndex => 5;
        public int MonitorBusIndex => 0; // A1

        private readonly ILogger<VoicemeeterService>? _logger;
        private readonly VoicemeeterRemote _remote;
        private readonly object _gate = new();

        private bool _loaded;
        private bool _connected;
        private readonly System.Threading.Timer _keepAlive;

        public VoicemeeterService(ILogger<VoicemeeterService>? logger = null)
        {
            _logger = logger;
            _remote = new VoicemeeterRemote(logger);
            // The Remote API needs periodic polling or its parameter engine drifts
            // ("goes crazy" when the app sits idle). Poll IsParametersDirty ~1/s.
            _keepAlive = new System.Threading.Timer(_ => KeepAlive(), null,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        private void KeepAlive()
        {
            try
            {
                lock (_gate)
                {
                    if (!_remote.IsAvailable || !_connected) return;
                    // A cheap dirty-poll keeps the engine in sync; if the type read
                    // fails the session is stale, so drop it and let the next op reconnect.
                    _remote.IsParametersDirty();
                    if (!_remote.TryGetType(out var t) || t <= 0)
                        _connected = false;
                }
            }
            catch { /* keep-alive must never throw */ }
        }

        private void EnsureKeepAliveRunning() =>
            _keepAlive.Change(1000, 1000);

        public bool IsInstalled
        {
            get { EnsureLoaded(); return _remote.IsAvailable; }
        }

        public VoicemeeterStatus Status { get; private set; } = VoicemeeterStatus.NotInstalled;
        public VoicemeeterKind Kind { get; private set; } = VoicemeeterKind.Unknown;
        public bool IsConnected => _connected;

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _remote.Load();
        }

        public VoicemeeterStatus RefreshStatus()
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_remote.IsAvailable)
                {
                    Status = VoicemeeterStatus.NotInstalled;
                    Kind = VoicemeeterKind.Unknown;
                    return Status;
                }

                // Login registers the client. Return 0 = running, 1 = installed but not launched.
                if (!_connected)
                {
                    int r = _remote.Login();
                    _connected = r >= 0; // -2 == already logged in is also fine
                }

                if (_remote.TryGetType(out int type) && type > 0)
                {
                    Status = VoicemeeterStatus.Running;
                    Kind = (VoicemeeterKind)type;
                }
                else
                {
                    Status = VoicemeeterStatus.Stopped;
                    Kind = VoicemeeterKind.Unknown;
                }

                return Status;
            }
        }

        public bool Connect()
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_remote.IsAvailable) return false;
                if (_connected) return true;

                int r = _remote.Login();
                _connected = r >= 0;
                if (_connected) EnsureKeepAliveRunning();
                return _connected;
            }
        }

        public void Reconnect()
        {
            Disconnect();
            System.Threading.Thread.Sleep(150);
            Connect();
        }

        public bool RestartAudioEngine()
        {
            if (!EnsureConnectedForWrite()) return false;
            // Command.Restart = 1 asks Voicemeeter to restart its audio engine.
            return _remote.SetFloat("Command.Restart", 1f);
        }

        public async Task<bool> RestartApplicationAsync(CancellationToken ct = default)
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return false;

            _logger?.LogInformation("Restarting the Voicemeeter application…");

            // 1) Ask Voicemeeter to close itself (clean shutdown), then release our client.
            try
            {
                if (Connect()) _remote.SetFloat("Command.Shutdown", 1f);
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Command.Shutdown failed"); }
            Disconnect();

            await Task.Delay(1500, ct).ConfigureAwait(false);

            // 2) Force-kill anything still lingering (a hung Banana won't honour Shutdown).
            KillVoicemeeterProcesses();
            await Task.Delay(800, ct).ConfigureAwait(false);

            // 3) Relaunch Banana and wait until the engine is ready again.
            return await EnsureRunningAsync(ct).ConfigureAwait(false);
        }

        private void KillVoicemeeterProcesses()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    if (!p.ProcessName.StartsWith("voicemeeter", StringComparison.OrdinalIgnoreCase)) continue;
                    try { p.Kill(); p.WaitForExit(2000); }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Could not kill {Proc}", p.ProcessName); }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Enumerating Voicemeeter processes failed"); }
        }

        public void Disconnect()
        {
            lock (_gate)
            {
                if (_connected)
                {
                    try { _remote.Logout(); } catch (Exception ex) { _logger?.LogDebug(ex, "Voicemeeter logout failed"); }
                    _connected = false;
                }
            }
        }

        public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return false;

            if (!Connect()) return false;

            // Already running with a valid engine?
            if (_remote.TryGetType(out int type) && type > 0)
            {
                Status = VoicemeeterStatus.Running;
                Kind = (VoicemeeterKind)type;
                return true;
            }

            // Launch Banana specifically and wait for the engine to come up.
            _logger?.LogInformation("Starting Voicemeeter Banana…");
            _remote.RunVoicemeeter(VoicemeeterRemote.TYPE_BANANA);

            for (int i = 0; i < 50 && !ct.IsCancellationRequested; i++)   // ~10s
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                if (_remote.TryGetType(out type) && type > 0)
                {
                    Status = VoicemeeterStatus.Running;
                    Kind = (VoicemeeterKind)type;

                    // Flush the initial "dirty" burst so subsequent sets stick.
                    for (int j = 0; j < 5; j++) { _remote.IsParametersDirty(); await Task.Delay(60, ct).ConfigureAwait(false); }
                    _logger?.LogInformation("Voicemeeter {Kind} ready", Kind);
                    return true;
                }
            }

            _logger?.LogWarning("Voicemeeter did not become ready in time");
            return false;
        }

        public void OpenUi()
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return;
            Connect();
            // RunVoicemeeter shows/raises the Banana window; harmless if already open.
            _remote.RunVoicemeeter(VoicemeeterRemote.TYPE_BANANA);
        }

        // ─────────────────── strongly-typed parameters ───────────────────

        public bool SetHardwareInput(int stripIndex, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            // WDM is the standard shared-mode driver kind for hardware I/O.
            return Set($"Strip[{stripIndex}].device.wdm", deviceName);
        }

        public bool SetMonitorDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            return Set($"Bus[{MonitorBusIndex}].device.wdm", deviceName);
        }

        public bool SetStripGain(int stripIndex, float gainDb)
            => Set($"Strip[{stripIndex}].Gain", gainDb);

        public bool SetStripMute(int stripIndex, bool mute)
            => Set($"Strip[{stripIndex}].Mute", mute ? 1f : 0f);

        public bool SetRoute(int stripIndex, VoicemeeterBus bus, bool on)
            => Set($"Strip[{stripIndex}].{bus}", on ? 1f : 0f);

        public bool SetRouteToB1(int stripIndex, bool on) => SetRoute(stripIndex, VoicemeeterBus.B1, on);

        public bool SetMonitorRouting(int stripIndex, bool toA1) => SetRoute(stripIndex, VoicemeeterBus.A1, toA1);

        private bool Set(string param, float value)
        {
            if (!EnsureConnectedForWrite()) return false;
            var ok = _remote.SetFloat(param, value);
            if (!ok) _logger?.LogWarning("SetFloat failed: {Param}={Value}", param, value);
            return ok;
        }

        private bool Set(string param, string value)
        {
            if (!EnsureConnectedForWrite()) return false;
            var ok = _remote.SetString(param, value);
            if (!ok) _logger?.LogWarning("SetString failed: {Param}='{Value}'", param, value);
            return ok;
        }

        private bool EnsureConnectedForWrite()
        {
            if (!_remote.IsAvailable) return false;
            return _connected || Connect();
        }

        public void Dispose()
        {
            try { _keepAlive.Dispose(); } catch { }
            Disconnect();
            _remote.Dispose();
        }
    }
}
