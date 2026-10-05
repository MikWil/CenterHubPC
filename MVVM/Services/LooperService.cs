using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// The guitar looper as the UI sees it: captures the guitar from an audio input, places every captured
    /// sample at the engine frame that was audible when it was played (so a note on the beat lands on the
    /// beat), and drives <see cref="LooperEngine"/>, which is mixed into the drum machine's output. Without
    /// the drums it runs on the output's own clock (free mode: count-in, then a loop that cycles by its own
    /// length); it asks <see cref="MetronomeService"/> to open that output, it has none of its own.
    /// Commands and properties are for the UI thread; capture runs on NAudio's thread.
    /// </summary>
    public sealed class LooperService : IDisposable
    {
        private static readonly TimeSpan ChangedInterval = TimeSpan.FromMilliseconds(66);   // ~15 Hz

        private const float LevelDecay = 0.9f;          // per capture buffer (~10 ms)
        private const int CalibrationClicks = 8;
        private const double CalibrationBpm = 100;

        /// <summary>Everything one open capture needs; stale sessions' callbacks are ignored.</summary>
        private sealed class CaptureSession
        {
            public WasapiCapture Capture = null!;
            public MMDevice Device = null!;
            public string DeviceName = "";
            public int Channels;
            public int Bits;
            public bool IsFloat;
            public int SourceRate;
            public WdlResampler? Resampler;
            public int ResamplerDelay;                   // engine-rate frames the sinc filter delays the stream by
            public float[] Mono = new float[4096];
            public float[] Resampled = new float[4096];
            public long Total;                           // engine-rate samples produced so far
            public readonly CaptureClock Clock = new();
            public double[] ChannelEnergy = Array.Empty<double>();   // smoothed mean square per channel
            public bool[] ChannelActive = Array.Empty<bool>();
        }

        /// <summary>Raw capture with audible-frame stamps (no latency compensation) for calibration.</summary>
        private sealed class CalibrationCapture
        {
            private readonly object _gate = new();
            private readonly List<float> _samples = new();
            private long _first = -1;

            public void Append(long start, float[] data, int count)
            {
                lock (_gate)
                {
                    if (_first < 0) _first = start;
                    long expected = _first + _samples.Count;
                    int skip = 0;
                    if (start > expected) _samples.AddRange(new float[(int)Math.Min(start - expected, 44100)]);
                    else if (start < expected) skip = (int)Math.Min(expected - start, count);
                    for (int i = skip; i < count; i++) _samples.Add(data[i]);
                }
            }

            public (float[] Data, long FirstFrame) Snapshot()
            {
                lock (_gate) return (_samples.ToArray(), Math.Max(_first, 0));
            }
        }

        private readonly MetronomeService _metronome;
        private readonly IAudioDeviceService _audio;
        private readonly VoicemeeterSettingsService _voicemeeterSettings;
        private readonly IVoicemeeterService? _voicemeeter;
        private readonly ILogger<LooperService>? _logger;
        private readonly LooperEngine _looper;
        private readonly int _rate;

        // Free mode count-in: the clicks are scheduled on the engine's clock; recording starts at _countInEnd.
        private long _countInEnd;
        private int _countInBeats;
        private double _countInBeatFrames;
        private int _countInBars = 1;

        // What Banana's guitar strip uses, asked on the UI thread at most every few seconds.
        private static readonly TimeSpan BananaNameMaxAge = TimeSpan.FromSeconds(5);
        private string? _bananaName;
        private long _bananaNameAt;

        private CaptureSession? _session;
        private CalibrationCapture? _calibration;
        private bool _calibrating;
        private string? _inputDeviceId;
        private string? _nameCache;
        private bool _nameCacheValid;
        private string? _lastError;
        private float _level;
        private int _latencyMs = 60;
        private int _changedPending;
        private DispatcherTimer? _timer;
        private bool _disposed;

        public LooperService(MetronomeService metronome, IAudioDeviceService audio,
                             VoicemeeterSettingsService voicemeeterSettings, IVoicemeeterService? voicemeeter = null,
                             ILogger<LooperService>? logger = null)
        {
            _metronome = metronome ?? throw new ArgumentNullException(nameof(metronome));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _voicemeeterSettings = voicemeeterSettings ?? throw new ArgumentNullException(nameof(voicemeeterSettings));
            _voicemeeter = voicemeeter;
            _logger = logger;
            _rate = metronome.Engine.WaveFormat.SampleRate;
            _looper = new LooperEngine(_rate);
            _looper.Changed += OnLooperChanged;
            metronome.Engine.AttachLooper(_looper);
        }

        // ───────────────────────── state ─────────────────────────

        public LooperState State => _looper.State;

        /// <summary>Bars of a bar-synced loop, or of a free take recorded with a fixed length (0 when empty or free-length).</summary>
        public int LoopBars => _looper.LoopBars;

        /// <summary>True when the loop is not tied to the drum machine's bars (recorded without it, or trimmed).</summary>
        public bool IsFreeLoop => _looper.IsFreeLoop;

        /// <summary>Current (trimmed) loop length in seconds; 0 when empty.</summary>
        public double LoopSeconds => _looper.LoopSeconds;

        /// <summary>Untrimmed length of the take in seconds; 0 when empty.</summary>
        public double RecordedSeconds => _looper.RecordedSeconds;

        /// <summary>Milliseconds cut from the start of the loop.</summary>
        public double TrimStartMs => _looper.TrimStartMs;

        /// <summary>Milliseconds cut from the end of the loop.</summary>
        public double TrimEndMs => _looper.TrimEndMs;

        /// <summary>Cuts the loop (all layers, non-destructive, at once); at least 100 ms stay. Trimming a bar-synced loop frees it.</summary>
        public void SetTrim(double startMs, double endMs)
        {
            if (!_disposed) _looper.SetTrim(startMs, endMs);
        }

        public void ResetTrim()
        {
            if (!_disposed) _looper.ResetTrim();
        }

        /// <summary>Peak (0..1) per bucket over the untrimmed take, all layers mixed, after the auto-level gain; empty when there is no loop.</summary>
        public float[] GetWaveform(int buckets) => _looper.GetWaveform(buckets);

        /// <summary>True (default): the first take is raised to a healthy level when it ends.</summary>
        public bool AutoLevel
        {
            get => _looper.AutoLevel;
            set => _looper.AutoLevel = value;
        }

        /// <summary>The gain auto-level chose (1 when off or empty).</summary>
        public float LoopGain => _looper.LoopGain;

        /// <summary>Bars of count-in before a free take (drums not playing): 0, 1 or 2. Default 1.</summary>
        public int CountInBars
        {
            get => Volatile.Read(ref _countInBars);
            set => Volatile.Write(ref _countInBars, Math.Clamp(value, 0, 2));
        }

        /// <summary>Armed and the count-in clicks are running.</summary>
        public bool IsCountingIn => CountInBeatsLeft > 0;

        /// <summary>Count-in beats still to go (0 when not counting in).</summary>
        public int CountInBeatsLeft
        {
            get
            {
                long end = Volatile.Read(ref _countInEnd);
                if (end <= 0 || _countInBeatFrames <= 0 || _looper.State != LooperState.Armed) return 0;
                long left = end - NowFrame();
                if (left <= 0) return 0;
                return Math.Min(_countInBeats, (int)Math.Ceiling(left / _countInBeatFrames));
            }
        }

        /// <summary>Tempo the loop was recorded at (0 when empty).</summary>
        public double LoopBpm => _looper.LoopBpm;

        /// <summary>0 empty, 1 = first take, 2+ = overdubs.</summary>
        public int LayerCount => _looper.LayerCount;

        /// <summary>0..1 through the loop (0 when it is not playing).</summary>
        public double Position
        {
            get
            {
                if (_disposed) return 0;
                long frame = NowFrame();
                return _looper.PositionAt(frame);
            }
        }

        /// <summary>0..1 peak of the input, decaying; 0 when the input is closed.</summary>
        public float InputLevel => IsInputOpen ? Math.Clamp(Volatile.Read(ref _level), 0f, 1f) : 0f;

        public bool IsInputOpen => Volatile.Read(ref _session) != null;

        /// <summary>The device that is (or would be) captured.</summary>
        public string? InputDeviceName
        {
            get
            {
                var open = Volatile.Read(ref _session);
                if (open != null) return open.DeviceName;
                if (!_nameCacheValid)
                {
                    _nameCache = ResolveInput()?.Name;
                    _nameCacheValid = true;
                }
                return _nameCache;
            }
        }

        /// <summary>Human-readable, e.g. "No guitar input found — pick one below".</summary>
        public string? LastError => Volatile.Read(ref _lastError);

        public bool CanUndo => _looper.CanUndo;

        /// <summary>Raised on the UI thread on every state / layer / device change, and ~15 times a second while the input is open or a loop is running.</summary>
        public event Action? Changed;

        // ───────────────────────── options ─────────────────────────

        /// <summary>0 = free (ends when Record is pressed again), else 1, 2, 4, 8 bars.</summary>
        public int LengthBars
        {
            get => _looper.LengthBars;
            set => _looper.LengthBars = Math.Clamp(value, 0, 64);
        }

        /// <summary>Diagnostics: see <see cref="LooperEngine.InputStats"/>.</summary>
        public (long Buffers, long Slips, long Jumps) InputStats => _looper.InputStats;

        /// <summary>Diagnostics: frames the capture placement is off its own long-run estimate (see <c>CaptureClock.Error</c>).</summary>
        public double ClockErrorFrames => Volatile.Read(ref _session)?.Clock.Error ?? 0;

        /// <summary>Diagnostics: the format the open input delivers, e.g. "48000 Hz, 2 ch, 32-bit float" (null when closed).</summary>
        public string? InputFormat
        {
            get
            {
                var s = Volatile.Read(ref _session);
                return s == null ? null : $"{s.SourceRate} Hz, {s.Channels} ch, {s.Bits}-bit{(s.IsFloat ? " float" : "")}";
            }
        }

        /// <summary>True (default): the loop plays as soon as the first take ends; false: it waits for Play loop.</summary>
        public bool AutoPlay
        {
            get => _looper.AutoPlay;
            set => _looper.AutoPlay = value;
        }

        /// <summary>Loop playback gain 0..2 (on top of the auto-level gain).</summary>
        public float Volume
        {
            get => _looper.Volume;
            set => _looper.Volume = value;
        }

        /// <summary>Input + output delay to compensate, 0..400 ms (default 60).</summary>
        public int LatencyMs
        {
            get => Volatile.Read(ref _latencyMs);
            set => Volatile.Write(ref _latencyMs, Math.Clamp(value, 0, 400));
        }

        // ───────────────────────── input ─────────────────────────

        /// <summary>Real capture devices; Voicemeeter's own virtual outputs are hidden.</summary>
        public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
        {
            var devices = _audio.GetRecordingDevices()
                .Where(d => !AudioRoutingService.IsVoicemeeterDevice(d.Name))
                .ToList();
            _nameCacheValid = false;
            return devices;
        }

        /// <summary>The input to capture; null = automatic (the guitar from Sound setup, else the Windows default recording device).</summary>
        public string? InputDeviceId
        {
            get => _inputDeviceId;
            set
            {
                if (string.Equals(_inputDeviceId, value, StringComparison.Ordinal)) return;
                _inputDeviceId = string.IsNullOrEmpty(value) ? null : value;
                _nameCacheValid = false;
                if (IsInputOpen)
                {
                    CloseInput();
                    OpenInput();
                }
                else
                {
                    RaiseChanged();
                }
            }
        }

        /// <summary>Starts capturing (the level meter goes live). False + <see cref="LastError"/> on failure.</summary>
        public bool OpenInput()
        {
            if (_disposed) return false;
            if (IsInputOpen) return true;

            var target = ResolveInput();
            _nameCache = target?.Name;
            _nameCacheValid = true;
            if (target == null)
            {
                SetError("No guitar input found — pick one below");
                return false;
            }

            MMDevice? device = null;
            WasapiCapture? capture = null;
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                    device = enumerator.GetDevice(target.Id);

                // Shared mode, event driven: works while Voicemeeter has the same input open.
                capture = new WasapiCapture(device, true, 20) { ShareMode = AudioClientShareMode.Shared };
                var format = capture.WaveFormat;

                var session = new CaptureSession
                {
                    Capture = capture,
                    Device = device,
                    DeviceName = target.Name,
                    Channels = Math.Max(1, format.Channels),
                    Bits = format.BitsPerSample,
                    IsFloat = IsFloatFormat(format),
                    SourceRate = format.SampleRate,
                };
                if (session.SourceRate != _rate)
                {
                    // Sinc interpolation (the default is linear, which aliases a 48 kHz mic into 44.1 kHz).
                    const int sincSize = 64;
                    var resampler = new WdlResampler();
                    resampler.SetMode(true, 0, true, sincSize, 32);
                    resampler.SetFilterParms();
                    resampler.SetFeedMode(true);        // input driven: we hand over whatever the device delivered
                    resampler.SetRates(session.SourceRate, _rate);
                    session.Resampler = resampler;
                    // A linear-phase filter delays the stream by half its length; the samples are older than they arrive.
                    session.ResamplerDelay = (int)Math.Round(sincSize / 2.0 * _rate / session.SourceRate);
                }

                capture.DataAvailable += (_, e) => OnCaptureData(session, e);
                capture.RecordingStopped += (_, e) => OnCaptureStopped(session, e);
                Volatile.Write(ref _session, session);
                Volatile.Write(ref _level, 0f);
                capture.StartRecording();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Looper: could not open input {Device}", target.Name);
                Volatile.Write(ref _session, null);
                try { capture?.Dispose(); } catch { /* half-built */ }
                try { device?.Dispose(); } catch { /* half-built */ }
                SetError($"Could not open \"{target.Name}\" — it may be in use or unplugged");
                return false;
            }

            Volatile.Write(ref _lastError, null);
            EnsureTimer();
            RaiseChanged();
            return true;
        }

        public void CloseInput()
        {
            var session = Interlocked.Exchange(ref _session, null);
            if (session == null) return;

            try { session.Capture.StopRecording(); } catch { /* best effort */ }
            try { session.Capture.Dispose(); } catch { /* best effort */ }
            try { session.Device.Dispose(); } catch { /* best effort */ }
            Volatile.Write(ref _level, 0f);
            RaiseChanged();
        }

        // ───────────────────────── pedal ─────────────────────────

        /// <summary>Record / overdub. Opens the input by itself when it is closed.</summary>
        public void PressRecord()
        {
            if (_disposed) return;
            if (!IsInputOpen && !OpenInput()) return;

            var state = _looper.State;
            if (state == LooperState.Empty && !_metronome.IsPlaying)
            {
                ArmFreeTake();
            }
            else
            {
                // Overdubs of a free loop need the output (the loop's clock) too; bar-synced use is unchanged.
                if (state is LooperState.Stopped or LooperState.Playing) _metronome.EnsureOutputOpen();
                _looper.PressRecord(NowFrame());
                if (state == LooperState.Armed) CancelCountIn();
            }
            EnsureTimer();
        }

        public void PressStop()
        {
            if (_disposed) return;
            var state = _looper.State;
            if (state == LooperState.Stopped) _metronome.EnsureOutputOpen();   // a free loop restarts at once, on the output's clock
            _looper.PressStop(NowFrame());
            if (state == LooperState.Armed) CancelCountIn();
        }

        // Free mode: the drums are off, so the looper runs on the output's own clock. Count-in clicks are
        // scheduled sample-exactly; the take starts on the frame where the next downbeat would be.
        private void ArmFreeTake()
        {
            // The capture clock needs the output's position: open it first (the count-in lets the clock settle).
            if (!_metronome.EnsureOutputOpen())
            {
                SetError("Could not open the audio output for the looper");
                return;
            }

            var engine = _metronome.Engine;
            double bpm = engine.Bpm;
            int beatsPerBar = engine.BeatsPerBar;
            double beatFrames = _rate * 60.0 / bpm;
            long now = NowFrame();

            long start = now;
            int bars = CountInBars;
            Volatile.Write(ref _countInEnd, 0);
            if (bars > 0)
            {
                int beats = bars * beatsPerBar;
                long first = engine.ScheduleCountIn(beats, beatsPerBar, beatFrames);
                start = first + (long)Math.Round(beats * beatFrames);
                _countInBeats = beats;
                _countInBeatFrames = beatFrames;
                Volatile.Write(ref _countInEnd, start);
            }

            int fixedBars = LengthBars;
            long fixedFrames = fixedBars > 0 ? (long)Math.Round(fixedBars * beatsPerBar * beatFrames) : 0;
            _looper.PressRecordFree(now, start, fixedFrames, bpm, fixedBars);
        }

        private void CancelCountIn()
        {
            Volatile.Write(ref _countInEnd, 0);
            try { _metronome.Engine.CancelCountIn(); } catch { /* best effort */ }
        }

        /// <summary>Removes the newest overdub layer (not the first take).</summary>
        public bool Undo() => !_disposed && _looper.Undo();

        public void Clear()
        {
            if (!_disposed) _looper.Clear();
        }

        // ───────────────────────── calibration / export ─────────────────────────

        /// <summary>
        /// Measures <see cref="LatencyMs"/>: plays 8 clicks at 100 BPM, the user strums once on each, and the
        /// median delay between click and strum is the latency. The drums must be stopped. Null = could not measure.
        /// </summary>
        public async Task<int?> CalibrateAsync(CancellationToken ct)
        {
            if (_disposed || _calibrating) return null;
            if (_metronome.IsPlaying)
            {
                SetError("Stop the drums first — calibration plays its own clicks");
                return null;
            }
            if (!OpenInput()) return null;

            _calibrating = true;
            var capture = new CalibrationCapture();
            try
            {
                Volatile.Write(ref _calibration, capture);      // raw capture: no latency compensation
                long first = _metronome.PlayClickTrack(MetronomeSound.WoodBlock, CalibrationClicks, CalibrationBpm, out int interval);
                if (first < 0)
                {
                    SetError("Could not open the audio output for the calibration clicks");
                    return null;
                }

                // Last click + the strum window (350 ms) + room for the latency itself.
                double seconds = (CalibrationClicks - 1) * 60.0 / CalibrationBpm + 1.2;
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

                var (data, firstFrame) = capture.Snapshot();
                var clicks = new List<long>();
                for (int i = 0; i < CalibrationClicks; i++) clicks.Add(first + (long)i * interval);

                int? ms = LooperEngine.MeasureLatencyMs(data, firstFrame, clicks, _rate);
                if (ms == null)
                {
                    SetError("Could not hear enough strums — play once on each click, loud enough for the meter to move");
                    return null;
                }

                LatencyMs = ms.Value;
                Volatile.Write(ref _lastError, null);
                return ms;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                Volatile.Write(ref _calibration, null);
                _calibrating = false;
                RaiseChanged();
            }
        }

        /// <summary>Writes one cycle of the mixed loop as a 44.1 kHz 16-bit stereo WAV. False when empty or on failure.</summary>
        public bool ExportWav(string path)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                return _looper.ExportWav(path);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Looper: export to {Path} failed", path);
                SetError("Could not save the loop: " + ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _timer?.Stop(); } catch { /* best effort */ }
            _timer = null;
            CloseInput();
            _looper.Changed -= OnLooperChanged;
            try { _metronome.Engine.AttachLooper(null); } catch { /* best effort */ }
        }

        // ───────────────────────── device resolution ─────────────────────────

        private AudioDeviceInfo? ResolveInput()
        {
            IReadOnlyList<AudioDeviceInfo> all;
            try { all = _audio.GetRecordingDevices(); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Looper: could not list recording devices");
                return null;
            }

            // 1. The user's pick.
            if (!string.IsNullOrEmpty(_inputDeviceId))
            {
                var picked = all.FirstOrDefault(d => string.Equals(d.Id, _inputDeviceId, StringComparison.OrdinalIgnoreCase));
                if (picked != null) return picked;
            }

            // 2. What Banana's guitar strip actually uses (what the Sound tab shows), while Voicemeeter runs.
            var banana = GetBananaGuitarName();
            if (!string.IsNullOrWhiteSpace(banana))
            {
                var match = MatchDeviceName(all, banana);
                if (match != null) return match;
            }

            // 3. The guitar saved in the Sound setup — unless it is the very device saved as the microphone
            //    (an unset guitar once defaulted to the headset mic).
            try
            {
                var s = _voicemeeterSettings.Load().Settings;
                bool sameAsMic =
                    (!string.IsNullOrEmpty(s.GuitarDeviceId) && string.Equals(s.GuitarDeviceId, s.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(s.GuitarDeviceName) && string.Equals(s.GuitarDeviceName, s.MicrophoneDeviceName, StringComparison.OrdinalIgnoreCase));
                if (!sameAsMic)
                {
                    if (!string.IsNullOrEmpty(s.GuitarDeviceId))
                    {
                        var byId = all.FirstOrDefault(d => string.Equals(d.Id, s.GuitarDeviceId, StringComparison.OrdinalIgnoreCase));
                        if (byId != null) return byId;
                    }
                    if (!string.IsNullOrEmpty(s.GuitarDeviceName))
                    {
                        var byName = all.FirstOrDefault(d => string.Equals(d.Name, s.GuitarDeviceName, StringComparison.OrdinalIgnoreCase));
                        if (byName != null) return byName;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Looper: could not read the guitar device from the Voicemeeter settings");
            }

            // 4. Windows' default recording device — unless that is one of Voicemeeter's own outputs.
            var def = _audio.GetDefaultRecording();
            if (def != null)
            {
                var match = all.FirstOrDefault(d =>
                    (!string.IsNullOrEmpty(def.Id) && string.Equals(d.Id, def.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (string.IsNullOrEmpty(def.Id) && string.Equals(d.Name, def.Name, StringComparison.OrdinalIgnoreCase)));
                if (match != null && !AudioRoutingService.IsVoicemeeterDevice(match.Name)) return match;
            }
            return null;
        }

        // The input Banana's guitar strip has right now, or null (not running / not set / the API failed).
        // UI thread only: the Remote API can be slow, so the answer is kept for a few seconds.
        private string? GetBananaGuitarName()
        {
            if (_voicemeeter == null) return null;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_bananaNameAt != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_bananaNameAt, now) < BananaNameMaxAge)
                return _bananaName;

            string? name = null;
            try
            {
                if (_voicemeeter.Status == VoicemeeterStatus.Running)
                    name = _voicemeeter.GetHardwareInputName(_voicemeeter.GuitarStripIndex)?.Trim();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Looper: could not ask Voicemeeter for the guitar input");
            }
            _bananaName = name;
            _bananaNameAt = now;
            return name;
        }

        // Banana may report a truncated or decorated name: exact first, then one starting with the other, then containing it.
        internal static AudioDeviceInfo? MatchDeviceName(IReadOnlyList<AudioDeviceInfo> devices, string name)
        {
            name = name.Trim();
            if (name.Length < 3 || name == "-") return null;
            devices = devices.Where(d => d.Name.Trim().Length >= 3 && !AudioRoutingService.IsVoicemeeterDevice(d.Name)).ToList();

            var exact = devices.FirstOrDefault(d => string.Equals(d.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var prefix = devices.FirstOrDefault(d =>
                d.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(d.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (prefix != null) return prefix;

            return devices.FirstOrDefault(d =>
                d.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(d.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // ───────────────────────── capture (NAudio's thread) ─────────────────────────

        private static bool IsFloatFormat(WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat) return true;
            if (format is WaveFormatExtensible ext)
                return ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");   // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
            return false;
        }

        private void OnCaptureData(CaptureSession session, WaveInEventArgs e)
        {
            if (!ReferenceEquals(session, Volatile.Read(ref _session))) return;

            try
            {
                int bytesPerSample = Math.Max(1, session.Bits / 8);
                int frameBytes = bytesPerSample * session.Channels;
                int frames = e.BytesRecorded / frameBytes;
                if (frames <= 0) return;

                if (session.Mono.Length < frames) session.Mono = new float[frames * 2];
                DecodeToMono(session, e.Buffer, frames, bytesPerSample, frameBytes);

                float[] output;
                int n;
                if (session.Resampler == null)
                {
                    output = session.Mono;
                    n = frames;
                }
                else
                {
                    session.Resampler.ResamplePrepare(frames, 1, out float[] inBuffer, out int inOffset);
                    Array.Copy(session.Mono, 0, inBuffer, inOffset, frames);
                    int maxOut = (int)((long)frames * _rate / session.SourceRate) + 64;
                    if (session.Resampled.Length < maxOut) session.Resampled = new float[maxOut * 2];
                    n = session.Resampler.ResampleOut(session.Resampled, 0, frames, maxOut, 1);
                    output = session.Resampled;
                }
                if (n <= 0) return;

                float peak = 0f;
                for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(output[i]));
                Volatile.Write(ref _level, Math.Max(peak, Volatile.Read(ref _level) * LevelDecay));

                // Stamp the buffer with the engine frame that was audible when its last sample arrived.
                long audible = _metronome.GetAudibleFrame();
                session.Total += n;
                if (audible < 0)
                {
                    session.Clock.Reset();      // no output: the clock restarts when one opens
                    return;
                }
                long start = session.Clock.Stamp(audible, session.Total, n);
                if (start == CaptureClock.NotReady) return;   // warming up: nothing is placed yet
                start -= session.ResamplerDelay;

                var calibration = Volatile.Read(ref _calibration);
                if (calibration != null) calibration.Append(start, output, n);
                else _looper.WriteInput(start - (long)Volatile.Read(ref _latencyMs) * _rate / 1000, output, 0, n);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Looper: capture callback failed");
            }
        }

        /// <summary>
        /// Whatever the device delivers (16/24/32-bit PCM or float, any channel count) to one mono float
        /// stream: the average of the channels that carry signal. (Picking the loudest channel per
        /// *sample* switches between channels thousands of times a second — audible distortion on a
        /// stereo source such as an amp's USB output.) A channel counts as silent while its level stays
        /// 40 dB under the loudest one, so a mono mic on a stereo device is not halved.
        /// </summary>
        private static void DecodeToMono(CaptureSession s, byte[] buffer, int frames, int bytesPerSample, int frameBytes)
        {
            int channels = s.Channels;
            if (s.ChannelEnergy.Length != channels)
            {
                s.ChannelEnergy = new double[channels];
                s.ChannelActive = new bool[channels];
                Array.Fill(s.ChannelActive, true);
            }
            var energy = s.ChannelEnergy;
            var active = s.ChannelActive;

            int activeCount = 0;
            for (int c = 0; c < channels; c++) if (active[c]) activeCount++;
            float scale = 1f / Math.Max(1, activeCount);

            Span<double> sum = channels <= 16 ? stackalloc double[channels] : new double[channels];
            var mono = s.Mono;
            for (int i = 0; i < frames; i++)
            {
                float mix = 0f;
                int at = i * frameBytes;
                for (int c = 0; c < channels; c++, at += bytesPerSample)
                {
                    float v;
                    if (s.IsFloat && bytesPerSample == 4) v = BitConverter.ToSingle(buffer, at);
                    else if (bytesPerSample == 2) v = BitConverter.ToInt16(buffer, at) / 32768f;
                    else if (bytesPerSample == 3) v = ((buffer[at] << 8 | buffer[at + 1] << 16 | buffer[at + 2] << 24) >> 8) / 8388608f;
                    else if (bytesPerSample == 4) v = BitConverter.ToInt32(buffer, at) / 2147483648f;
                    else v = 0f;
                    sum[c] += (double)v * v;
                    if (active[c]) mix += v;
                }
                mono[i] = mix * scale;
            }

            // Which channels carry signal — decided slowly (≈ 1 s), applied from the next buffer on.
            double loudest = 0;
            for (int c = 0; c < channels; c++)
            {
                energy[c] += (sum[c] / frames - energy[c]) * 0.02;
                loudest = Math.Max(loudest, energy[c]);
            }
            if (loudest > 1e-9)
                for (int c = 0; c < channels; c++) active[c] = energy[c] >= loudest * 1e-4;
        }

        private void OnCaptureStopped(CaptureSession session, StoppedEventArgs e)
        {
            if (!ReferenceEquals(session, Volatile.Read(ref _session))) return;
            // The device went away (or capture failed) under us.
            PostToUi(() =>
            {
                if (_disposed || !ReferenceEquals(session, Volatile.Read(ref _session))) return;
                _logger?.LogWarning(e.Exception, "Looper: input stopped unexpectedly");
                CloseInput();
                SetError("The input device was lost — pick it again or plug it back in");
            });
        }

        // ───────────────────────── helpers ─────────────────────────

        // The engine frame that is audible right now (the render head when there is no output yet).
        private long NowFrame()
        {
            long audible = _metronome.GetAudibleFrame();
            return audible >= 0 ? audible : _metronome.Engine.FramesRendered;
        }

        private void SetError(string message)
        {
            Volatile.Write(ref _lastError, message);
            RaiseChanged();
        }

        // The looper changed on the audio thread (bar start), the capture thread or the UI thread.
        private void OnLooperChanged()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _changedPending, 1) != 0) return;
            PostToUi(() =>
            {
                Interlocked.Exchange(ref _changedPending, 0);
                if (_disposed) return;
                EnsureTimer();
                Changed?.Invoke();
            });
        }

        private void RaiseChanged()
        {
            if (_disposed) return;
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
            else PostToUi(() => { if (!_disposed) Changed?.Invoke(); });
        }

        private static void PostToUi(Action action)
        {
            try { Dispatcher.UIThread.Post(action); }
            catch (InvalidOperationException) { /* dispatcher already shut down */ }
        }

        private bool NeedsTimer =>
            IsInputOpen || _looper.State is LooperState.Armed or LooperState.Recording
                or LooperState.Playing or LooperState.Overdubbing;

        // The ~15 Hz heartbeat for the level meter and the position bar; stops when there is nothing to show.
        private void EnsureTimer()
        {
            if (_disposed || !Dispatcher.UIThread.CheckAccess()) return;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = ChangedInterval };
                _timer.Tick += (_, _) =>
                {
                    if (_disposed) return;
                    if (!NeedsTimer) _timer?.Stop();
                    Changed?.Invoke();
                };
            }
            if (NeedsTimer && !_timer.IsEnabled) _timer.Start();
        }
    }
}
