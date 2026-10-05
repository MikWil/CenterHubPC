using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CenterHubNew.MVVM.Models;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Sample-accurate drum sequencer and mixer. Renders a stereo float stream: a song state machine
    /// (count-in, intro, parts, fills, transitions, outro) drives a voice pool of drum samples and
    /// metronome clicks. <see cref="Read"/> runs on the audio thread; every other member is called
    /// from the UI thread. All mutable state is guarded by a single lock.
    /// </summary>
    public sealed class DrumMachineEngine : ISampleProvider
    {
        /// <summary>Sequencer resolution: ticks per beat.</summary>
        public const int TicksPerBeat = 48;

        private const int MaxVoices = 48;
        private const int MaxQueuedEvents = 2048;
        private const float StopFadeMs = 60f;
        private const float ChokeFadeMs = 5f;

        /// <summary>One sounding sample (drum hit or click) in the pool.</summary>
        private struct Voice
        {
            public float[]? Data;      // null = free slot
            public float[]? DataR;     // right channel of a stereo hit; null = mono (Data goes to both via the gains)
            public int Delay;          // frames of silence before the hit starts (human timing)
            public int Pos;
            public float GainL;
            public float GainR;
            public DrumVoice? Kind;    // null for clicks
            public float Fade;         // current fade multiplier (1 = full)
            public float FadeStep;     // per-frame decrement; 0 = not fading
            public long Seq;           // start order, used to steal the oldest
            public bool CountIn;       // a looper count-in click (can be cancelled)
        }

        private readonly object _gate = new();
        private readonly int _sampleRate;
        private readonly Voice[] _voices = new Voice[MaxVoices];
        private readonly ConcurrentQueue<DrumEngineEvent> _events = new();
        private readonly Random _rng = new(1);

        // Per drum: which recorded hit played last, so the next one is always a different one (-1 = none yet).
        private readonly int[] _lastLayer = NewLayerMemory();

        /// <summary>Longest start delay of the human-timing feel, in seconds, at Humanize = 1.</summary>
        private const double HumanDelaySeconds = 0.080;

        private DrumKit _kit;
        private long _voiceSeq;
        private LooperEngine? _looper;      // mixed into Read after the master stage; null = no looper

        // ── settings ──
        private double _bpm = 120;
        private float _masterVolume = 0.8f;
        private float _drumVolume = 1f;
        private float _clickVolume = 1f;
        private float _humanize;
        private bool _clickEnabled = true;
        private MetronomeSound _clickSound = MetronomeSound.Clock;
        private ClickSubdivision _subdivision = ClickSubdivision.None;
        private int _clickBeats = 4;
        private BeatAccent[] _accents = { BeatAccent.Accent };
        private bool _countInEnabled;
        private bool _introEnabled = true;
        private int _autoFillEveryBars;
        private int _gapPlay;
        private int _gapMute;

        // ── style ──
        private DrumStyle? _style;          // what the user selected
        private DrumStyle? _activeStyle;    // _style if it is playable (has parts with grooves), else null = click-only
        private DrumStyle? _pendingStyle;
        private bool _hasPendingStyle;
        private int[] _fillCursor = Array.Empty<int>();

        // ── transport / timeline ──
        private bool _playing;
        private double _framesToNextTick;
        private long _framesRendered;
        private int _tick;
        private int _ticksInBar = 4 * TicksPerBeat;
        private int _ticksPerStep = TicksPerBeat;
        private int _stepsInBar = 4;
        private int _beatsInBar = 4;

        // ── song state ──
        private DrumSection _section = DrumSection.Stopped;
        private int _partIndex;
        private int _mainBarIndex;
        private int _phraseBar;
        private int _barNumber;
        private DrumBar? _pattern;
        private bool _gapMuted;
        private bool _fillNextBar;
        private bool _transitionNextBar;
        private bool _outroRequested;
        private bool _crashOnNextBar;
        private bool _endingPending;

        /// <summary>Creates an engine that plays <paramref name="kit"/> at <paramref name="sampleRate"/>.</summary>
        public DrumMachineEngine(DrumKit kit, int sampleRate = 44100)
        {
            _kit = kit ?? throw new ArgumentNullException(nameof(kit));
            _sampleRate = sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        /// <summary>Stereo 32-bit float at the engine sample rate.</summary>
        public WaveFormat WaveFormat { get; }

        // ───────────────────────── settings ─────────────────────────

        /// <summary>Tempo in beats per minute (30..300). Read at every tick, so changes are smooth.</summary>
        public double Bpm
        {
            get { lock (_gate) return _bpm; }
            set
            {
                if (double.IsNaN(value)) return;
                lock (_gate) _bpm = Math.Clamp(value, 30.0, 300.0);
            }
        }

        /// <summary>Master output level, 0..1.</summary>
        public float MasterVolume
        {
            get { lock (_gate) return _masterVolume; }
            set { lock (_gate) _masterVolume = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>Level of the drum voices, 0..1.</summary>
        public float DrumVolume
        {
            get { lock (_gate) return _drumVolume; }
            set { lock (_gate) _drumVolume = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>Level of the metronome click, 0..1.</summary>
        public float ClickVolume
        {
            get { lock (_gate) return _clickVolume; }
            set { lock (_gate) _clickVolume = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>Random velocity variation applied to pattern hits, 0..0.3.</summary>
        public float Humanize
        {
            get { lock (_gate) return _humanize; }
            set { lock (_gate) _humanize = Math.Clamp(value, 0f, 0.3f); }
        }

        /// <summary>
        /// Attaches (or, with null, detaches) the guitar looper. It is told about bar starts and stops and
        /// mixes itself into <see cref="Read"/>; with no looper, or an empty one, the output is unchanged.
        /// </summary>
        public void AttachLooper(LooperEngine? looper)
        {
            lock (_gate) _looper = looper;
        }

        /// <summary>Swaps the drum kit. Already-sounding voices keep their old samples.</summary>
        public void SetKit(DrumKit kit)
        {
            if (kit is null) throw new ArgumentNullException(nameof(kit));
            lock (_gate) _kit = kit;
        }

        /// <summary>The selected style (the pending one if a change is waiting for the bar boundary); null = click-only.</summary>
        public DrumStyle? Style
        {
            get { lock (_gate) return _hasPendingStyle ? _pendingStyle : _style; }
        }

        /// <summary>Selects a style (null = click-only). Stopped: immediately. Playing: at the next bar boundary.</summary>
        public void SetStyle(DrumStyle? style)
        {
            lock (_gate)
            {
                // Re-selecting the style that is already playing (or already queued) must not
                // restart the song at the bar line.
                if (_playing && ReferenceEquals(style, _hasPendingStyle ? _pendingStyle : _style)) return;

                if (_playing)
                {
                    _pendingStyle = style;
                    _hasPendingStyle = true;
                }
                else
                {
                    ApplyStyle(style);
                }
            }
        }

        /// <summary>Whether the metronome click sounds.</summary>
        public bool ClickEnabled
        {
            get { lock (_gate) return _clickEnabled; }
            set { lock (_gate) _clickEnabled = value; }
        }

        /// <summary>The click timbre.</summary>
        public MetronomeSound ClickSound
        {
            get { lock (_gate) return _clickSound; }
            set { lock (_gate) _clickSound = value; }
        }

        /// <summary>Extra clicks between the beats.</summary>
        public ClickSubdivision Subdivision
        {
            get { lock (_gate) return _subdivision; }
            set { lock (_gate) _subdivision = value; }
        }

        /// <summary>Beats per bar in click-only mode (1..12). Applies at the next bar.</summary>
        public void SetClickBeats(int beats)
        {
            lock (_gate) _clickBeats = Math.Clamp(beats, 1, 12);
        }

        /// <summary>Per-beat click accents; beats beyond the list are Normal.</summary>
        public void SetAccents(IReadOnlyList<BeatAccent> accents)
        {
            var copy = new BeatAccent[accents?.Count ?? 0];
            for (int i = 0; i < copy.Length; i++) copy[i] = accents![i];
            lock (_gate) _accents = copy;
        }

        /// <summary>Play one count-in bar of sticks before the groove starts (drum styles only).</summary>
        public bool CountInEnabled
        {
            get { lock (_gate) return _countInEnabled; }
            set { lock (_gate) _countInEnabled = value; }
        }

        /// <summary>Play the style's intro bar when starting.</summary>
        public bool IntroEnabled
        {
            get { lock (_gate) return _introEnabled; }
            set { lock (_gate) _introEnabled = value; }
        }

        /// <summary>0 = off; N = every Nth bar of a phrase is a fill.</summary>
        public int AutoFillEveryBars
        {
            get { lock (_gate) return _autoFillEveryBars; }
            set { lock (_gate) _autoFillEveryBars = Math.Max(0, value); }
        }

        /// <summary>Gap trainer: play <paramref name="playBars"/> bars, then mute <paramref name="muteBars"/>. muteBars &lt;= 0 turns it off.</summary>
        public void SetGap(int playBars, int muteBars)
        {
            lock (_gate)
            {
                if (muteBars <= 0 || playBars < 0)
                {
                    _gapPlay = 0;
                    _gapMute = 0;
                }
                else
                {
                    _gapPlay = playBars;
                    _gapMute = muteBars;
                }
            }
        }

        // ───────────────────────── transport ─────────────────────────

        /// <summary>Starts (or restarts) from the top. The first tick fires on the next rendered frame.</summary>
        public void Start()
        {
            lock (_gate)
            {
                // Voices are left ringing on purpose; only the song state is reset.
                if (_hasPendingStyle) ApplyStyle(_pendingStyle);
                else Array.Clear(_fillCursor, 0, _fillCursor.Length);

                _fillNextBar = false;
                _transitionNextBar = false;
                _outroRequested = false;
                _crashOnNextBar = false;
                _endingPending = false;
                _partIndex = 0;
                _mainBarIndex = 0;
                _phraseBar = 0;
                _barNumber = 0;
                _pattern = null;
                _gapMuted = false;
                Array.Fill(_lastLayer, -1);

                var style = _activeStyle;
                if (style == null)
                {
                    _section = DrumSection.Main;
                    _barNumber = 1;
                }
                else if (_countInEnabled)
                {
                    _section = DrumSection.CountIn;
                }
                else if (_introEnabled && style.Intro != null)
                {
                    _section = DrumSection.Intro;
                    _pattern = style.Intro;
                }
                else
                {
                    EnterMain(crash: false);
                    _barNumber = 1;
                }

                SetupBarGeometry();
                _gapMuted = ComputeGapMute();
                _framesToNextTick = 0;
                _playing = true;
                _looper?.OnEngineStart(_framesRendered);
            }
        }

        /// <summary>Stops immediately; sounding voices get a 60 ms fade-out.</summary>
        public void Stop()
        {
            lock (_gate) StopCore(_framesRendered, fadeVoices: true);
        }

        /// <summary>Plays a fill: right now if at least a beat of the bar remains, else next bar.</summary>
        public void RequestFill()
        {
            lock (_gate)
            {
                var style = _activeStyle;
                if (!_playing || style == null || _section != DrumSection.Main) return;
                if (_ticksInBar - _tick >= TicksPerBeat)
                {
                    _pattern = NextFill(style.Parts[_partIndex]);
                    _section = DrumSection.Fill;
                    _gapMuted = false;
                }
                else
                {
                    _fillNextBar = true;
                }
            }
        }

        /// <summary>Moves on to the next part through its transition fill.</summary>
        public void RequestNextPart()
        {
            lock (_gate)
            {
                var style = _activeStyle;
                if (!_playing || style == null) return;
                if (_section != DrumSection.Main && _section != DrumSection.Fill) return;
                if (_ticksInBar - _tick >= TicksPerBeat)
                {
                    var part = style.Parts[_partIndex];
                    _pattern = part.Transition ?? NextFill(part);
                    _section = DrumSection.Transition;
                    _gapMuted = false;
                }
                else
                {
                    _transitionNextBar = true;
                }
            }
        }

        /// <summary>Ends the song at the next bar boundary through the outro (click-only: stops at once).</summary>
        public void RequestOutro()
        {
            lock (_gate)
            {
                if (!_playing) return;
                if (_activeStyle == null) StopCore(_framesRendered, fadeVoices: true);
                else _outroRequested = true;
            }
        }

        /// <summary>Crash and kick right now.</summary>
        public void TriggerAccentHit()
        {
            lock (_gate)
            {
                TriggerDrum(DrumVoice.Crash, 1f, humanize: false);
                TriggerDrum(DrumVoice.Kick, 1f, humanize: false);
            }
        }

        /// <summary>Plays one click so the user can audition a timbre (works while stopped).</summary>
        public void PreviewClick(MetronomeSound sound, bool accent)
        {
            lock (_gate)
            {
                float gain = ClickGain(accent ? 1f : 0.8f);
                AddVoice(_kit.GetClick(sound, accent), gain, gain, null);
            }
        }

        /// <summary>
        /// Schedules <paramref name="count"/> accented clicks <paramref name="intervalFrames"/> apart as
        /// delayed voices, sample-exact, independent of the sequencer (works while stopped; used to calibrate
        /// the looper's latency). Returns the engine frame of the first click. The click volume has a floor
        /// of 0.5 so the clicks are always audible.
        /// </summary>
        public long ScheduleClicks(MetronomeSound sound, int count, int intervalFrames)
        {
            lock (_gate)
            {
                float gain = 0.9f * Math.Max(_clickVolume, 0.5f);
                var click = _kit.GetClick(sound, true);
                for (int i = 0; i < count; i++)
                    AddVoice(click, gain, gain, null, null, i * intervalFrames);
                return _framesRendered;
            }
        }

        /// <summary>Beats per bar of what plays: the selected style's, or the click-only setting.</summary>
        public int BeatsPerBar
        {
            get { lock (_gate) return Math.Max(1, _activeStyle?.Beats ?? _clickBeats); }
        }

        /// <summary>
        /// Schedules a count-in for the looper: <paramref name="beats"/> clicks, <paramref name="beatFrames"/>
        /// apart (the exact, fractional spacing — each click is placed at round(i × beatFrames), so there is
        /// no drift), accent on beat 1 of every <paramref name="beatsPerBar"/>. Independent of the sequencer
        /// (works while stopped). Returns the engine frame of the first click. The click volume has a floor
        /// of 0.5 so the clicks are always audible.
        /// </summary>
        public long ScheduleCountIn(int beats, int beatsPerBar, double beatFrames)
        {
            lock (_gate)
            {
                float volume = Math.Max(_clickVolume, 0.5f);
                beatsPerBar = Math.Max(1, beatsPerBar);
                for (int i = 0; i < beats; i++)
                {
                    bool accent = i % beatsPerBar == 0;
                    float gain = MathF.Pow(accent ? 1f : 0.8f, 1.5f) * 0.9f * volume;
                    AddVoice(_kit.GetClick(_clickSound, accent), gain, gain, null, null, (int)Math.Round(i * beatFrames), countIn: true);
                }
                return _framesRendered;
            }
        }

        /// <summary>Silences the count-in clicks that have not sounded yet (and fades the one that is).</summary>
        public void CancelCountIn()
        {
            lock (_gate)
            {
                for (int i = 0; i < _voices.Length; i++)
                {
                    if (_voices[i].Data == null || !_voices[i].CountIn) continue;
                    if (_voices[i].Delay > 0) _voices[i].Data = null;
                    else FadeVoice(ref _voices[i], ChokeFadeMs);
                }
            }
        }

        /// <summary>Plays one drum piece so the user can audition the kit (works while stopped).</summary>
        public void PreviewVoice(DrumVoice voice)
        {
            lock (_gate) TriggerDrum(voice, 1f, humanize: false);
        }

        // ───────────────────────── state ─────────────────────────

        /// <summary>True while the sequencer is running.</summary>
        public bool IsPlaying
        {
            get { lock (_gate) return _playing; }
        }

        /// <summary>True once stopped and every voice has finished sounding.</summary>
        public bool IsIdle
        {
            get
            {
                lock (_gate)
                {
                    if (_playing) return false;
                    if (_looper != null && _looper.IsBusy) return false;
                    for (int i = 0; i < _voices.Length; i++)
                        if (_voices[i].Data != null) return false;
                    return true;
                }
            }
        }

        /// <summary>Total frames (sample pairs) produced by <see cref="Read"/> so far.</summary>
        public long FramesRendered
        {
            get { lock (_gate) return _framesRendered; }
        }

        /// <summary>The song section being played.</summary>
        public DrumSection Section
        {
            get { lock (_gate) return _section; }
        }

        /// <summary>Index of the current part.</summary>
        public int PartIndex
        {
            get { lock (_gate) return _partIndex; }
        }

        /// <summary>Looks at the oldest queued position event without removing it.</summary>
        public bool TryPeekEvent(out DrumEngineEvent e) => _events.TryPeek(out e);

        /// <summary>Removes and returns the oldest queued position event.</summary>
        public bool TryDequeueEvent(out DrumEngineEvent e) => _events.TryDequeue(out e);

        // ───────────────────────── rendering ─────────────────────────

        /// <summary>Renders <paramref name="count"/> floats (interleaved stereo). Always fills the whole request.</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                Array.Clear(buffer, offset, count);
                int frames = count / 2;
                int done = 0;
                while (done < frames)
                {
                    if (_playing && _framesToNextTick <= 0)
                    {
                        ProcessTick(_framesRendered + done);
                        // Read the tempo per tick so BPM changes are smooth; the fractional remainder carries over.
                        _framesToNextTick += _sampleRate * 60.0 / _bpm / TicksPerBeat;
                        continue;
                    }

                    int chunk = _playing
                        ? Math.Min(frames - done, (int)Math.Ceiling(_framesToNextTick))
                        : frames - done;
                    MixVoices(buffer, offset + done * 2, chunk);
                    done += chunk;
                    if (_playing) _framesToNextTick -= chunk;
                }

                float master = _masterVolume;
                int end = offset + frames * 2;
                for (int i = offset; i < end; i++)
                {
                    float x = buffer[i] * master;
                    float a = Math.Abs(x);
                    if (a > 0.8f)
                    {
                        // Soft knee above 0.8 keeps the output strictly inside (-1, 1).
                        float limited = 0.8f + 0.2f * MathF.Tanh((a - 0.8f) / 0.2f);
                        x = x < 0f ? -limited : limited;
                    }
                    buffer[i] = x;
                }

                _looper?.Mix(buffer, offset, frames, _framesRendered, master);

                _framesRendered += frames;
                return count;
            }
        }

        private void MixVoices(float[] buffer, int start, int frames)
        {
            for (int k = 0; k < _voices.Length; k++)
            {
                ref Voice v = ref _voices[k];
                var data = v.Data;
                if (data == null) continue;

                // A delayed hit first lets output frames go by in silence.
                int skip = 0;
                if (v.Delay > 0)
                {
                    skip = Math.Min(v.Delay, frames);
                    v.Delay -= skip;
                }

                var dataR = v.DataR;
                int n = Math.Min(frames - skip, data.Length - v.Pos);
                int o = start + skip * 2;
                int i = 0;
                bool finished = false;
                for (; i < n; i++)
                {
                    float s = data[v.Pos + i];
                    float r = dataR != null ? dataR[v.Pos + i] : s;
                    if (v.FadeStep > 0f)
                    {
                        v.Fade -= v.FadeStep;
                        if (v.Fade <= 0f)
                        {
                            finished = true;
                            break;
                        }
                        s *= v.Fade;
                        r *= v.Fade;
                    }
                    buffer[o] += s * v.GainL;
                    buffer[o + 1] += r * v.GainR;
                    o += 2;
                }

                v.Pos += i;
                if (finished || v.Pos >= data.Length) v.Data = null;
            }
        }

        // ───────────────────────── voices ─────────────────────────

        private float ClickGain(float velocity) =>
            MathF.Pow(Math.Clamp(velocity, 0f, 1f), 1.5f) * 0.9f * _clickVolume;

        private void TriggerDrum(DrumVoice voice, float velocity, bool humanize)
        {
            if (humanize && _humanize > 0f)
                velocity *= 1f + ((float)_rng.NextDouble() * 2f - 1f) * _humanize;
            velocity = Math.Clamp(velocity, 0f, 1f);

            // A closed or pedal hat cuts any open hat that is still ringing.
            if (voice == DrumVoice.ClosedHat || voice == DrumVoice.PedalHat)
            {
                for (int i = 0; i < _voices.Length; i++)
                    if (_voices[i].Data != null && _voices[i].Kind == DrumVoice.OpenHat)
                        FadeVoice(ref _voices[i], ChokeFadeMs);
            }

            // A real drummer is never sample-exact: pattern hits start up to a few ms late.
            // Drawn after the velocity so the Humanize == 0 stream of random numbers is unchanged.
            int delay = 0;
            if (humanize && _humanize > 0f)
                delay = (int)(_rng.NextDouble() * _humanize * HumanDelaySeconds * _sampleRate);

            if (_kit.IsSampled(voice))
            {
                TriggerRecorded(voice, velocity, delay);
                return;
            }

            // 0.7 leaves headroom for kick + snare + crash landing together, so the limiter
            // only ever shaves the loudest downbeats.
            float gain = MathF.Pow(velocity, 1.5f) * _drumVolume * 0.7f;
            if (gain <= 0f) return;

            // Equal-power pan; the sqrt(2) makes the centre unity on both channels.
            float angle = (_kit.GetPan(voice) + 1f) * (MathF.PI / 4f);
            float gl = gain * MathF.Cos(angle) * MathF.Sqrt(2f);
            float gr = gain * MathF.Sin(angle) * MathF.Sqrt(2f);
            AddVoice(_kit.GetSample(voice), gl, gr, voice, null, delay);
        }

        /// <summary>
        /// Plays the recorded hit that fits <paramref name="velocity"/>. The recordings carry their own
        /// dynamics (a soft hit is quieter and sounds different), so the velocity picks a hit instead of
        /// scaling one; neighbouring hits alternate so a repeated note never sounds identical.
        /// </summary>
        private void TriggerRecorded(DrumVoice voice, float velocity, int delay)
        {
            if (velocity <= 0f || _drumVolume <= 0f) return;

            var layers = _kit.GetLayers(voice);
            int n = layers.Count;

            // Ideal (fractional) position in the soft → loud list; any hit within one step of it will do.
            float t = velocity * (n - 1);
            const float eps = 1e-4f;
            int first = Math.Max(0, (int)MathF.Ceiling(t - 1f - eps));
            int last = Math.Min(n - 1, (int)MathF.Floor(t + 1f + eps));

            int count = last - first + 1;
            int previous = _lastLayer[(int)voice];
            bool skipPrevious = count > 1 && previous >= first && previous <= last;
            int pool = skipPrevious ? count - 1 : count;
            int index = first + (pool > 1 ? _rng.Next(pool) : 0);
            if (skipPrevious && index >= previous) index++;
            _lastLayer[(int)voice] = index;

            var chosen = layers[index];

            // Match the level the ideal position would have had, so picking a neighbour doesn't change the loudness.
            int lo = (int)MathF.Floor(t);
            int hi = Math.Min(n - 1, lo + 1);
            float desired = layers[lo].Loudness + (layers[hi].Loudness - layers[lo].Loudness) * (t - lo);
            float gain = _drumVolume * 0.7f * Math.Clamp(desired / Math.Max(chosen.Loudness, 1e-4f), 0.5f, 2f);

            if (chosen.Right != null)
            {
                // The recording has its own stereo image.
                AddVoice(chosen.Left, gain, gain, voice, chosen.Right, delay);
            }
            else
            {
                float angle = (_kit.GetPan(voice) + 1f) * (MathF.PI / 4f);
                AddVoice(chosen.Left, gain * MathF.Cos(angle) * MathF.Sqrt(2f), gain * MathF.Sin(angle) * MathF.Sqrt(2f),
                         voice, null, delay);
            }
        }

        private static int[] NewLayerMemory()
        {
            var memory = new int[Enum.GetValues<DrumVoice>().Length];
            Array.Fill(memory, -1);
            return memory;
        }

        private void TriggerClick(bool accent, float velocity)
        {
            float gain = ClickGain(velocity);
            if (gain <= 0f) return;
            AddVoice(_kit.GetClick(_clickSound, accent), gain, gain, null);
        }

        private void AddVoice(float[] data, float gainL, float gainR, DrumVoice? kind, float[]? dataR = null, int delay = 0, bool countIn = false)
        {
            int slot = -1;
            long oldest = long.MaxValue;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Data == null)
                {
                    slot = i;
                    break;
                }
                if (_voices[i].Seq < oldest)
                {
                    oldest = _voices[i].Seq;
                    slot = i; // pool full: remember the oldest to steal
                }
            }

            _voices[slot] = new Voice
            {
                Data = data,
                DataR = dataR,
                Delay = delay,
                Pos = 0,
                GainL = gainL,
                GainR = gainR,
                Kind = kind,
                Fade = 1f,
                FadeStep = 0f,
                Seq = ++_voiceSeq,
                CountIn = countIn,
            };
        }

        private void FadeVoice(ref Voice v, float ms)
        {
            float step = 1f / Math.Max(1f, _sampleRate * ms / 1000f);
            if (step > v.FadeStep) v.FadeStep = step;
        }

        // ───────────────────────── sequencer ─────────────────────────

        private void ProcessTick(long frame)
        {
            if (_endingPending)
            {
                TriggerDrum(DrumVoice.Crash, 1f, humanize: false);
                TriggerDrum(DrumVoice.Kick, 1f, humanize: false);
                StopCore(frame, fadeVoices: false);
                return;
            }

            int tick = _tick;
            // A real bar starts (the count-in does not count) — the looper is slaved to these.
            if (tick == 0 && _section != DrumSection.CountIn) _looper?.OnBarStart(frame, _bpm, _beatsInBar);
            bool onGrid = tick % _ticksPerStep == 0;
            bool beatStart = tick % TicksPerBeat == 0;
            var pattern = _pattern;

            if (!_gapMuted)
            {
                if (onGrid && pattern != null)
                {
                    int step = tick / _ticksPerStep;
                    if (step < pattern.Steps)
                    {
                        var hits = pattern.HitsAt(step);
                        for (int i = 0; i < hits.Count; i++)
                            TriggerDrum(hits[i].Voice, hits[i].Velocity, humanize: true);
                    }
                }

                if (tick == 0 && _crashOnNextBar)
                {
                    bool hasCrash = pattern != null && pattern.Steps > 0 && pattern.VelocityAt(DrumVoice.Crash, 0) > 0f;
                    if (!hasCrash) TriggerDrum(DrumVoice.Crash, 0.9f, humanize: true);
                }

                if (_section == DrumSection.CountIn)
                {
                    if (beatStart) TriggerDrum(DrumVoice.Sticks, tick == 0 ? 1f : 0.85f, humanize: false);
                }
                else if (_clickEnabled)
                {
                    ClickLayer(tick, beatStart);
                }
            }

            if (tick == 0) _crashOnNextBar = false;

            if (onGrid)
            {
                Enqueue(new DrumEngineEvent(
                    frame,
                    tick / _ticksPerStep,
                    _stepsInBar,
                    tick / TicksPerBeat + 1,
                    _beatsInBar,
                    beatStart,
                    _barNumber,
                    _section,
                    _partIndex,
                    pattern,
                    _gapMuted));
            }

            _tick++;
            if (_tick >= _ticksInBar) AdvanceBar(frame);
        }

        private void ClickLayer(int tick, bool beatStart)
        {
            if (beatStart)
            {
                int beat = tick / TicksPerBeat;
                var accent = beat < _accents.Length ? _accents[beat] : BeatAccent.Normal;
                if (accent == BeatAccent.Accent) TriggerClick(true, 1f);
                else if (accent == BeatAccent.Normal) TriggerClick(false, 0.8f);
                // Mute: nothing on the beat, but subdivision clicks below still sound.
                return;
            }

            int t = tick % TicksPerBeat;
            bool sub = _subdivision switch
            {
                ClickSubdivision.Eighths => t == 24,
                ClickSubdivision.Triplets => t == 16 || t == 32,
                ClickSubdivision.Sixteenths => t == 12 || t == 24 || t == 36,
                ClickSubdivision.Swing => t == 32,
                _ => false,
            };
            if (sub) TriggerClick(false, 0.4f);
        }

        /// <summary>Called when a bar finishes: picks the next bar and prepares its timing.</summary>
        private void AdvanceBar(long frame)
        {
            BuildNextBar(frame);
            if (!_playing) return;
            SetupBarGeometry();
            _gapMuted = ComputeGapMute();
        }

        private void BuildNextBar(long frame)
        {
            var finished = _section;

            // 1. A style change waits for the bar boundary and restarts the song in the new style.
            if (_hasPendingStyle)
            {
                ApplyStyle(_pendingStyle);
                _phraseBar = 0;
                _fillNextBar = false;
                _transitionNextBar = false;
                if (_activeStyle == null)
                {
                    _section = DrumSection.Main;
                    _pattern = null;
                }
                else
                {
                    EnterMain(crash: true);
                }
                _barNumber++;
                return;
            }

            var style = _activeStyle;

            // 2. Click-only mode.
            if (style == null)
            {
                if (_outroRequested)
                {
                    StopCore(frame, fadeVoices: true);
                    return;
                }
                _barNumber++;
                _section = DrumSection.Main;
                _pattern = null;
                return;
            }

            // 3. Outro requested: play it (or just the ending hit when the style has none).
            if (_outroRequested && finished != DrumSection.Outro)
            {
                _outroRequested = false;
                if (style.Outro != null)
                {
                    _section = DrumSection.Outro;
                    _pattern = style.Outro;
                    _barNumber++;
                }
                else
                {
                    _endingPending = true;
                    _pattern = null;
                }
                return;
            }

            // 4. Follow the song.
            switch (finished)
            {
                case DrumSection.CountIn:
                    if (_introEnabled && style.Intro != null)
                    {
                        _section = DrumSection.Intro;
                        _pattern = style.Intro;
                    }
                    else
                    {
                        EnterMain(crash: false);
                        _barNumber = 1;
                    }
                    break;

                case DrumSection.Intro:
                    EnterMain(crash: true);
                    _barNumber = 1;
                    break;

                case DrumSection.Outro:
                    _endingPending = true;
                    _pattern = null;
                    break;

                case DrumSection.Transition:
                    _partIndex = (_partIndex + 1) % style.Parts.Count;
                    _mainBarIndex = 0;
                    _phraseBar = 0;
                    _crashOnNextBar = true;
                    ChooseNextBar(style);
                    break;

                case DrumSection.Fill:
                    _mainBarIndex = (_mainBarIndex + 1) % style.Parts[_partIndex].Main.Count;
                    _phraseBar = 0;
                    _crashOnNextBar = true;
                    ChooseNextBar(style);
                    break;

                default: // Main
                    _mainBarIndex = (_mainBarIndex + 1) % style.Parts[_partIndex].Main.Count;
                    _phraseBar++;
                    ChooseNextBar(style);
                    break;
            }
        }

        private void ChooseNextBar(DrumStyle style)
        {
            _barNumber++;
            var part = style.Parts[_partIndex];

            if (_transitionNextBar)
            {
                _section = DrumSection.Transition;
                _pattern = part.Transition ?? NextFill(part);
                _transitionNextBar = false;
            }
            else if (_fillNextBar || (_autoFillEveryBars > 0 && _phraseBar >= _autoFillEveryBars - 1))
            {
                _section = DrumSection.Fill;
                _pattern = NextFill(part);
                _fillNextBar = false;
            }
            else
            {
                _section = DrumSection.Main;
                _pattern = part.Main[_mainBarIndex % part.Main.Count];
            }
        }

        /// <summary>Starts the first groove bar of part 0 (the caller sets the bar number). Needs a playable style.</summary>
        private void EnterMain(bool crash)
        {
            var part = _activeStyle!.Parts[0];
            _partIndex = 0;
            _mainBarIndex = 0;
            _phraseBar = 0;
            _crashOnNextBar = crash;
            if (_autoFillEveryBars == 1)
            {
                _section = DrumSection.Fill;
                _pattern = NextFill(part);
            }
            else
            {
                _section = DrumSection.Main;
                _pattern = part.Main[0];
            }
        }

        /// <summary>Round-robin fill of the current part; falls back to the current groove bar when the part has none.</summary>
        private DrumBar NextFill(DrumPart part)
        {
            var fills = part.Fills;
            if (fills.Count == 0 || _partIndex >= _fillCursor.Length)
                return part.Main[Math.Min(_mainBarIndex, part.Main.Count - 1)];

            int idx = _fillCursor[_partIndex] % fills.Count;
            _fillCursor[_partIndex] = (idx + 1) % fills.Count;
            return fills[idx];
        }

        /// <summary>Gap trainer: only groove bars (or click-only bars) can be muted.</summary>
        private bool ComputeGapMute()
        {
            if (_gapMute <= 0 || _section != DrumSection.Main || _endingPending || _barNumber < 1) return false;
            return (_barNumber - 1) % (_gapPlay + _gapMute) >= _gapPlay;
        }

        /// <summary>Recomputes bar length and step grid for the bar about to start and rewinds the tick.</summary>
        private void SetupBarGeometry()
        {
            var style = _activeStyle;
            _beatsInBar = Math.Max(1, style?.Beats ?? _clickBeats);
            _ticksInBar = _beatsInBar * TicksPerBeat;
            if (style != null)
            {
                _ticksPerStep = Math.Max(1, TicksPerBeat / Math.Clamp(style.StepsPerBeat, 1, TicksPerBeat));
                _stepsInBar = Math.Max(1, _ticksInBar / _ticksPerStep);
            }
            else
            {
                _ticksPerStep = TicksPerBeat;
                _stepsInBar = _beatsInBar;
            }
            _tick = 0;
        }

        private void ApplyStyle(DrumStyle? style)
        {
            _style = style;
            _activeStyle = Playable(style);
            _pendingStyle = null;
            _hasPendingStyle = false;
            _fillCursor = new int[_activeStyle?.Parts.Count ?? 0];
        }

        /// <summary>A style needs at least one part and every part needs a groove; otherwise it is treated as click-only.</summary>
        private static DrumStyle? Playable(DrumStyle? style)
        {
            if (style == null || style.Parts.Count == 0) return null;
            for (int i = 0; i < style.Parts.Count; i++)
                if (style.Parts[i].Main.Count == 0) return null;
            return style;
        }

        private void StopCore(long frame, bool fadeVoices)
        {
            if (!_playing) return;

            _playing = false;
            _looper?.OnEngineStop(frame);
            _section = DrumSection.Stopped;
            _fillNextBar = false;
            _transitionNextBar = false;
            _outroRequested = false;
            _crashOnNextBar = false;
            _endingPending = false;
            _pattern = null;

            if (fadeVoices)
            {
                for (int i = 0; i < _voices.Length; i++)
                    if (_voices[i].Data != null) FadeVoice(ref _voices[i], StopFadeMs);
            }

            Enqueue(new DrumEngineEvent(
                frame, 0, _stepsInBar, 1, _beatsInBar, true, _barNumber,
                DrumSection.Stopped, _partIndex, null, false));
        }

        private void Enqueue(DrumEngineEvent e)
        {
            _events.Enqueue(e);
            // Nobody may be listening (tests, stopped UI): keep only the newest events.
            while (_events.Count > MaxQueuedEvents) _events.TryDequeue(out _);
        }
    }
}
