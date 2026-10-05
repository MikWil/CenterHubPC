using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CenterHubNew.MVVM.Models;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// The loop logic of the guitar looper, with no device and no UI. Everything is measured in
    /// *engine frames* (the absolute frame counter of <see cref="DrumMachineEngine"/>): bar starts
    /// arrive from the engine (<see cref="OnBarStart"/>), the loop is mixed into the engine's output
    /// (<see cref="Mix"/>), and captured guitar samples arrive stamped with the engine frame that was
    /// audible when they were played (<see cref="WriteInput"/>).
    ///
    /// Threads: the audio thread calls OnBarStart/OnEngineStart/OnEngineStop/Mix (from inside the
    /// engine's lock), the capture thread calls WriteInput, the UI thread calls the commands. One
    /// short lock guards everything; nothing in here ever calls back into the engine while holding it.
    /// </summary>
    public sealed class LooperEngine
    {
        /// <summary>Layers kept for Undo; the 9th overdub merges the two oldest.</summary>
        public const int MaxLayers = 8;

        /// <summary>A loop never gets longer than this; recording closes at the last full bar before it.</summary>
        public const double MaxLoopSeconds = 60;

        private const double SeamMs = 5;
        private const double StopFadeMs = 60;
        private const int MaxSlipFrames = 8;    // capture-clock corrections of a few frames are bridged, not treated as gaps

        /// <summary>One recorded pass (mono). The first take is a layer too.</summary>
        private sealed class Layer
        {
            public float[] Data = Array.Empty<float>();
            public long Start = long.MaxValue;  // first frame it accepts (MaxValue = waiting for the next bar)
            public long End = long.MaxValue;    // first frame it no longer accepts (MaxValue = open-ended)
            public long Origin;                 // first take only: the engine frame of Data[0]
            public bool IsTake;
            public bool Closed;                 // no more input will be written
        }

        private readonly object _lock = new();
        private readonly int _rate;
        private readonly int _seamFrames;
        private readonly int _fadeFrames;
        private readonly int _maxFrames;

        private int _lengthBars;
        private float _volume = 1f;

        private LooperState _state = LooperState.Empty;
        private Layer? _take;                       // the first take while armed / recording
        private readonly List<Layer> _layers = new();
        private float[][] _snapshot = Array.Empty<float[]>();
        private Layer? _overdub;                    // the layer being recorded on top of the loop

        private long _loopFrames;
        private int _loopBars;
        private double _loopBpm;
        private int[] _barOffsets = Array.Empty<int>(); // frame offset of each loop bar from the loop start (loopBars + 1 entries)

        // Mirror of the engine's transport.
        private bool _engineRunning;
        private int _barIndex = -1;                 // index of the latest real bar (count-in excluded), -1 = none yet
        private long _lastBarFrame;

        // First take.
        private int _takeStartBar;
        private long _takeStart;
        private double _takeBpm;
        private bool _takeStopRequested;
        private readonly List<int> _takeOffsets = new();

        // Playback.
        private bool _audible;                      // the loop is producing sound (or fading out)
        private bool _awaitBar;                     // (re)start in phase at the next bar start
        private bool _stopPending;                  // start the fade-out at the next Mix
        private long _cycleStart;                   // engine frame at which the current pass of the loop began
        private long _playFrom;
        private long _stopFrame = long.MaxValue;
        private int _phaseBar;                      // engine bar index at which the loop's first bar plays

        // Input continuity.
        private bool _haveInput;
        private long _lastInputEnd;

        public LooperEngine(int sampleRate = 44100)
        {
            _rate = sampleRate;
            _seamFrames = Math.Max(1, (int)(sampleRate * SeamMs / 1000.0));
            _fadeFrames = Math.Max(1, (int)(sampleRate * StopFadeMs / 1000.0));
            _maxFrames = (int)(sampleRate * MaxLoopSeconds);
        }

        /// <summary>Raised (on whatever thread caused it, outside the lock) when state or layers change.</summary>
        public event Action? Changed;

        // ───────────────────────── options ─────────────────────────

        /// <summary>0 = free (ends when Record is pressed again), else the number of bars of the first take.</summary>
        public int LengthBars
        {
            get => Volatile.Read(ref _lengthBars);
            set => Volatile.Write(ref _lengthBars, Math.Max(0, value));
        }

        /// <summary>
        /// True (default): the loop plays as soon as the first take ends. False: the take is kept
        /// silent (<see cref="LooperState.Stopped"/>) until the Stop/Play pedal starts it.
        /// </summary>
        public bool AutoPlay
        {
            get => Volatile.Read(ref _autoPlay);
            set => Volatile.Write(ref _autoPlay, value);
        }
        private bool _autoPlay = true;

        /// <summary>Loop playback gain, 0..1.5.</summary>
        public float Volume
        {
            get => Volatile.Read(ref _volume);
            set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 1.5f));
        }

        // ───────────────────────── state ─────────────────────────

        public int SampleRate => _rate;

        public LooperState State { get { lock (_lock) return _state; } }

        /// <summary>Length of the loop in bars (0 when empty).</summary>
        public int LoopBars { get { lock (_lock) return _loopBars; } }

        /// <summary>Length of the loop in frames (0 when empty).</summary>
        public long LoopFrames { get { lock (_lock) return _loopFrames; } }

        /// <summary>Tempo the loop was recorded at (0 when empty).</summary>
        public double LoopBpm { get { lock (_lock) return _loopBpm; } }

        /// <summary>0 empty, 1 = first take, 2+ = overdubs.</summary>
        public int LayerCount { get { lock (_lock) return _layers.Count; } }

        public bool CanUndo { get { lock (_lock) return _layers.Count >= 2; } }

        /// <summary>True while the loop makes sound.</summary>
        public bool IsAudible { get { lock (_lock) return _audible; } }

        /// <summary>True while the loop is still fading out after the engine stopped — the output must stay open.</summary>
        public bool IsBusy { get { lock (_lock) return _audible && _stopFrame != long.MaxValue; } }

        /// <summary>0..1 through the loop at <paramref name="frame"/> (0 when the loop is not sounding).</summary>
        public double PositionAt(long frame)
        {
            lock (_lock)
            {
                if (!_audible || _loopFrames <= 0) return 0;
                return FloorMod(frame - _cycleStart, _loopFrames) / (double)_loopFrames;
            }
        }

        // ───────────────────────── pedal ─────────────────────────

        /// <summary>The Record pedal. <paramref name="nowFrame"/> is the engine frame that is audible right now.</summary>
        public void PressRecord(long nowFrame)
        {
            // Big arrays are allocated before taking the lock: the audio thread must never wait for them.
            LooperState peek;
            lock (_lock) peek = _state;
            float[]? buffer = null;
            if (peek == LooperState.Empty)
            {
                buffer = new float[_maxFrames];
            }
            else if (peek is LooperState.Playing or LooperState.Stopped)
            {
                MergeOldestIfFull();
                int need;
                lock (_lock) need = (int)_loopFrames;
                buffer = new float[need];
            }

            lock (_lock)
            {
                switch (_state)
                {
                    case LooperState.Empty:
                        if (buffer == null || buffer.Length != _maxFrames) buffer = new float[_maxFrames];
                        _take = new Layer { Data = buffer, IsTake = true };
                        _state = LooperState.Armed;
                        break;

                    case LooperState.Armed:
                        _take = null;
                        _state = LooperState.Empty;
                        break;

                    case LooperState.Recording:
                        // A fixed length ends by itself; only a free take is ended by the pedal.
                        if (LengthBars != 0 || _takeStopRequested) return;
                        _takeStopRequested = true;
                        break;

                    case LooperState.Playing:
                    case LooperState.Stopped:
                        if (buffer == null || buffer.Length != (int)_loopFrames) buffer = new float[_loopFrames];
                        StartOverdub(buffer, nowFrame);
                        _state = LooperState.Overdubbing;
                        break;

                    case LooperState.Overdubbing:
                        CloseOverdub(nowFrame);
                        _state = LooperState.Playing;
                        break;
                }
            }
            Notify();
        }

        /// <summary>The Stop pedal.</summary>
        public void PressStop(long nowFrame)
        {
            bool changed = true;
            lock (_lock)
            {
                switch (_state)
                {
                    case LooperState.Armed:
                        _take = null;
                        _state = LooperState.Empty;
                        break;

                    case LooperState.Recording:
                        _take = null;
                        _takeStopRequested = false;
                        _state = LooperState.Empty;
                        break;

                    case LooperState.Playing:
                        SilenceLoop();
                        _state = LooperState.Stopped;
                        break;

                    case LooperState.Overdubbing:
                        CloseOverdub(nowFrame);
                        SilenceLoop();
                        _state = LooperState.Stopped;
                        break;

                    case LooperState.Stopped:
                        // Resume in phase with the bars at the next bar start.
                        _state = LooperState.Playing;
                        _audible = false;
                        _stopPending = false;
                        _stopFrame = long.MaxValue;
                        _awaitBar = _engineRunning;
                        break;

                    default:
                        changed = false;
                        break;
                }
            }
            if (changed) Notify();
        }

        /// <summary>Removes the newest overdub layer (never the first take).</summary>
        public bool Undo()
        {
            lock (_lock)
            {
                if (_layers.Count < 2) return false;
                var last = _layers[^1];
                _layers.RemoveAt(_layers.Count - 1);
                if (ReferenceEquals(last, _overdub))
                {
                    _overdub = null;
                    if (_state == LooperState.Overdubbing) _state = LooperState.Playing;
                }
                RefreshSnapshot();
            }
            Notify();
            return true;
        }

        /// <summary>Forgets the loop.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _take = null;
                _overdub = null;
                _layers.Clear();
                RefreshSnapshot();
                _loopFrames = 0;
                _loopBars = 0;
                _loopBpm = 0;
                _barOffsets = Array.Empty<int>();
                _takeStopRequested = false;
                _audible = false;
                _awaitBar = false;
                _stopPending = false;
                _stopFrame = long.MaxValue;
                _state = LooperState.Empty;
            }
            Notify();
        }

        // ───────────────────────── engine hooks (audio thread) ─────────────────────────

        /// <summary>The engine (re)started; its first real bar after the count-in will be bar 0.</summary>
        public void OnEngineStart(long frame)
        {
            bool changed = false;
            lock (_lock)
            {
                if (_engineRunning) changed = StopCore(frame);
                _engineRunning = true;
                _barIndex = -1;
                _phaseBar = 0;
                if (_state is LooperState.Playing or LooperState.Overdubbing)
                {
                    // The loop starts at its beginning on the first bar.
                    _audible = false;
                    _stopPending = false;
                    _stopFrame = long.MaxValue;
                    _awaitBar = true;
                }
            }
            if (changed) Notify();
        }

        /// <summary>The engine started a real bar (not a count-in bar) at <paramref name="frame"/>.</summary>
        public void OnBarStart(long frame, double bpm)
        {
            bool changed = false;
            lock (_lock)
            {
                _engineRunning = true;
                _barIndex++;
                long previousBar = _lastBarFrame;
                _lastBarFrame = frame;

                switch (_state)
                {
                    case LooperState.Armed:
                        StartTake(frame, bpm);
                        changed = true;
                        break;

                    case LooperState.Recording:
                        changed = AdvanceTake(frame, previousBar);
                        break;

                    case LooperState.Playing:
                    case LooperState.Overdubbing:
                        BarStartPlayback(frame);
                        break;
                }
            }
            if (changed) Notify();
        }

        /// <summary>The engine stopped at <paramref name="frame"/>: the loop stops with it.</summary>
        public void OnEngineStop(long frame)
        {
            bool changed;
            lock (_lock)
            {
                if (!_engineRunning) return;
                changed = StopCore(frame);
            }
            if (changed) Notify();
        }

        /// <summary>
        /// Adds the loop to the engine's output: <paramref name="frames"/> stereo frames in
        /// <paramref name="buffer"/> starting at <paramref name="offset"/>, the first being engine frame
        /// <paramref name="firstFrame"/>. Leaves the buffer untouched when there is nothing to play.
        /// </summary>
        public void Mix(float[] buffer, int offset, int frames, long firstFrame, float masterVolume)
        {
            lock (_lock)
            {
                if (_stopPending)
                {
                    if (_audible && _stopFrame == long.MaxValue) _stopFrame = firstFrame;
                    _stopPending = false;
                }

                if (!_audible || _loopFrames <= 0 || _snapshot.Length == 0) return;

                long endFrame = firstFrame + frames;
                long from = Math.Max(firstFrame, _playFrom);
                long to = _stopFrame == long.MaxValue ? endFrame : Math.Min(endFrame, _stopFrame + _fadeFrames);
                int len = (int)_loopFrames;
                var layers = _snapshot;
                float gain = Volume * masterVolume;

                long f = from;
                while (f < to)
                {
                    int pos = (int)FloorMod(f - _cycleStart, len);
                    int chunk = (int)Math.Min(to - f, len - pos);
                    int o = offset + (int)(f - firstFrame) * 2;
                    for (int i = 0; i < chunk; i++, o += 2)
                    {
                        float sum = 0f;
                        for (int k = 0; k < layers.Length; k++) sum += layers[k][pos + i];
                        if (sum == 0f) continue;

                        float g = gain * SeamGain(pos + i, len, _seamFrames);
                        long frame = f + i;
                        if (frame >= _stopFrame) g *= 1f - (frame - _stopFrame) / (float)_fadeFrames;

                        float x = sum * g;
                        buffer[o] = SoftLimit(buffer[o] + x);
                        buffer[o + 1] = SoftLimit(buffer[o + 1] + x);
                    }
                    f += chunk;
                }

                if (_stopFrame != long.MaxValue && endFrame >= _stopFrame + _fadeFrames)
                {
                    _audible = false;
                    _stopFrame = long.MaxValue;
                }
            }
        }

        // ───────────────────────── capture (capture thread) ─────────────────────────

        /// <summary>
        /// Adds captured mono samples. <paramref name="firstFrame"/> is the engine frame of
        /// <c>data[offset]</c> — already corrected for latency, i.e. the frame that was audible when the
        /// note was played. A slip of a few frames against the previous buffer (clock correction) is bridged.
        /// </summary>
        /// <summary>
        /// Diagnostics: buffers written while a take or loop existed, how many of them were a clock
        /// correction of a few frames (stretched to fit — no click), and how many were further off
        /// than that (a real gap or overlap, which is audible).
        /// </summary>
        public (long Buffers, long Slips, long Jumps) InputStats
        {
            get { lock (_lock) return (_inputBuffers, _inputSlips, _inputJumps); }
        }
        private long _inputBuffers, _inputSlips, _inputJumps;
        private float[] _stretch = Array.Empty<float>();

        public void WriteInput(long firstFrame, float[] data, int offset, int count)
        {
            if (count <= 0) return;
            lock (_lock)
            {
                if (_take == null && _layers.Count == 0)
                {
                    _haveInput = false;
                    return;
                }

                _inputBuffers++;
                if (_haveInput)
                {
                    long gap = firstFrame - _lastInputEnd;
                    if (gap != 0)
                    {
                        if (Math.Abs(gap) <= MaxSlipFrames) _inputSlips++;
                        else _inputJumps++;
                    }
                    if (gap != 0 && Math.Abs(gap) <= MaxSlipFrames && count + gap >= 32)
                    {
                        // A clock correction of a few frames: stretch this buffer so it starts where the
                        // previous one ended and still ends where it should. Dropping or repeating the
                        // samples instead leaves a step in the waveform — a click on every correction.
                        int stretched = count + (int)gap;
                        if (_stretch.Length < stretched) _stretch = new float[stretched * 2];
                        double step = (count - 1) / (double)(stretched - 1);
                        for (int i = 0; i < stretched; i++)
                        {
                            double at = i * step;
                            int i0 = (int)at;
                            int i1 = Math.Min(i0 + 1, count - 1);
                            float f = (float)(at - i0);
                            _stretch[i] = data[offset + i0] * (1f - f) + data[offset + i1] * f;
                        }
                        WriteSpan(_lastInputEnd, new ReadOnlySpan<float>(_stretch, 0, stretched));
                        _lastInputEnd = firstFrame + count;
                        return;
                    }
                }

                WriteSpan(firstFrame, new ReadOnlySpan<float>(data, offset, count));
                _lastInputEnd = firstFrame + count;
                _haveInput = true;
            }
        }

        // ───────────────────────── rendering / export ─────────────────────────

        /// <summary>One cycle of the loop (all layers × Volume, seams faded), mono. Null when empty.</summary>
        public float[]? RenderCycle()
        {
            lock (_lock)
            {
                if (_loopFrames <= 0 || _snapshot.Length == 0) return null;
                int len = (int)_loopFrames;
                var mix = new float[len];
                float gain = Volume;
                for (int i = 0; i < len; i++)
                {
                    float sum = 0f;
                    for (int k = 0; k < _snapshot.Length; k++) sum += _snapshot[k][i];
                    mix[i] = sum * gain * SeamGain(i, len, _seamFrames);
                }
                return mix;
            }
        }

        /// <summary>Writes one cycle of the loop as a 16-bit stereo WAV. False when empty; IO errors propagate.</summary>
        public bool ExportWav(string path)
        {
            var mix = RenderCycle();
            if (mix == null) return false;

            using var writer = new WaveFileWriter(path, new WaveFormat(_rate, 16, 2));
            var bytes = new byte[4096 * 4];
            int i = 0;
            while (i < mix.Length)
            {
                int n = Math.Min(4096, mix.Length - i);
                for (int j = 0; j < n; j++)
                {
                    short s = (short)Math.Clamp((int)MathF.Round(mix[i + j] * 32767f), short.MinValue, short.MaxValue);
                    bytes[j * 4] = bytes[j * 4 + 2] = (byte)(s & 0xFF);
                    bytes[j * 4 + 1] = bytes[j * 4 + 3] = (byte)((s >> 8) & 0xFF);
                }
                writer.Write(bytes, 0, n * 4);
                i += n;
            }
            return true;
        }

        // ───────────────────────── calibration maths ─────────────────────────

        /// <summary>
        /// Latency from a strum recorded against known clicks. For each click the onset (first sample above
        /// 25 % of the window's peak, window −80 … +350 ms around the click) is found; clicks whose window
        /// is below the noise floor are ignored. Returns the median onset offset in ms (0..400), or null
        /// with fewer than 4 usable onsets. <paramref name="firstFrame"/> is the engine frame of <c>input[0]</c>.
        /// </summary>
        public static int? MeasureLatencyMs(float[] input, long firstFrame, IReadOnlyList<long> clickFrames,
                                            int sampleRate, float noiseFloor = 0.01f)
        {
            int before = (int)(sampleRate * 0.080);
            int after = (int)(sampleRate * 0.350);
            var offsets = new List<long>();

            foreach (long click in clickFrames)
            {
                long a = Math.Max(click - before, firstFrame);
                long b = Math.Min(click + after, firstFrame + input.Length);
                if (b <= a) continue;

                float peak = 0f;
                for (long f = a; f < b; f++) peak = Math.Max(peak, Math.Abs(input[f - firstFrame]));
                if (peak < noiseFloor) continue;

                float threshold = peak * 0.25f;
                for (long f = a; f < b; f++)
                {
                    if (Math.Abs(input[f - firstFrame]) >= threshold)
                    {
                        offsets.Add(f - click);
                        break;
                    }
                }
            }

            if (offsets.Count < 4) return null;
            offsets.Sort();
            double median = offsets.Count % 2 == 1
                ? offsets[offsets.Count / 2]
                : (offsets[offsets.Count / 2 - 1] + offsets[offsets.Count / 2]) / 2.0;
            int ms = (int)Math.Round(median * 1000.0 / sampleRate);
            return Math.Clamp(ms, 0, 400);
        }

        // ───────────────────────── internals (lock held) ─────────────────────────

        private void StartTake(long frame, double bpm)
        {
            var take = _take!;
            take.Start = frame;
            take.Origin = frame;
            _takeStart = frame;
            _takeStartBar = _barIndex;
            _takeBpm = bpm;
            _takeStopRequested = false;
            _takeOffsets.Clear();
            _takeOffsets.Add(0);
            _state = LooperState.Recording;
            _haveInput = false;
        }

        /// <summary>A bar of the first take finished. Returns true when the take ended.</summary>
        private bool AdvanceTake(long frame, long previousBar)
        {
            int done = _barIndex - _takeStartBar;
            _takeOffsets.Add((int)Math.Min(frame - _takeStart, int.MaxValue));

            int target = LengthBars;
            bool end = (target > 0 && done >= target) || (_takeStopRequested && done >= 1);
            // The next bar would not fit in the longest allowed loop: close at the last full bar.
            if (!end && done >= 1 && frame - _takeStart + (frame - previousBar) > _maxFrames) end = true;
            if (!end) return false;

            var take = _take!;
            _take = null;
            _loopFrames = Math.Min(frame - _takeStart, take.Data.Length);
            _loopBars = done;
            _loopBpm = _takeBpm;
            _barOffsets = _takeOffsets.ToArray();
            take.End = frame;
            _layers.Clear();
            _layers.Add(take);
            RefreshSnapshot();

            _phaseBar = _takeStartBar;
            // Auto-play off: keep the take but stay silent until the Stop/Play pedal starts it.
            bool play = AutoPlay;
            _state = play ? LooperState.Playing : LooperState.Stopped;
            _audible = play;
            _awaitBar = false;
            _stopPending = false;
            _stopFrame = long.MaxValue;
            _cycleStart = frame;
            _playFrom = frame;
            _takeStopRequested = false;
            _overdub = null;
            return true;
        }

        private void BarStartPlayback(long frame)
        {
            if (_loopBars <= 0) return;
            int rel = (int)FloorMod(_barIndex - _phaseBar, _loopBars);

            if (_awaitBar)
            {
                // (Re)start in phase with the bars: where the loop would be had it never stopped.
                _awaitBar = false;
                _audible = true;
                _stopPending = false;
                _stopFrame = long.MaxValue;
                _cycleStart = frame - _barOffsets[rel];
                _playFrom = frame;
                if (_overdub is { } od && od.Start == long.MaxValue) od.Start = frame;
            }
            else if (_audible && _stopFrame == long.MaxValue && rel == 0)
            {
                // Every loop start that coincides with an engine bar: re-align (≤ 1 frame at the recorded tempo).
                _cycleStart = frame;
            }
        }

        private bool StopCore(long frame)
        {
            _engineRunning = false;
            bool changed = false;
            switch (_state)
            {
                case LooperState.Recording:
                    _take = null;
                    _takeStopRequested = false;
                    _state = LooperState.Empty;
                    changed = true;
                    break;

                case LooperState.Overdubbing:
                    CloseOverdub(frame);
                    _state = LooperState.Playing;
                    changed = true;
                    break;
            }

            _awaitBar = false;
            _stopPending = false;
            if (_audible && _stopFrame == long.MaxValue) _stopFrame = frame;
            return changed;
        }

        private void StartOverdub(float[] buffer, long nowFrame)
        {
            var layer = new Layer { Data = buffer };
            if (_audible && _stopFrame == long.MaxValue && !_stopPending) layer.Start = nowFrame;
            else if (_engineRunning) _awaitBar = true;      // starts when the loop does, at a bar
            _layers.Add(layer);
            _overdub = layer;
            RefreshSnapshot();
        }

        private void CloseOverdub(long nowFrame)
        {
            var layer = _overdub;
            _overdub = null;
            if (layer == null) return;
            if (layer.Start == long.MaxValue)
            {
                _layers.Remove(layer);      // never started: nothing was recorded
                RefreshSnapshot();
            }
            else
            {
                layer.End = Math.Max(nowFrame, layer.Start);
            }
        }

        /// <summary>The loop goes quiet with a short fade (started at the next Mix).</summary>
        private void SilenceLoop()
        {
            _awaitBar = false;
            if (_audible && _stopFrame == long.MaxValue) _stopPending = true;
        }

        private void WriteSpan(long frame0, ReadOnlySpan<float> data)
        {
            long end0 = frame0 + data.Length;
            if (_take != null) WriteLayer(_take, frame0, end0, data);
            for (int i = 0; i < _layers.Count; i++) WriteLayer(_layers[i], frame0, end0, data);
        }

        private void WriteLayer(Layer layer, long frame0, long end0, ReadOnlySpan<float> data)
        {
            if (layer.Closed || layer.Start == long.MaxValue) return;

            long a = Math.Max(frame0, layer.Start);
            long b = Math.Min(end0, layer.End);
            if (b > a)
            {
                var dst = layer.Data;
                if (layer.IsTake)
                {
                    for (long f = a; f < b; f++)
                    {
                        long idx = f - layer.Origin;
                        if ((ulong)idx < (ulong)dst.Length) dst[idx] += data[(int)(f - frame0)];
                    }
                }
                else if (_loopFrames > 0)
                {
                    // Overdub passes add up: a layer that stays open for several cycles is summed.
                    int len = (int)_loopFrames;
                    int pos = (int)FloorMod(a - _cycleStart, len);
                    for (long f = a; f < b; f++)
                    {
                        if (pos < dst.Length) dst[pos] += data[(int)(f - frame0)];
                        if (++pos == len) pos = 0;
                    }
                }
            }

            // Everything up to the end of the layer's window has arrived: it is complete.
            if (end0 >= layer.End) CloseLayer(layer);
        }

        private void CloseLayer(Layer layer)
        {
            layer.Closed = true;
            if (layer.IsTake && _loopFrames > 0 && layer.Data.Length != _loopFrames && _layers.Contains(layer))
            {
                // The first take was recorded into a maximum-size buffer; keep only the loop.
                var trimmed = new float[_loopFrames];
                Array.Copy(layer.Data, trimmed, Math.Min(layer.Data.Length, trimmed.Length));
                layer.Data = trimmed;
                RefreshSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            var snapshot = new float[_layers.Count][];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = _layers[i].Data;
            _snapshot = snapshot;
        }

        /// <summary>At the layer cap the two oldest layers are summed into one (UI thread; the sum is built outside the lock).</summary>
        private void MergeOldestIfFull()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float[] a, b;
                int length;
                lock (_lock)
                {
                    if (_layers.Count < MaxLayers) return;
                    a = _layers[0].Data;
                    b = _layers[1].Data;
                    length = (int)_loopFrames;
                }

                var merged = new float[length];
                for (int i = 0; i < length; i++)
                    merged[i] = (i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f);

                lock (_lock)
                {
                    if (_layers.Count >= 2 && ReferenceEquals(_layers[0].Data, a) && ReferenceEquals(_layers[1].Data, b))
                    {
                        _layers[0].Data = merged;
                        _layers.RemoveAt(1);
                        RefreshSnapshot();
                        return;
                    }
                }
            }
        }

        private void Notify() => Changed?.Invoke();

        private static long FloorMod(long x, long m)
        {
            long r = x % m;
            return r < 0 ? r + m : r;
        }

        /// <summary>5 ms linear fade at both ends of a pass, so the wrap never clicks.</summary>
        private static float SeamGain(int pos, int len, int seam)
        {
            float g = 1f;
            if (pos < seam) g = (pos + 1f) / seam;
            int fromEnd = len - pos;
            if (fromEnd <= seam) g = Math.Min(g, fromEnd / (float)seam);
            return g;
        }

        /// <summary>The engine's output limiter: soft knee above 0.8, strictly inside (-1, 1).</summary>
        private static float SoftLimit(float x)
        {
            float a = Math.Abs(x);
            if (a <= 0.8f) return x;
            float limited = 0.8f + 0.2f * MathF.Tanh((a - 0.8f) / 0.2f);
            return x < 0f ? -limited : limited;
        }
    }

    /// <summary>
    /// Maps capture buffers onto the engine's frame clock. The device position is jittery, so a running
    /// estimate of <c>audibleFrame − capturedSampleCount</c> is low-passed (re-syncing on big jumps) and
    /// consecutive buffers are placed contiguously.
    /// <para>
    /// Every correction of the placement drops or repeats a sample — a tiny click. Following the
    /// jitter one frame at a time did that on three buffers out of four (measured: 535 of 701), which
    /// made loops sound robotic. So the offset is fixed after a short warm-up and only nudged when
    /// the low-passed estimate has really moved away (the two devices' clocks drifting apart), and
    /// then at most a few times a second.
    /// </para>
    /// </summary>
    internal sealed class CaptureClock
    {
        /// <summary>Returned by <see cref="Stamp"/> while the clock is warming up: don't place this buffer.</summary>
        public const long NotReady = long.MinValue;

        private const double Alpha = 0.002;         // low-pass weight once the estimate has settled
        private const double ResyncFrames = 4410;   // 100 ms: the estimate is wrong, not jittery
        private const int WarmupBuffers = 8;        // measure this many buffers before placing any
        private const double Deadband = 32;         // frames (0.7 ms) the placement may be off before it is nudged
        private const int MinBuffersBetweenSlips = 8;
        private const int SettleBuffers = 200;      // ≈ 2 s after a start: corrections glide quickly
        private const int SettleStep = 6;           // frames per buffer while settling (≤ LooperEngine's stretch limit)
        private bool _correcting;

        private double _estimate;
        private long _used;
        private int _count;
        private int _sinceSlip;

        public void Reset() => _count = 0;

        /// <summary>Diagnostics: frames the current placement is away from the low-passed estimate (0 while warming up).</summary>
        public double Error => _count >= WarmupBuffers ? Volatile.Read(ref _estimate) - Volatile.Read(ref _used) : 0;

        /// <summary>
        /// Engine frame of the first sample of a buffer of <paramref name="count"/> samples that has just
        /// arrived, when <paramref name="audibleFrameAtEnd"/> was audible and <paramref name="totalAtEnd"/>
        /// samples (this buffer included) have been captured so far — or <see cref="NotReady"/> during
        /// the warm-up after a start, a reset or a jump of the output clock.
        /// </summary>
        public long Stamp(long audibleFrameAtEnd, long totalAtEnd, int count)
        {
            double measured = audibleFrameAtEnd - (double)totalAtEnd;

            if (_count == 0 || Math.Abs(measured - _estimate) > ResyncFrames)
            {
                _estimate = measured;
                _count = 1;
                return NotReady;
            }

            _count++;
            _estimate += (measured - _estimate) * Math.Max(Alpha, 1.0 / _count);
            if (_count < WarmupBuffers) return NotReady;

            if (_count == WarmupBuffers)
            {
                _used = (long)Math.Round(_estimate);
                _sinceSlip = 0;
                _correcting = false;
            }
            else
            {
                _sinceSlip++;
                double diff = _estimate - _used;
                if (_count <= SettleBuffers)
                {
                    // Right after a start the first estimate is taken while the output is still
                    // spinning up (measured: 40–170 frames off, settled within 1.5 s). Glide there a
                    // few frames per buffer — the looper stretches each buffer to fit, so there is
                    // neither a hole (a jump left 4 ms of silence in the take) nor a click.
                    if (Math.Abs(diff) >= 2) _used += (long)Math.Clamp(Math.Round(diff), -SettleStep, SettleStep);
                    _correcting = false;
                }
                else
                {
                    // Hysteresis: start correcting at the deadband, stop well inside it — otherwise an
                    // estimate resting on the edge slips for ever.
                    if (Math.Abs(diff) >= Deadband) _correcting = true;
                    else if (Math.Abs(diff) < Deadband / 4) _correcting = false;

                    if (_correcting && _sinceSlip >= MinBuffersBetweenSlips)
                    {
                        _used += diff > 0 ? 1 : -1;
                        _sinceSlip = 0;
                    }
                }
            }

            return _used + totalAtEnd - count;
        }
    }
}
