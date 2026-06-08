using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>The selectable click timbres for the metronome.</summary>
    public enum MetronomeSound
    {
        Clock,
        WoodBlock,
        Beep,
        Click,
        Cowbell,
        Rim,
    }

    /// <summary>
    /// Synthesises a small in-memory click sample for each <see cref="MetronomeSound"/>
    /// (a normal "tick" plus a brighter/heavier accent for beat 1). One shared
    /// WaveOutEvent + BufferedWaveProvider so every click goes through the same
    /// low-latency pipeline — no per-tick device allocation.
    /// </summary>
    public sealed class MetronomeService : IDisposable
    {
        private readonly WaveFormat _format = new(44100, 16, 1); // 44.1 kHz mono 16-bit

        // Pre-rendered (tick, accent) PCM pair for every sound.
        private readonly Dictionary<MetronomeSound, (byte[] tick, byte[] accent)> _samples = new();

        private WaveOutEvent? _output;
        private BufferedWaveProvider? _buffer;

        public MetronomeService()
        {
            // ── Clock — high, light mechanical tick; deeper tock on beat 1 ──
            _samples[MetronomeSound.Clock] = (
                GenerateClockTick(bodyHz: 1800, clickHz: 4000, durationSec: 0.028, amplitude: 0.70, bodyDecay: 280, clickDecay: 600),
                GenerateClockTick(bodyHz:  900, clickHz: 2800, durationSec: 0.040, amplitude: 0.95, bodyDecay: 180, clickDecay: 400));

            // ── Wood block — warm, woody knock with a couple of harmonics ──
            _samples[MetronomeSound.WoodBlock] = (
                GenerateDecayTone(new[] { 1200.0, 2400.0, 3600.0 }, new[] { 1.0, 0.45, 0.20 }, 0.045, 0.78, 95),
                GenerateDecayTone(new[] {  820.0, 1640.0, 2460.0 }, new[] { 1.0, 0.45, 0.20 }, 0.055, 0.98, 75));

            // ── Beep — clean digital metronome tone (flat envelope) ──
            _samples[MetronomeSound.Beep] = (
                GenerateSustainTone(1320, 0.045, 0.55),
                GenerateSustainTone( 880, 0.060, 0.78));

            // ── Click — short, snappy sine click ──
            _samples[MetronomeSound.Click] = (
                GenerateDecayTone(new[] { 2000.0 }, new[] { 1.0 }, 0.022, 0.60, 360),
                GenerateDecayTone(new[] { 1500.0 }, new[] { 1.0 }, 0.030, 0.88, 260));

            // ── Cowbell — metallic 808-style pair of detuned square tones ──
            _samples[MetronomeSound.Cowbell] = (
                GenerateSquarePair(540, 800, 0.110, 0.50, 30),
                GenerateSquarePair(480, 720, 0.140, 0.65, 26));

            // ── Rim — tight, bright rimshot-like blip ──
            _samples[MetronomeSound.Rim] = (
                GenerateDecayTone(new[] { 2400.0, 3200.0 }, new[] { 1.0, 0.7 }, 0.018, 0.65, 520),
                GenerateDecayTone(new[] { 1700.0, 2550.0 }, new[] { 1.0, 0.7 }, 0.024, 0.90, 380));
        }

        /// <summary>Start the output stream if not already running.</summary>
        public void Prime()
        {
            if (_output is not null) return;
            _buffer = new BufferedWaveProvider(_format)
            {
                BufferLength = _format.AverageBytesPerSecond, // 1 s ring buffer
                DiscardOnBufferOverflow = true,
            };
            _output = new WaveOutEvent { DesiredLatency = 60 };
            _output.Init(_buffer);
            _output.Play();
        }

        /// <summary>Queue one click. accent=true plays the brighter beat-1 sound.</summary>
        public void Tick(bool accent, float volume = 1.0f, MetronomeSound sound = MetronomeSound.Clock)
        {
            Prime();
            var pair = _samples.TryGetValue(sound, out var p) ? p : _samples[MetronomeSound.Clock];
            var src = accent ? pair.accent : pair.tick;

            if (Math.Abs(volume - 1.0f) < 0.005f)
            {
                _buffer?.AddSamples(src, 0, src.Length);
                return;
            }
            // Scale amplitude by volume without allocating a cached buffer
            var scaled = new byte[src.Length];
            for (int i = 0; i < src.Length - 1; i += 2)
            {
                short s = (short)(BitConverter.ToInt16(src, i) * volume);
                scaled[i]     = (byte)(s & 0xFF);
                scaled[i + 1] = (byte)((s >> 8) & 0xFF);
            }
            _buffer?.AddSamples(scaled, 0, scaled.Length);
        }

        /// <summary>Stop and dispose the output (keeps the cached PCM).</summary>
        public void Stop()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;
            _buffer = null;
        }

        // ─────────────────── Tone synthesis ───────────────────

        /// <summary>
        /// Clock tick: a high "click" transient (fast decay) layered over a
        /// mid-frequency resonant "body", mimicking a mechanical escapement.
        /// </summary>
        private byte[] GenerateClockTick(
            double bodyHz, double clickHz, double durationSec, double amplitude,
            double bodyDecay, double clickDecay)
        {
            int samples       = (int)(_format.SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(_format.SampleRate * 0.0005));
            var shorts = new short[samples];

            double bodyPhase = 0, bodyInc = 2 * Math.PI * bodyHz / _format.SampleRate;
            double clickPhase = 0, clickInc = 2 * Math.PI * clickHz / _format.SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t      = (double)i / _format.SampleRate;
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;

                double body  = Math.Sin(bodyPhase)  * Math.Exp(-bodyDecay  * t);
                double click = Math.Sin(clickPhase) * Math.Exp(-clickDecay * t);

                double v = (body * 0.70 + click * 0.30) * amplitude * attack;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);

                bodyPhase  += bodyInc;
                clickPhase += clickInc;
            }
            return ToBytes(shorts);
        }

        /// <summary>Percussive additive tone: sum of sine partials under a shared
        /// exponential decay envelope with a short anti-pop attack.</summary>
        private byte[] GenerateDecayTone(double[] freqs, double[] amps, double durationSec,
                                         double amplitude, double decay, double attackSec = 0.0008)
        {
            int samples       = (int)(_format.SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(_format.SampleRate * attackSec));
            var shorts = new short[samples];
            var phases = new double[freqs.Length];

            double norm = 0;
            foreach (var a in amps) norm += a;
            if (norm <= 0) norm = 1;

            for (int i = 0; i < samples; i++)
            {
                double t      = (double)i / _format.SampleRate;
                double env    = Math.Exp(-decay * t);
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;

                double v = 0;
                for (int k = 0; k < freqs.Length; k++)
                {
                    v += Math.Sin(phases[k]) * amps[k];
                    phases[k] += 2 * Math.PI * freqs[k] / _format.SampleRate;
                }
                v = v / norm * amplitude * env * attack;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
            }
            return ToBytes(shorts);
        }

        /// <summary>Flat-envelope sine "beep" with short attack/release ramps.</summary>
        private byte[] GenerateSustainTone(double freq, double durationSec, double amplitude)
        {
            int samples = (int)(_format.SampleRate * durationSec);
            int ramp    = Math.Max(1, (int)(_format.SampleRate * 0.005));
            var shorts  = new short[samples];

            double phase = 0, inc = 2 * Math.PI * freq / _format.SampleRate;
            for (int i = 0; i < samples; i++)
            {
                double env = 1.0;
                if (i < ramp)                 env = (double)i / ramp;
                else if (i > samples - ramp)  env = (double)(samples - i) / ramp;

                double v = Math.Sin(phase) * amplitude * env;
                shorts[i] = (short)(v * short.MaxValue);
                phase += inc;
            }
            return ToBytes(shorts);
        }

        /// <summary>Two detuned square oscillators under an exponential decay —
        /// the basis of a classic cowbell timbre.</summary>
        private byte[] GenerateSquarePair(double f1, double f2, double durationSec,
                                          double amplitude, double decay)
        {
            int samples = (int)(_format.SampleRate * durationSec);
            int ramp    = Math.Max(1, (int)(_format.SampleRate * 0.001));
            var shorts  = new short[samples];

            double p1 = 0, p2 = 0;
            double i1 = 2 * Math.PI * f1 / _format.SampleRate;
            double i2 = 2 * Math.PI * f2 / _format.SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t      = (double)i / _format.SampleRate;
                double env    = Math.Exp(-decay * t);
                double attack = i < ramp ? (double)i / ramp : 1.0;

                // Soft squares (0.6 weight) keep the metallic edge without harsh clipping
                double s1 = Math.Sign(Math.Sin(p1));
                double s2 = Math.Sign(Math.Sin(p2));
                double v  = (s1 + s2) / 2 * 0.6 * amplitude * env * attack;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);

                p1 += i1;
                p2 += i2;
            }
            return ToBytes(shorts);
        }

        private byte[] ToBytes(short[] shorts)
        {
            var bytes = new byte[shorts.Length * sizeof(short)];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Stop();
    }
}
