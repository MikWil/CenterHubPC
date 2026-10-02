using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

        // The Voicemeeter engine executables (Standard / Banana / Potato, 32- and 64-bit).
        // Deliberately NOT a "voicemeeter*" prefix match: that also hit VoicemeeterMacroButtons.
        private static readonly string[] EngineProcessNames =
        {
            "voicemeeter", "voicemeeter_x64",
            "voicemeeterpro", "voicemeeterpro_x64",
            "voicemeeter8", "voicemeeter8x64",
        };
        private const string BananaExe = "voicemeeterpro.exe";

        // Measured on a healthy Banana: it exits ~1 s after Command.Shutdown and its engine answers
        // ~0.4 s after launch. The limits below are generous multiples of that.
        private static readonly TimeSpan PoliteExitWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReadyWait = TimeSpan.FromSeconds(20);

        // After a crash or kill the Remote API keeps answering from its shared memory ("type 2,
        // running") — for as long as any client still holds it open — so its word alone is not
        // enough. Banana counts as running only if its process exists, and as ready only once that
        // process has been up this long: a freshly launched instance otherwise looks "ready"
        // through the previous instance's stale data and the settings sent to it are lost.
        private static readonly TimeSpan MinEngineUptime = TimeSpan.FromMilliseconds(1500);

        private readonly ILogger<VoicemeeterService>? _logger;
        private readonly VoicemeeterRemote _remote;
        private readonly object _gate = new();                       // guards every Remote DLL call + _connected
        private readonly SemaphoreSlim _lifecycle = new(1, 1);       // one start / restart at a time
        private int _lifecycleDepth;                                 // > 0 while a start / restart is running

        private bool _loaded;
        private bool _connected;
        private readonly System.Threading.Timer _keepAlive;

        private readonly object _processGate = new();
        private Process? _engineProcess;      // cached so the liveness check is a cheap HasExited
        private long _lastEngineScanTick;

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
                // While Banana is being started or restarted it is *expected* to be unreachable.
                // Dropping the session here raced the start-up wait and made restarts fail at random.
                if (Volatile.Read(ref _lifecycleDepth) > 0) return;

                lock (_gate)
                {
                    if (!_remote.IsAvailable || !_connected) return;
                    // A cheap dirty-poll keeps the engine in sync; if the type read
                    // fails the session is stale, so drop it and let the next op reconnect.
                    _remote.IsParametersDirty();
                    if (!IsEngineProcessAlive() || !_remote.TryGetType(out var t) || t <= 0)
                    {
                        // Banana went away (closed/crashed). Really log out — just clearing the
                        // flag left the client logged in, so the next Login() returned -2
                        // ("already logged in") and every start attempt failed.
                        try { _remote.Logout(); } catch { }
                        _connected = false;
                        Status = VoicemeeterStatus.Stopped;
                    }
                }
            }
            catch { /* keep-alive must never throw */ }
        }

        /// <summary>
        /// Log in, recovering from a stale session. Login returns 0 (running), 1 (installed,
        /// not running) or -2 (this client is already logged in — log out and retry).
        /// Caller must hold <see cref="_gate"/>.
        /// </summary>
        private bool LoginLocked()
        {
            int r = _remote.Login();
            if (r == -2)
            {
                try { _remote.Logout(); } catch { }
                r = _remote.Login();
            }
            _connected = r >= 0;
            if (_connected) EnsureKeepAliveRunning();
            else _logger?.LogWarning("Voicemeeter Remote login failed ({Code})", r);
            return _connected;
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
        public string? LastError { get; private set; }

        private void EnsureLoaded()
        {
            lock (_gate)
            {
                if (_loaded) return;
                _loaded = true;
                _remote.Load();
            }
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
                if (!_connected) LoginLocked();

                if (IsEngineProcessAlive() && _remote.TryGetType(out int type) && type > 0)
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
                return LoginLocked();
            }
        }

        public void Reconnect()
        {
            Disconnect();
            System.Threading.Thread.Sleep(150);
            Connect();
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

        public bool RestartAudioEngine()
        {
            // Command.Restart = 1 asks Voicemeeter to restart its audio engine.
            return Set("Command.Restart", 1f);
        }

        // ─────────────────── start / restart ───────────────────

        public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return Fail("Voicemeeter is not installed.");

            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref _lifecycleDepth);
            try
            {
                return await EnsureRunningCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _lifecycleDepth);
                _lifecycle.Release();
            }
        }

        public async Task<bool> RestartApplicationAsync(CancellationToken ct = default)
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return Fail("Voicemeeter is not installed.");

            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref _lifecycleDepth);
            try
            {
                _logger?.LogInformation("Restarting the Voicemeeter application…");

                if (!await CloseCoreAsync(ct).ConfigureAwait(false)) return false;

                // Start from a clean session, give the driver a moment to let go, relaunch.
                Disconnect();
                Status = VoicemeeterStatus.Stopped;
                await Task.Delay(500, ct).ConfigureAwait(false);

                return await EnsureRunningCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _lifecycleDepth);
                _lifecycle.Release();
            }
        }

        public async Task<bool> ShutdownAsync(CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref _lifecycleDepth);
            try
            {
                LastError = null;
                if (!await CloseCoreAsync(ct).ConfigureAwait(false)) return false;

                Disconnect();
                if (_remote.IsAvailable) Status = VoicemeeterStatus.Stopped;
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _lifecycleDepth);
                _lifecycle.Release();
            }
        }

        /// <summary>
        /// Closes every Voicemeeter engine process: asks first (a clean exit saves its settings),
        /// terminates it if it is still there. Caller holds the lifecycle lock.
        /// </summary>
        private async Task<bool> CloseCoreAsync(CancellationToken ct)
        {
            if (!AnyEngineRunning()) return true;

            bool asked = false;
            if (_remote.IsAvailable)
            {
                lock (_gate)
                {
                    asked = (_connected || LoginLocked()) && _remote.SetFloat("Command.Shutdown", 1f);
                }
            }
            if (asked) await WaitForEnginesToExitAsync(PoliteExitWait, ct).ConfigureAwait(false);

            // Still there (hung, or it never heard us): terminate it.
            if (AnyEngineRunning())
            {
                _logger?.LogWarning("Voicemeeter did not close on request — terminating it");
                bool denied = KillEngines();
                if (!await WaitForEnginesToExitAsync(KillWait, ct).ConfigureAwait(false))
                {
                    return Fail(denied
                        ? "Couldn't close Voicemeeter — it is running as administrator. Close it from its own menu, then try again."
                        : "Couldn't close Voicemeeter — it is not responding. End it in Task Manager, then try again.");
                }
            }
            return true;
        }

        /// <summary>Launch Banana if its engine isn't answering and wait until it does. Caller holds the lifecycle lock.</summary>
        private async Task<bool> EnsureRunningCoreAsync(CancellationToken ct)
        {
            LastError = null;

            if (!Connect())
                return Fail("Couldn't connect to Voicemeeter's remote control. Reinstalling Voicemeeter usually fixes this.");

            if (EngineReady()) return true;

            // Nothing answering. Launch it — unless a process is already there (still starting, or hung).
            bool launchedByUs = false;
            if (!AnyEngineRunning())
            {
                _logger?.LogInformation("Starting Voicemeeter Banana…");
                Launch();
                launchedByUs = true;
            }

            var clock = Stopwatch.StartNew();
            long lastRelogin = 0;
            bool retriedLaunch = false;

            while (clock.Elapsed < ReadyWait)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);

                if (EngineReady())
                {
                    // Flush the initial "dirty" burst so the settings that follow stick.
                    for (int j = 0; j < 5; j++)
                    {
                        lock (_gate) _remote.IsParametersDirty();
                        await Task.Delay(60, ct).ConfigureAwait(false);
                    }
                    _logger?.LogInformation("Voicemeeter {Kind} ready after {Ms} ms", Kind, clock.ElapsedMilliseconds);
                    return true;
                }

                // The process never showed up (or died straight away): start the exe ourselves, once.
                if (launchedByUs && !retriedLaunch && clock.ElapsedMilliseconds > 3000 && !AnyEngineRunning())
                {
                    retriedLaunch = true;
                    _logger?.LogWarning("Voicemeeter did not start — launching the executable directly");
                    LaunchExeDirectly();
                }

                // A session normally attaches to the new instance by itself; if it hasn't after a
                // second, open a fresh one (cheap, and it also clears any stale "-2" state).
                if (clock.ElapsedMilliseconds - lastRelogin >= 1000)
                {
                    lastRelogin = clock.ElapsedMilliseconds;
                    lock (_gate)
                    {
                        if (_connected) { try { _remote.Logout(); } catch { } _connected = false; }
                        LoginLocked();
                    }
                }
            }

            Status = VoicemeeterStatus.Stopped;
            return Fail(AnyEngineRunning()
                ? "Voicemeeter is open but its audio engine isn't answering. Use Restart Voicemeeter."
                : "Voicemeeter didn't start. Try opening it from the Start menu.");
        }

        private bool EngineReady()
        {
            if (!IsEngineProcessAlive(out var uptime) || uptime < MinEngineUptime) return false;

            lock (_gate)
            {
                if (!_remote.TryGetType(out int type) || type <= 0) return false;
                Status = VoicemeeterStatus.Running;
                Kind = (VoicemeeterKind)type;
                return true;
            }
        }

        private bool IsEngineProcessAlive() => IsEngineProcessAlive(out _);

        /// <summary>True when a Voicemeeter engine process exists; <paramref name="uptime"/> is how long it has been up.</summary>
        private bool IsEngineProcessAlive(out TimeSpan uptime)
        {
            uptime = TimeSpan.Zero;
            lock (_processGate)
            {
                if (_engineProcess != null)
                {
                    bool exited;
                    try { exited = _engineProcess.HasExited; }
                    catch { exited = true; }   // handle no longer usable — look it up again

                    if (!exited)
                    {
                        uptime = UptimeOf(_engineProcess);
                        return true;
                    }
                    _engineProcess.Dispose();
                    _engineProcess = null;
                }

                // Scanning the process list is not free; while Banana is down do it at most ~3×/s.
                long now = Environment.TickCount64;
                if (now - _lastEngineScanTick < 300) return false;
                _lastEngineScanTick = now;

                var found = GetEngineProcesses();
                if (found.Count == 0) return false;

                _engineProcess = found[0];
                for (int i = 1; i < found.Count; i++) found[i].Dispose();
                uptime = UptimeOf(_engineProcess);
                return true;
            }
        }

        private static TimeSpan UptimeOf(Process process)
        {
            try { return DateTime.Now - process.StartTime; }
            catch { return TimeSpan.MaxValue; }   // elevated process: start time unreadable — don't wait on it
        }

        private bool Fail(string reason)
        {
            LastError = reason;
            _logger?.LogWarning("Voicemeeter: {Reason}", reason);
            return false;
        }

        private void Launch()
        {
            int rc;
            lock (_gate) rc = _remote.RunVoicemeeter(VoicemeeterRemote.TYPE_BANANA);
            if (rc == 0) return;

            _logger?.LogWarning("RunVoicemeeter failed ({Code}) — launching the executable directly", rc);
            LaunchExeDirectly();
        }

        private void LaunchExeDirectly()
        {
            try
            {
                var dir = Path.GetDirectoryName(_remote.DllPath);
                if (string.IsNullOrEmpty(dir)) return;
                var exe = Path.Combine(dir, BananaExe);
                if (!File.Exists(exe)) { _logger?.LogWarning("{Exe} not found", exe); return; }
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir })?.Dispose();
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Could not launch Voicemeeter directly"); }
        }

        private static List<Process> GetEngineProcesses()
        {
            var list = new List<Process>();
            foreach (var name in EngineProcessNames)
            {
                try { list.AddRange(Process.GetProcessesByName(name)); }
                catch { /* process list unavailable — treat as none */ }
            }
            return list;
        }

        private static bool AnyEngineRunning()
        {
            var processes = GetEngineProcesses();
            foreach (var p in processes) p.Dispose();
            return processes.Count > 0;
        }

        private static async Task<bool> WaitForEnginesToExitAsync(TimeSpan limit, CancellationToken ct)
        {
            var clock = Stopwatch.StartNew();
            while (AnyEngineRunning())
            {
                if (clock.Elapsed >= limit) return false;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>Terminates every engine process. Returns true when one refused with "access denied".</summary>
        private bool KillEngines()
        {
            bool denied = false;
            foreach (var p in GetEngineProcesses())
            {
                try { p.Kill(); }
                catch (Win32Exception ex)
                {
                    // 5 = ERROR_ACCESS_DENIED: Voicemeeter runs elevated and CenterHub does not.
                    if (ex.NativeErrorCode == 5) denied = true;
                    _logger?.LogWarning(ex, "Could not terminate {Proc}", p.ProcessName);
                }
                catch (InvalidOperationException) { /* already gone */ }
                catch (Exception ex) { _logger?.LogWarning(ex, "Could not terminate {Proc}", p.ProcessName); }
                finally { p.Dispose(); }
            }
            return denied;
        }

        public void OpenUi()
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return;
            Connect();
            // RunVoicemeeter shows/raises the Banana window; harmless if already open.
            lock (_gate) _remote.RunVoicemeeter(VoicemeeterRemote.TYPE_BANANA);
        }

        // ─────────────────── strongly-typed parameters ───────────────────

        public bool SetHardwareInput(int stripIndex, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            // WDM (WASAPI) is Voicemeeter's low-latency driver kind for hardware I/O.
            return Set($"Strip[{stripIndex}].device.wdm", deviceName);
        }

        public bool SetMonitorDevice(string deviceName, bool shared)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;

            // Measured: Banana opens a WDM output in EXCLUSIVE mode — while it holds the headphones
            // no other app can play to them directly (AUDCLNT_E_DEVICE_IN_USE). Opened through MME
            // the device stays shared, at the cost of a longer monitoring delay.
            if (!shared) return Set($"Bus[{MonitorBusIndex}].device.wdm", deviceName);

            // MME device names are cut to 31 characters by Windows; Banana lists them that way.
            const int mmeNameLength = 31;
            var mmeName = deviceName.Length > mmeNameLength ? deviceName[..mmeNameLength] : deviceName;
            return Set($"Bus[{MonitorBusIndex}].device.mme", mmeName);
        }

        public string? GetMonitorDeviceName() => GetString($"Bus[{MonitorBusIndex}].device.name");

        public string? GetHardwareInputName(int stripIndex) => GetString($"Strip[{stripIndex}].device.name");

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
            lock (_gate)
            {
                if (!ConnectedForIoLocked()) return false;
                var ok = _remote.SetFloat(param, value);
                if (!ok) _logger?.LogWarning("SetFloat failed: {Param}={Value}", param, value);
                return ok;
            }
        }

        private bool Set(string param, string value)
        {
            lock (_gate)
            {
                if (!ConnectedForIoLocked()) return false;
                var ok = _remote.SetString(param, value);
                if (!ok) _logger?.LogWarning("SetString failed: {Param}='{Value}'", param, value);
                return ok;
            }
        }

        /// <summary>Reads a string parameter; null when Voicemeeter isn't running or the read fails.</summary>
        private string? GetString(string param)
        {
            lock (_gate)
            {
                if (!ConnectedForIoLocked()) return null;
                if (!IsEngineProcessAlive() || !_remote.TryGetType(out int type) || type <= 0) return null;
                // Parameters are only refreshed on the client when the dirty flag is polled.
                _remote.IsParametersDirty();
                return _remote.GetString(param);
            }
        }

        private bool ConnectedForIoLocked()
        {
            EnsureLoaded();
            if (!_remote.IsAvailable) return false;
            // With no engine process a write "succeeds" into shared memory nobody reads.
            if (!IsEngineProcessAlive()) return false;
            return _connected || LoginLocked();
        }

        public void Dispose()
        {
            try { _keepAlive.Dispose(); } catch { }
            Disconnect();
            lock (_gate) _remote.Dispose();
            lock (_processGate) { _engineProcess?.Dispose(); _engineProcess = null; }
            _lifecycle.Dispose();
        }
    }
}
